using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Typizer
{
    /// <summary>
    /// Всё, что бросает SDK, кроме ошибок аргументов (<see cref="ArgumentException"/>) и отмены
    /// (<see cref="OperationCanceledException"/>).
    /// </summary>
    public class TypizerException : Exception
    {
        public TypizerException(string message) : base(message) { }
        public TypizerException(string message, Exception? innerException) : base(message, innerException) { }

        /// <summary>Поможет ли повтор того же вызова позже.</summary>
        public virtual bool IsRetryable => false;
    }

    /// <summary>Клиент настроен неверно: например, нет ключа API ни в аргументе, ни в <c>TYPIZER_API_KEY</c>.</summary>
    public sealed class TypizerConfigurationException : TypizerException
    {
        public TypizerConfigurationException(string message) : base(message) { }
    }

    /// <summary>Ответа нет: DNS, TCP, TLS, прокси, обрыв соединения.</summary>
    public class TypizerConnectionException : TypizerException
    {
        public TypizerConnectionException(string message, Exception? innerException) : base(message, innerException) { }
        public override bool IsRetryable => true;
    }

    /// <summary>Истёк таймаут одной HTTP-попытки (<see cref="TypizerClientOptions.Timeout"/>).</summary>
    public sealed class TypizerTimeoutException : TypizerConnectionException
    {
        public TypizerTimeoutException(string message, Exception? innerException) : base(message, innerException) { }
    }

    /// <summary>Лимиты запросов из заголовков <c>RateLimit-Limit</c>, <c>RateLimit-Remaining</c>, <c>RateLimit-Reset</c>.</summary>
    public sealed class RateLimitInfo
    {
        public RateLimitInfo(int? limit, int? remaining, int? reset)
        {
            Limit = limit;
            Remaining = remaining;
            Reset = reset;
        }

        public int? Limit { get; }
        public int? Remaining { get; }
        /// <summary>Секунд до сброса окна.</summary>
        public int? Reset { get; }
    }

    /// <summary>
    /// Ответ API с ошибкой (4xx/5xx). Поля — из тела <c>application/problem+json</c>, а если тела нет
    /// или оно не JSON (лимит сетевого края, страница прокси), — из статуса и заголовков.
    /// </summary>
    public class TypizerApiException : TypizerException
    {
        internal TypizerApiException(ApiErrorData data) : base(data.FormatMessage())
        {
            StatusCode = data.StatusCode;
            Code = data.Code;
            Title = data.Title;
            Detail = data.Detail;
            Type = data.Type;
            DocUrl = data.DocUrl;
            Param = data.Param;
            RequestId = data.RequestId;
            RetryAfter = data.RetryAfter;
            RateLimit = data.RateLimit;
            Headers = data.Headers;
            Body = data.Body;
            Problem = data.Problem;
            Extra = data.Problem?.ExtensionData != null
                ? new Dictionary<string, JsonElement>(data.Problem.ExtensionData, StringComparer.Ordinal)
                : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            _retryable = data.Retryable;
        }

        private readonly bool _retryable;

        /// <summary>HTTP-статус.</summary>
        public int StatusCode { get; }
        /// <summary>Машиночитаемый код (<c>invalid_request</c>, <c>insufficient_balance</c>, <c>rate_limited</c>…) — см. <see cref="ErrorCodes"/>.</summary>
        public string Code { get; }
        public string? Title { get; }
        public string? Detail { get; }
        /// <summary>URI типа ошибки (<c>type</c> из problem+json).</summary>
        public string? Type { get; }
        /// <summary>Страница кода ошибки в документации.</summary>
        public string? DocUrl { get; }
        /// <summary>Поле запроса, из-за которого ошибка.</summary>
        public string? Param { get; }
        /// <summary>Идентификатор запроса — приложите его к обращению в поддержку.</summary>
        public string? RequestId { get; }
        public override bool IsRetryable => _retryable;
        /// <summary><c>Retry-After</c> (у 429 и 503), если пришёл.</summary>
        public TimeSpan? RetryAfter { get; }
        public RateLimitInfo? RateLimit { get; }
        /// <summary>Заголовки ответа (имена без учёта регистра).</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }
        /// <summary>Тело ответа как текст.</summary>
        public string? Body { get; }
        /// <summary>Разобранное тело problem+json или <c>null</c>.</summary>
        public Problem? Problem { get; }
        /// <summary>Прочие поля ответа: <c>batch_id</c>, <c>created</c>, <c>expected</c>, <c>findings</c>…</summary>
        public IReadOnlyDictionary<string, JsonElement> Extra { get; }
    }

    /// <summary>400: <c>invalid_request</c>, <c>invalid_id</c>, <c>invalid_schema</c>, <c>unsupported_schema_feature</c>, <c>too_many_pages</c>.</summary>
    public sealed class TypizerBadRequestException : TypizerApiException
    {
        internal TypizerBadRequestException(ApiErrorData data) : base(data) { }
    }

    /// <summary>401 <c>unauthorized</c>: ключа нет, он неверен или отозван.</summary>
    public sealed class TypizerAuthenticationException : TypizerApiException
    {
        internal TypizerAuthenticationException(ApiErrorData data) : base(data) { }
    }

    /// <summary>402 <c>insufficient_balance</c>: не хватает страниц на балансе.</summary>
    public sealed class TypizerInsufficientBalanceException : TypizerApiException
    {
        internal TypizerInsufficientBalanceException(ApiErrorData data) : base(data) { }
    }

    /// <summary>403 <c>forbidden</c>, <c>insufficient_scope</c>: у ключа нет скоупа или доступа.</summary>
    public sealed class TypizerPermissionDeniedException : TypizerApiException
    {
        internal TypizerPermissionDeniedException(ApiErrorData data) : base(data) { }
    }

    /// <summary>404 <c>not_found</c>.</summary>
    public sealed class TypizerNotFoundException : TypizerApiException
    {
        internal TypizerNotFoundException(ApiErrorData data) : base(data) { }
    }

    /// <summary>409 <c>conflict</c>.</summary>
    public class TypizerConflictException : TypizerApiException
    {
        internal TypizerConflictException(ApiErrorData data) : base(data) { }
    }

    /// <summary>
    /// 409 <c>idempotency_conflict</c>: тот же <c>Idempotency-Key</c> пришёл с другим запросом или пока
    /// первый ещё выполняется. Не повторяется автоматически.
    /// </summary>
    public sealed class TypizerIdempotencyConflictException : TypizerConflictException
    {
        internal TypizerIdempotencyConflictException(ApiErrorData data) : base(data) { }
    }

    /// <summary>413 <c>file_too_large</c>: файл больше 50 МиБ (SDK отклоняет такой файл ещё до отправки).</summary>
    public sealed class TypizerFileTooLargeException : TypizerApiException
    {
        internal TypizerFileTooLargeException(ApiErrorData data) : base(data) { }
    }

    /// <summary>415 <c>unsupported_file_type</c>.</summary>
    public sealed class TypizerUnsupportedFileTypeException : TypizerApiException
    {
        internal TypizerUnsupportedFileTypeException(ApiErrorData data) : base(data) { }
    }

    /// <summary>422 <c>document_unreadable</c>, <c>extraction_failed</c>.</summary>
    public sealed class TypizerUnprocessableDocumentException : TypizerApiException
    {
        internal TypizerUnprocessableDocumentException(ApiErrorData data) : base(data) { }
    }

    /// <summary>429 <c>rate_limited</c> после всех повторов.</summary>
    public sealed class TypizerRateLimitException : TypizerApiException
    {
        internal TypizerRateLimitException(ApiErrorData data) : base(data) { }
    }

    /// <summary>500 <c>internal_error</c> (и прочие 5xx, кроме 502–504) после всех повторов.</summary>
    public sealed class TypizerInternalServerException : TypizerApiException
    {
        internal TypizerInternalServerException(ApiErrorData data) : base(data) { }
    }

    /// <summary>503 <c>upstream_unavailable</c> (и 502, 504) после всех повторов.</summary>
    public sealed class TypizerServiceUnavailableException : TypizerApiException
    {
        internal TypizerServiceUnavailableException(ApiErrorData data) : base(data) { }
    }

    /// <summary>
    /// <see cref="TypizerClient.ExtractAsync(TypizerFile, object, System.Threading.CancellationToken)"/>:
    /// прогон завершился <c>failed</c>. Прогон — в <see cref="Run"/>; повторить — <c>client.Runs.RetryAsync(run.Id)</c>.
    /// </summary>
    public sealed class TypizerRunFailedException : TypizerException
    {
        public TypizerRunFailedException(Run run)
            : base(Format(run ?? throw new ArgumentNullException(nameof(run))))
        {
            Run = run;
            Code = run.Error?.Code ?? "unknown";
            ErrorMessage = run.Error?.Message ?? string.Empty;
        }

        /// <summary>Прогон в статусе <c>failed</c>.</summary>
        public Run Run { get; }
        /// <summary><c>run.error.code</c>: <c>document_unreadable</c>, <c>too_many_pages</c>, <c>extraction_failed</c>, <c>upstream_unavailable</c>, <c>internal_error</c>, <c>timeout</c>, <c>insufficient_balance</c>…</summary>
        public string Code { get; }
        /// <summary><c>run.error.message</c>.</summary>
        public string ErrorMessage { get; }
        /// <summary>Истинно для <c>timeout</c>, <c>upstream_unavailable</c>, <c>internal_error</c>: повторите через <c>client.Runs.RetryAsync(id)</c>.</summary>
        public override bool IsRetryable =>
            Code == "timeout" || Code == ErrorCodes.UpstreamUnavailable || Code == ErrorCodes.InternalError;

        private static string Format(Run run) =>
            "[" + (run.Error?.Code ?? "unknown") + "] " + (run.Error?.Message ?? "Run failed") + " (run_id=" + run.Id + ")";
    }

    /// <summary>
    /// Срок ожидания прогона истёк. Прогон при этом продолжается и будет оплачен: дождитесь его через
    /// <c>client.Runs.WaitAsync(RunId)</c> или вебхук.
    /// </summary>
    public sealed class TypizerWaitTimeoutException : TypizerException
    {
        public TypizerWaitTimeoutException(Run run, TimeSpan waited)
            : base(string.Format(CultureInfo.InvariantCulture,
                "Run {0} is still {1} after {2:0.#} s. It keeps running and will be billed: wait for it with client.Runs.WaitAsync(\"{0}\") or a webhook.",
                (run ?? throw new ArgumentNullException(nameof(run))).Id, run.Status, waited.TotalSeconds))
        {
            Run = run;
            RunId = run.Id;
            Waited = waited;
        }

        public string RunId { get; }
        /// <summary>Последнее известное состояние прогона.</summary>
        public Run Run { get; }
        public TimeSpan Waited { get; }
    }

    /// <summary>
    /// <see cref="TypizerWebhooks.Verify(string, IEnumerable{KeyValuePair{string, string}}, string, TimeSpan?, DateTimeOffset?)"/>:
    /// подпись или время не прошли проверку. Отвечайте на такой запрос 400 и не обрабатывайте тело.
    /// </summary>
    public sealed class TypizerWebhookVerificationException : TypizerException
    {
        public TypizerWebhookVerificationException(string reason, string message) : base(message)
        {
            Reason = reason;
        }

        /// <summary><c>missing_header</c>, <c>invalid_timestamp</c>, <c>timestamp_out_of_tolerance</c>, <c>signature_mismatch</c>, <c>invalid_secret</c>, <c>invalid_payload</c>.</summary>
        public string Reason { get; }
    }

    /// <summary>Коды ошибок API (<c>Problem.code</c>). Список открытый: сравнивайте со строкой.</summary>
    public static class ErrorCodes
    {
        public const string InvalidRequest = "invalid_request";
        public const string InvalidId = "invalid_id";
        public const string InvalidSchema = "invalid_schema";
        public const string UnsupportedSchemaFeature = "unsupported_schema_feature";
        public const string FileTooLarge = "file_too_large";
        public const string UnsupportedFileType = "unsupported_file_type";
        public const string TooManyPages = "too_many_pages";
        public const string Unauthorized = "unauthorized";
        public const string Forbidden = "forbidden";
        public const string InsufficientScope = "insufficient_scope";
        public const string NotFound = "not_found";
        public const string Conflict = "conflict";
        public const string IdempotencyConflict = "idempotency_conflict";
        public const string InsufficientBalance = "insufficient_balance";
        public const string RateLimited = "rate_limited";
        public const string ExtractionFailed = "extraction_failed";
        public const string DocumentUnreadable = "document_unreadable";
        public const string UpstreamUnavailable = "upstream_unavailable";
        public const string InternalError = "internal_error";
    }

    /// <summary>Everything an API error carries, collected before choosing its exception class.</summary>
    internal sealed class ApiErrorData
    {
        public int StatusCode;
        public string Code = string.Empty;
        public string? Title;
        public string? Detail;
        public string? Type;
        public string? DocUrl;
        public string? Param;
        public string? RequestId;
        public bool Retryable;
        public TimeSpan? RetryAfter;
        public RateLimitInfo? RateLimit;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers = new Dictionary<string, IReadOnlyList<string>>();
        public string? Body;
        public Problem? Problem;

        /// <summary><c>[code] detail (HTTP 402, request_id=req_…, https://docs.typizer.ru/errors/code)</c></summary>
        public string FormatMessage()
        {
            var parts = new List<string> { "HTTP " + StatusCode.ToString(CultureInfo.InvariantCulture) };
            if (!string.IsNullOrEmpty(RequestId)) parts.Add("request_id=" + RequestId);
            if (!string.IsNullOrEmpty(DocUrl)) parts.Add(DocUrl!);
            string text = !string.IsNullOrEmpty(Detail) ? Detail! : !string.IsNullOrEmpty(Title) ? Title! : "HTTP error";
            return "[" + Code + "] " + text + " (" + string.Join(", ", parts) + ")";
        }
    }
}
