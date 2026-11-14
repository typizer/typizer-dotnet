using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Typizer.Internal
{
    /// <summary>One API call: method, path, query and a body factory (a fresh body per attempt).</summary>
    internal sealed class ApiRequest
    {
        public ApiRequest(string operationId, HttpMethod method, string path)
        {
            OperationId = operationId;
            Method = method;
            Path = path;
        }

        public string OperationId { get; }
        public HttpMethod Method { get; }
        public string Path { get; }
        public List<KeyValuePair<string, string>> Query { get; } = new List<KeyValuePair<string, string>>();
        public Func<HttpContent>? Body { get; set; }
        /// <summary>A file download: <c>Accept: */*</c>, the body is handed out as a stream.</summary>
        public bool Download { get; set; }
        /// <summary>Raises the attempt timeout (creating a run with <c>wait</c>: wait + 15 s).</summary>
        public TimeSpan? MinAttemptTimeout { get; set; }
        /// <summary>Overall deadline (Clock ticks) of an <c>ExtractAsync</c>: retry pauses never cross it.</summary>
        public long? Deadline { get; set; }

        public ApiRequest With(string name, string? value)
        {
            if (value != null) Query.Add(new KeyValuePair<string, string>(name, value));
            return this;
        }
    }

    /// <summary>
    /// Sending with SPEC §6–§7: Authorization and User-Agent, Idempotency-Key on every POST, a timeout
    /// per attempt, retries of connection errors, 408, 429, 500 and 502–504 with Retry-After or
    /// exponential backoff, typed exceptions, and the log lines of §10 (never a key or a body).
    /// </summary>
    internal sealed class HttpPipeline
    {
        private static readonly HttpMethod Patch = new HttpMethod("PATCH");
        private readonly HttpClient _http;
        private readonly TypizerClientOptions _options;
        private readonly string _baseUrl;
        private readonly string _userAgent;
        private readonly ConcurrentDictionary<string, bool> _maintenanceSeen;
        [ThreadStatic] private static Random? _random;

        public HttpPipeline(HttpClient http, TypizerClientOptions options, string baseUrl, ConcurrentDictionary<string, bool> maintenanceSeen)
        {
            _http = http;
            _options = options;
            _baseUrl = baseUrl;
            _userAgent = UserAgent.Build(options.AppInfo);
            _maintenanceSeen = maintenanceSeen;
        }

        public string BaseUrl => _baseUrl;

        public async Task<TypizerResponse<T>> SendJsonAsync<T>(ApiRequest request, RequestOptions? options, CancellationToken cancellationToken)
        {
            using (Raw raw = await SendAsync(request, options, cancellationToken).ConfigureAwait(false))
            {
                T value;
                try
                {
                    value = Json.Deserialize<T>(raw.Body!);
                }
                catch (JsonException ex)
                {
                    throw new TypizerException(
                        "The response to " + request.Method + " " + request.Path + " is not the expected JSON (HTTP " +
                        raw.StatusCode.ToString(CultureInfo.InvariantCulture) + ", request_id=" + raw.RequestId + "): " + ex.Message, ex);
                }
                return new TypizerResponse<T>(value, raw.StatusCode, raw.Headers);
            }
        }

        public async Task<TypizerDownload> SendDownloadAsync(ApiRequest request, RequestOptions? options, CancellationToken cancellationToken)
        {
            request.Download = true;
            Raw raw = await SendAsync(request, options, cancellationToken).ConfigureAwait(false);
            return new TypizerDownload(raw.Response!, raw.Stream!, raw.Headers);
        }

        internal sealed class Raw : IDisposable
        {
            public int StatusCode;
            public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers = new Dictionary<string, IReadOnlyList<string>>();
            public byte[]? Body;
            public HttpResponseMessage? Response;
            public Stream? Stream;
            public string? RequestId;

            public void Dispose()
            {
                if (Stream == null) Response?.Dispose();
            }
        }

        public async Task<Raw> SendAsync(ApiRequest request, RequestOptions? options, CancellationToken cancellationToken)
        {
            int maxRetries = options?.MaxRetries ?? _options.MaxRetries;
            if (maxRetries < 0) throw new ArgumentOutOfRangeException(nameof(options), "MaxRetries не может быть отрицательным");
            TimeSpan attemptTimeout = options?.Timeout ?? _options.Timeout;
            if (attemptTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options), "Timeout должен быть больше нуля");
            if (request.MinAttemptTimeout.HasValue && request.MinAttemptTimeout.Value > attemptTimeout)
                attemptTimeout = request.MinAttemptTimeout.Value;

            string? idempotencyKey = null;
            if (request.Method == HttpMethod.Post)
            {
                idempotencyKey = options?.IdempotencyKey;
                IdempotencyKeys.Validate(idempotencyKey, nameof(RequestOptions.IdempotencyKey));
                idempotencyKey ??= IdempotencyKeys.New();
            }

            string url = _baseUrl + request.Path + Url.Query(request.Query);
            int attempts = maxRetries + 1;
            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long started = Clock.Now();
                Exception? failure;
                TimeSpan? retryAfter = null;
                string reason;
                string? requestId = null;
                bool canRetry;

                using (var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    attemptCts.CancelAfter(attemptTimeout);
                    HttpRequestMessage message = BuildMessage(request, url, idempotencyKey, options);
                    HttpResponseMessage? response = null;
                    try
                    {
                        response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token).ConfigureAwait(false);
                        var headers = TypizerResponse.CollectHeaders(response);
                        requestId = First(headers, "X-Request-Id");
                        int status = (int)response.StatusCode;
                        WarnMaintenance(headers);

                        if (status >= 200 && status < 300)
                        {
                            var raw = new Raw { StatusCode = status, Headers = headers, Response = response, RequestId = requestId };
                            if (request.Download)
                            {
                                raw.Stream = await ReadStreamAsync(response).ConfigureAwait(false);
                            }
                            else
                            {
                                raw.Body = await ReadBodyAsync(response, attemptCts.Token).ConfigureAwait(false);
                            }
                            LogAttempt(request, status.ToString(CultureInfo.InvariantCulture), started, requestId, attempt, attempts);
                            message.Dispose();
                            return raw;
                        }

                        byte[] errorBody = await ReadBodyAsync(response, attemptCts.Token).ConfigureAwait(false);
                        TypizerApiException error = Errors.FromResponse(status, headers, errorBody);
                        LogAttempt(request, status.ToString(CultureInfo.InvariantCulture), started, requestId, attempt, attempts);
                        response.Dispose();
                        message.Dispose();
                        failure = error;
                        retryAfter = error.RetryAfter;
                        reason = status.ToString(CultureInfo.InvariantCulture) + " " + error.Code;
                        canRetry = error.IsRetryable;
                        if (canRetry && retryAfter.HasValue && retryAfter.Value > _options.MaxRetryDelay)
                            throw error;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        response?.Dispose();
                        message.Dispose();
                        throw;
                    }
                    catch (OperationCanceledException ex)
                    {
                        // Our attempt timer (or HttpClient.Timeout of a caller's client) fired.
                        response?.Dispose();
                        message.Dispose();
                        LogAttempt(request, "timeout", started, null, attempt, attempts);
                        failure = new TypizerTimeoutException(
                            "No response to " + request.Method + " " + request.Path + " within " +
                            attemptTimeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " s", ex);
                        reason = "timeout";
                        canRetry = request.Method != Patch;
                    }
                    catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is SocketException || ex is ObjectDisposedException && !cancellationToken.IsCancellationRequested)
                    {
                        response?.Dispose();
                        message.Dispose();
                        LogAttempt(request, "connection error", started, null, attempt, attempts);
                        failure = new TypizerConnectionException(
                            "Could not reach the Typizer API (" + request.Method + " " + request.Path + "): " + ex.Message, ex);
                        reason = "connection error";
                        canRetry = request.Method != Patch || NotSent(ex);
                    }
                }

                if (!canRetry || attempt >= attempts) throw failure;

                TimeSpan delay = retryAfter ?? Backoff(attempt - 1);
                if (request.Deadline.HasValue && delay >= Clock.Remaining(request.Deadline.Value)) throw failure;
                Log(TypizerLogLevel.Information, string.Format(CultureInfo.InvariantCulture,
                    "typizer: retrying {0} {1} in {2:0.0}s after {3} (attempt {4}/{5}{6})",
                    request.Method, request.Path, delay.TotalSeconds, reason, attempt + 1, attempts,
                    requestId != null ? ", request_id=" + requestId : string.Empty));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        private HttpRequestMessage BuildMessage(ApiRequest request, string url, string? idempotencyKey, RequestOptions? options)
        {
            var message = new HttpRequestMessage(request.Method, url);
            HttpRequestHeaders headers = message.Headers;
            foreach (KeyValuePair<string, string> header in _options.DefaultHeaders)
                SetHeader(headers, header.Key, header.Value);
            if (options?.ExtraHeaders != null)
            {
                foreach (KeyValuePair<string, string> header in options.ExtraHeaders)
                    SetHeader(headers, header.Key, header.Value);
            }
            if (!headers.Contains("User-Agent")) headers.TryAddWithoutValidation("User-Agent", _userAgent);
            headers.Remove("Accept");
            headers.TryAddWithoutValidation("Accept", request.Download ? "*/*" : "application/json");
            headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            if (idempotencyKey != null)
            {
                headers.Remove("Idempotency-Key");
                headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            }
            if (request.Body != null) message.Content = request.Body();
            return message;
        }

        private static void SetHeader(HttpRequestHeaders headers, string name, string value)
        {
            if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase)) return;
            headers.Remove(name);
            headers.TryAddWithoutValidation(name, value);
        }

        private static async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.Content == null) return Array.Empty<byte>();
#if NET8_0_OR_GREATER
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
#else
            // netstandard2.0 has no cancellable read: dispose the response to abort it.
            using (cancellationToken.Register(response.Dispose))
            {
                try
                {
                    return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
#endif
        }

        private static Task<Stream> ReadStreamAsync(HttpResponseMessage response) =>
            response.Content != null ? response.Content.ReadAsStreamAsync() : Task.FromResult<Stream>(new MemoryStream());

        /// <summary>A connection error that happened before the request left (so even PATCH may be retried).</summary>
        private static bool NotSent(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
#if NET8_0_OR_GREATER
                if (e is HttpRequestException hre &&
                    (hre.HttpRequestError == HttpRequestError.ConnectionError ||
                     hre.HttpRequestError == HttpRequestError.NameResolutionError ||
                     hre.HttpRequestError == HttpRequestError.SecureConnectionError ||
                     hre.HttpRequestError == HttpRequestError.ProxyTunnelError))
                    return true;
#endif
                if (e is SocketException se &&
                    (se.SocketErrorCode == SocketError.ConnectionRefused || se.SocketErrorCode == SocketError.HostNotFound ||
                     se.SocketErrorCode == SocketError.HostUnreachable || se.SocketErrorCode == SocketError.NetworkUnreachable ||
                     se.SocketErrorCode == SocketError.TryAgain))
                    return true;
                if (e is WebException we &&
                    (we.Status == WebExceptionStatus.ConnectFailure || we.Status == WebExceptionStatus.NameResolutionFailure ||
                     we.Status == WebExceptionStatus.ProxyNameResolutionFailure))
                    return true;
            }
            return false;
        }

        /// <summary>SPEC §7.2: min(8 s, 0.5 s × 2^n) × random(0.75, 1.0), n from 0.</summary>
        internal static TimeSpan Backoff(int retry)
        {
            double baseSeconds = Math.Min(8.0, 0.5 * Math.Pow(2, Math.Min(retry, 10)));
            Random random = _random ??= new Random(Guid.NewGuid().GetHashCode());
            return TimeSpan.FromSeconds(baseSeconds * (0.75 + 0.25 * random.NextDouble()));
        }

        private void WarnMaintenance(IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
        {
            string? value = First(headers, "Typizer-Maintenance");
            if (value == null) return;
            string id = value.Split(';')[0].Trim();
            if (_maintenanceSeen.TryAdd(id, true))
                Log(TypizerLogLevel.Warning, "typizer: scheduled maintenance announced: " + value);
        }

        private void LogAttempt(ApiRequest request, string outcome, long started, string? requestId, int attempt, int attempts)
        {
            if (_options.Logger == null) return;
            Log(TypizerLogLevel.Debug, string.Format(CultureInfo.InvariantCulture,
                "typizer: {0} {1} -> {2} in {3:0.00}s (request_id={4}, attempt {5}/{6})",
                request.Method, request.Path, outcome, Clock.Since(started).TotalSeconds, requestId ?? "-", attempt, attempts));
        }

        private void Log(TypizerLogLevel level, string message)
        {
            Action<TypizerLogLevel, string>? logger = _options.Logger;
            if (logger == null) return;
            try
            {
                logger(level, message);
            }
            catch (Exception)
            {
                // A broken logger must not break API calls.
            }
        }

        internal static string? First(IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string name) =>
            headers.TryGetValue(name, out IReadOnlyList<string>? values) && values.Count > 0 ? values[0] : null;

        internal static RateLimitInfo? ParseRateLimit(IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
        {
            int? limit = Int(First(headers, "RateLimit-Limit"));
            int? remaining = Int(First(headers, "RateLimit-Remaining"));
            int? reset = Int(First(headers, "RateLimit-Reset"));
            return limit == null && remaining == null && reset == null ? null : new RateLimitInfo(limit, remaining, reset);
        }

        private static int? Int(string? value) =>
            value != null && int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : (int?)null;
    }

    /// <summary>Response → exception class (SPEC §6.2).</summary>
    internal static class Errors
    {
        public static TypizerApiException FromResponse(int status, IReadOnlyDictionary<string, IReadOnlyList<string>> headers, byte[] body)
        {
            var data = new ApiErrorData { StatusCode = status, Headers = headers };
            string text = body.Length == 0 ? string.Empty : Encoding.UTF8.GetString(body);
            data.Body = text;
            Problem? problem = null;
            string? contentType = HttpPipeline.First(headers, "Content-Type");
            if (text.Length > 0 && contentType != null && contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try
                {
                    problem = JsonSerializer.Deserialize<Problem>(body, Json.ModelOptions);
                }
                catch (JsonException)
                {
                    problem = null;
                }
            }
            if (problem != null && !string.IsNullOrEmpty(problem.Code))
            {
                data.Problem = problem;
                data.Code = problem.Code;
                data.Title = NullIfEmpty(problem.Title);
                data.Detail = NullIfEmpty(problem.Detail);
                data.Type = NullIfEmpty(problem.Type);
                data.DocUrl = NullIfEmpty(problem.DocUrl);
                data.Param = NullIfEmpty(problem.Param);
                data.RequestId = NullIfEmpty(problem.RequestId) ?? HttpPipeline.First(headers, "X-Request-Id");
            }
            else
            {
                data.Code = CodeForStatus(status);
                data.Detail = text.Length > 200 ? text.Substring(0, 200) : NullIfEmpty(text.Trim());
                data.RequestId = HttpPipeline.First(headers, "X-Request-Id");
            }
            data.RetryAfter = ParseRetryAfter(HttpPipeline.First(headers, "Retry-After"));
            data.RateLimit = HttpPipeline.ParseRateLimit(headers);
            data.Retryable = status == 408 || status == 429 || status == 500 || status == 502 || status == 503 || status == 504 ||
                             data.Code == ErrorCodes.RateLimited || data.Code == ErrorCodes.UpstreamUnavailable || data.Code == ErrorCodes.InternalError;
            return Create(data);
        }

        public static TypizerFileTooLargeException FileTooLarge(long length) =>
            new TypizerFileTooLargeException(new ApiErrorData
            {
                StatusCode = 413,
                Code = ErrorCodes.FileTooLarge,
                Title = "File too large",
                Detail = "The file is " + length.ToString(CultureInfo.InvariantCulture) + " bytes; the limit is " +
                         TypizerFile.MaxBytes.ToString(CultureInfo.InvariantCulture) + " bytes (50 MiB). Nothing was sent.",
                DocUrl = "https://docs.typizer.ru/errors/file_too_large",
                Param = "file",
            });

        private static TypizerApiException Create(ApiErrorData data)
        {
            switch (data.Code)
            {
                case ErrorCodes.InvalidRequest when data.StatusCode == 408:
                    return new TypizerApiException(data);
                case ErrorCodes.InvalidRequest:
                case ErrorCodes.InvalidId:
                case ErrorCodes.InvalidSchema:
                case ErrorCodes.UnsupportedSchemaFeature:
                case ErrorCodes.TooManyPages:
                    return new TypizerBadRequestException(data);
                case ErrorCodes.Unauthorized: return new TypizerAuthenticationException(data);
                case ErrorCodes.InsufficientBalance: return new TypizerInsufficientBalanceException(data);
                case ErrorCodes.Forbidden:
                case ErrorCodes.InsufficientScope:
                    return new TypizerPermissionDeniedException(data);
                case ErrorCodes.NotFound: return new TypizerNotFoundException(data);
                case ErrorCodes.Conflict: return new TypizerConflictException(data);
                case ErrorCodes.IdempotencyConflict: return new TypizerIdempotencyConflictException(data);
                case ErrorCodes.FileTooLarge: return new TypizerFileTooLargeException(data);
                case ErrorCodes.UnsupportedFileType: return new TypizerUnsupportedFileTypeException(data);
                case ErrorCodes.DocumentUnreadable:
                case ErrorCodes.ExtractionFailed:
                    return new TypizerUnprocessableDocumentException(data);
                case ErrorCodes.RateLimited: return new TypizerRateLimitException(data);
                case ErrorCodes.InternalError: return new TypizerInternalServerException(data);
                case ErrorCodes.UpstreamUnavailable: return new TypizerServiceUnavailableException(data);
            }
            // A code this SDK does not know yet: the class follows the HTTP status.
            switch (data.StatusCode)
            {
                case 400: return new TypizerBadRequestException(data);
                case 401: return new TypizerAuthenticationException(data);
                case 402: return new TypizerInsufficientBalanceException(data);
                case 403: return new TypizerPermissionDeniedException(data);
                case 404: return new TypizerNotFoundException(data);
                case 409: return new TypizerConflictException(data);
                case 413: return new TypizerFileTooLargeException(data);
                case 415: return new TypizerUnsupportedFileTypeException(data);
                case 422: return new TypizerUnprocessableDocumentException(data);
                case 429: return new TypizerRateLimitException(data);
                case 502:
                case 503:
                case 504:
                    return new TypizerServiceUnavailableException(data);
            }
            return data.StatusCode >= 500 ? new TypizerInternalServerException(data) : new TypizerApiException(data);
        }

        private static string CodeForStatus(int status)
        {
            switch (status)
            {
                case 400: return ErrorCodes.InvalidRequest;
                case 401: return ErrorCodes.Unauthorized;
                case 402: return ErrorCodes.InsufficientBalance;
                case 403: return ErrorCodes.Forbidden;
                case 404: return ErrorCodes.NotFound;
                case 408: return ErrorCodes.InvalidRequest;
                case 409: return ErrorCodes.Conflict;
                case 413: return ErrorCodes.FileTooLarge;
                case 415: return ErrorCodes.UnsupportedFileType;
                case 422: return ErrorCodes.ExtractionFailed;
                case 429: return ErrorCodes.RateLimited;
                case 502:
                case 503:
                case 504:
                    return ErrorCodes.UpstreamUnavailable;
                default:
                    return status >= 500 ? ErrorCodes.InternalError : "http_" + status.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary><c>Retry-After</c>: seconds or an HTTP date.</summary>
        internal static TimeSpan? ParseRetryAfter(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (double.TryParse(value!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
                return seconds < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
            if (DateTimeOffset.TryParseExact(value.Trim(), "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date) ||
                DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date))
            {
                TimeSpan wait = date - DateTimeOffset.UtcNow;
                return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
            }
            return null;
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
