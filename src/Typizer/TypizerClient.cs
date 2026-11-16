using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Typizer.Internal;

namespace Typizer
{
    /// <summary>
    /// Клиент Typizer API. Один на приложение: потокобезопасен и переиспользует соединения.
    /// <code>
    /// using var client = new TypizerClient(Environment.GetEnvironmentVariable("TYPIZER_API_KEY"));
    /// Run run = await client.ExtractAsync(TypizerFile.FromPath("invoice.pdf"), schema, ct);
    /// </code>
    /// </summary>
    [DebuggerDisplay("{ToString(),nq}")]
    public sealed class TypizerClient : IDisposable
    {
        /// <summary>Версия SDK.</summary>
        public const string Version = "0.2.0";

        private readonly TypizerClientOptions _options;
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly HttpPipeline _pipeline;
        private readonly ConcurrentDictionary<string, bool> _maintenanceSeen;

        /// <summary>Клиент с ключом из переменной окружения <c>TYPIZER_API_KEY</c>.</summary>
        public TypizerClient() : this(new TypizerClientOptions()) { }

        /// <summary>Клиент с ключом API (<c>tz_live_…</c>); остальное — по умолчанию.</summary>
        public TypizerClient(string apiKey) : this(new TypizerClientOptions { ApiKey = apiKey ?? throw MissingKey() }) { }

        /// <summary>Клиент с настройками.</summary>
        public TypizerClient(TypizerClientOptions options)
            : this(options, null, null, null) { }

        /// <summary>
        /// Клиент поверх вашего <see cref="System.Net.Http.HttpClient"/> (например, типизированного клиента
        /// <c>IHttpClientFactory</c>). SDK его не закрывает.
        /// </summary>
        public TypizerClient(TypizerClientOptions options, HttpClient httpClient)
            : this(options, httpClient ?? throw new ArgumentNullException(nameof(httpClient)), null, null) { }

        private TypizerClient(TypizerClientOptions options, HttpClient? sharedHttp, ConcurrentDictionary<string, bool>? maintenanceSeen, string? unused)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options.Clone();
            _options.ApiKey = ResolveKey(_options.ApiKey);
            string baseUrl = ResolveBaseUrl(_options.BaseUrl);
            _options.BaseUrl = new Uri(baseUrl);
            if (_options.Timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options), "Timeout должен быть больше нуля");
            if (_options.MaxRetries < 0) throw new ArgumentOutOfRangeException(nameof(options), "MaxRetries не может быть отрицательным");

            if (sharedHttp != null)
            {
                _http = sharedHttp;
                _ownsHttp = false;
            }
            else if (_options.HttpClient != null)
            {
                if (_options.HttpMessageHandler != null || _options.Proxy != null)
                    throw new ArgumentException("HttpClient нельзя сочетать с HttpMessageHandler или Proxy: прокси и TLS настраиваются в вашем HttpClient.", nameof(options));
                _http = _options.HttpClient;
                _ownsHttp = false;
            }
            else if (_options.HttpMessageHandler != null)
            {
                if (_options.Proxy != null)
                    throw new ArgumentException("Proxy задаётся в вашем HttpMessageHandler, а не рядом с ним.", nameof(options));
                _http = new HttpClient(_options.HttpMessageHandler, disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
                _ownsHttp = true;
            }
            else
            {
                _http = new HttpClient(CreateDefaultHandler(_options.Proxy, _options.ConnectTimeout), disposeHandler: true)
                {
                    Timeout = System.Threading.Timeout.InfiniteTimeSpan,
                };
                _ownsHttp = true;
            }

            _maintenanceSeen = maintenanceSeen ?? new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
            _pipeline = new HttpPipeline(_http, _options, baseUrl, _maintenanceSeen);
            Runs = new RunsResource(_pipeline);
            Files = new FilesResource(_pipeline);
            Typizers = new TypizersResource(_pipeline);
            Catalog = new CatalogResource(_pipeline);
            Batches = new BatchesResource(_pipeline);
            WebhookEndpoints = new WebhookEndpointsResource(_pipeline);
        }

        /// <summary>Прогоны.</summary>
        public RunsResource Runs { get; }
        /// <summary>Загруженные файлы.</summary>
        public FilesResource Files { get; }
        /// <summary>Типизаторы — сохранённые схемы.</summary>
        public TypizersResource Typizers { get; }
        /// <summary>Каталог готовых схем.</summary>
        public CatalogResource Catalog { get; }
        /// <summary>Пакеты документов.</summary>
        public BatchesResource Batches { get; }
        /// <summary>Конечные точки вебхуков и их доставки.</summary>
        public WebhookEndpointsResource WebhookEndpoints { get; }

        /// <summary>Адрес API, к которому обращается клиент.</summary>
        public Uri BaseUrl => _options.BaseUrl!;

        /// <summary>
        /// Копия клиента с другими таймаутами, повторами, заголовками или журналом — на тех же соединениях.
        /// <code>var patient = client.WithOptions(o => o.Timeout = TimeSpan.FromMinutes(3));</code>
        /// </summary>
        public TypizerClient WithOptions(Action<TypizerClientOptions> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            TypizerClientOptions copy = _options.Clone();
            configure(copy);
            if (!ReferenceEquals(copy.HttpClient, _options.HttpClient) || !ReferenceEquals(copy.HttpMessageHandler, _options.HttpMessageHandler) ||
                !ReferenceEquals(copy.Proxy, _options.Proxy))
                throw new ArgumentException("WithOptions делит соединения с исходным клиентом: HttpClient, HttpMessageHandler и Proxy в нём не меняются.", nameof(configure));
            return new TypizerClient(copy, _http, _maintenanceSeen, null);
        }

        // ---- The helper (SPEC §4) --------------------------------------------------------------

        /// <summary>
        /// Документ и схема → данные: создаёт прогон и дожидается его, возвращая <see cref="Run"/> со
        /// <see cref="Run.Output"/> и <see cref="Run.Fields"/>. <paramref name="schema"/> — JSON Schema
        /// (JSON-текст, <c>JsonElement</c>, <c>JsonNode</c>, словарь, объект) или <see cref="SavedTypizer"/>.
        /// Схема из каталога — через <see cref="RunOptionsBase.Catalog"/>.
        /// </summary>
        /// <exception cref="TypizerRunFailedException">Прогон завершился <c>failed</c>.</exception>
        /// <exception cref="TypizerWaitTimeoutException">За 300 с прогон не завершился (он продолжается и будет оплачен).</exception>
        public Task<Run> ExtractAsync(TypizerFile file, object schema, CancellationToken cancellationToken = default) =>
            ExtractAsync(file, schema, new ExtractOptions(), cancellationToken);

        /// <inheritdoc cref="ExtractAsync(TypizerFile, object, CancellationToken)"/>
        public Task<Run> ExtractAsync(TypizerFile file, object schema, ExtractOptions options, CancellationToken cancellationToken = default)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema), "Схема не задана: передайте JSON Schema или используйте ExtractOptions.Catalog/Typizer.");
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (schema is ExtractOptions || schema is RunCreateOptions)
                throw new ArgumentException("Настройки передаются в параметре options, а не вместо схемы.", nameof(schema));
            if (options.Schema != null || options.Catalog != null || options.Typizer != null)
                throw new ArgumentException("Схема задана и аргументом, и в ExtractOptions: оставьте что-то одно.", nameof(options));
            ExtractOptions effective = Copy(options);
            if (schema is SavedTypizer typizer) effective.Typizer = typizer.Id;
            else effective.Schema = schema;
            return ExtractAsync(file, effective, cancellationToken);
        }

        /// <summary>
        /// Документ → данные по схеме из <paramref name="options"/>: ровно одно из <see cref="RunOptionsBase.Schema"/>,
        /// <see cref="RunOptionsBase.Catalog"/> (<c>"schet-faktura@1"</c>), <see cref="RunOptionsBase.Typizer"/> (<c>"tz_…"</c>).
        /// </summary>
        /// <exception cref="TypizerRunFailedException">Прогон завершился <c>failed</c>.</exception>
        /// <exception cref="TypizerWaitTimeoutException">Срок <see cref="ExtractOptions.Timeout"/> истёк (прогон продолжается и будет оплачен).</exception>
        public async Task<Run> ExtractAsync(TypizerFile file, ExtractOptions options, CancellationToken cancellationToken = default)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (options == null) throw new ArgumentNullException(nameof(options));
            RunsResource.ValidateTarget(options);
            IdempotencyKeys.Validate(options.IdempotencyKey, nameof(options.IdempotencyKey));
            TimeSpan timeout = RunsResource.CheckPositive(options.Timeout ?? RunsResource.DefaultWaitTimeout, nameof(options.Timeout));
            TimeSpan pollInterval = RunsResource.CheckPositive(options.PollInterval ?? RunsResource.DefaultPollInterval, nameof(options.PollInterval));

            long started = Clock.Now();
            long deadline = Clock.After(timeout);
            // One key for the whole call, retries included (SPEC §4.4).
            var create = new RequestOptions
            {
                IdempotencyKey = options.IdempotencyKey ?? IdempotencyKeys.New(),
                Timeout = options.RequestTimeout,
                MaxRetries = options.MaxRetries,
                ExtraHeaders = options.ExtraHeaders,
            };
            int wait = (int)Math.Max(0, Math.Min(60, Math.Floor(Clock.Remaining(deadline).TotalSeconds)));
            Run run = (await Runs.CreateCoreAsync(file, options, wait, create, deadline, cancellationToken).ConfigureAwait(false)).Value;

            var poll = new RequestOptions { Timeout = options.RequestTimeout, MaxRetries = options.MaxRetries, ExtraHeaders = options.ExtraHeaders };
            run = await Runs.PollAsync(run, started, deadline, pollInterval, poll, cancellationToken).ConfigureAwait(false);
            if (run.Status == RunStatus.Failed) throw new TypizerRunFailedException(run);
            return run;
        }

        private static ExtractOptions Copy(ExtractOptions o) => new ExtractOptions
        {
            Schema = o.Schema,
            Catalog = o.Catalog,
            Typizer = o.Typizer,
            Instructions = o.Instructions,
            Mode = o.Mode,
            Format = o.Format,
            Metadata = o.Metadata,
            WebhookUrl = o.WebhookUrl,
            Timeout = o.Timeout,
            PollInterval = o.PollInterval,
            IdempotencyKey = o.IdempotencyKey,
            RequestTimeout = o.RequestTimeout,
            MaxRetries = o.MaxRetries,
            ExtraHeaders = o.ExtraHeaders,
        };

        // ---- Account -----------------------------------------------------------------------------

        /// <summary>Кто вызывает API: организация, ключ и его скоупы (<c>GET /v1/me</c>).</summary>
        public async Task<Me> GetMeAsync(RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await GetMeWithResponseAsync(requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetMeAsync"/>
        public Task<TypizerResponse<Me>> GetMeWithResponseAsync(RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<Me>(new ApiRequest("getMe", Http.Get, "/v1/me"), requestOptions, cancellationToken);

        /// <summary>Организация ключа и её настройки хранения (<c>GET /v1/organization</c>).</summary>
        public async Task<Organization> GetOrganizationAsync(RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await GetOrganizationWithResponseAsync(requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetOrganizationAsync"/>
        public Task<TypizerResponse<Organization>> GetOrganizationWithResponseAsync(RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<Organization>(new ApiRequest("getOrganization", Http.Get, "/v1/organization"), requestOptions, cancellationToken);

        /// <summary>Баланс страниц (<c>GET /v1/balance</c>).</summary>
        public async Task<Balance> GetBalanceAsync(RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await GetBalanceWithResponseAsync(requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetBalanceAsync"/>
        public Task<TypizerResponse<Balance>> GetBalanceWithResponseAsync(RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<Balance>(new ApiRequest("getBalance", Http.Get, "/v1/balance"), requestOptions, cancellationToken);

        /// <summary>Расход за период по дням, типизаторам или ключам (<c>GET /v1/usage</c>).</summary>
        public async Task<Usage> GetUsageAsync(UsageOptions? options = null, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await GetUsageWithResponseAsync(options, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetUsageAsync"/>
        public Task<TypizerResponse<Usage>> GetUsageWithResponseAsync(UsageOptions? options = null, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<Usage>(new ApiRequest("getUsage", Http.Get, "/v1/usage")
                    .With("from", options?.From != null ? Url.Date(options.From.Value) : null)
                    .With("to", options?.To != null ? Url.Date(options.To.Value) : null)
                    .With("group_by", options?.GroupBy),
                requestOptions, cancellationToken);

        /// <summary>Плановые работы и инциденты (<c>GET /v1/status</c>; ключ не нужен).</summary>
        public async Task<Status> GetStatusAsync(RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await GetStatusWithResponseAsync(requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetStatusAsync"/>
        public Task<TypizerResponse<Status>> GetStatusWithResponseAsync(RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<Status>(new ApiRequest("getStatus", Http.Get, "/v1/status"), requestOptions, cancellationToken);

        /// <summary>Описание публичного API в OpenAPI 3.1 (<c>GET /v1/openapi.json</c>).</summary>
        public async Task<JsonElement> GetOpenApiDocumentAsync(RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await GetOpenApiDocumentWithResponseAsync(requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetOpenApiDocumentAsync"/>
        public Task<TypizerResponse<JsonElement>> GetOpenApiDocumentWithResponseAsync(RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<JsonElement>(new ApiRequest("getOpenApiDocument", Http.Get, "/v1/openapi.json"), requestOptions, cancellationToken);

        // ---- Lifecycle ---------------------------------------------------------------------------

        /// <summary>Закрывает соединения, если <see cref="System.Net.Http.HttpClient"/> создал сам SDK.</summary>
        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }

        /// <summary>Без ключа целиком: <c>TypizerClient(https://api.typizer.ru, tz_live_…a1b2)</c>.</summary>
        public override string ToString() => "TypizerClient(" + _pipeline.BaseUrl + ", " + KeyMask.Mask(_options.ApiKey) + ")";

        private static TypizerConfigurationException MissingKey() =>
            new TypizerConfigurationException(
                "Нет ключа API: передайте его в new TypizerClient(apiKey) или задайте переменную окружения TYPIZER_API_KEY. " +
                "Ключ создаётся в кабинете my.typizer.ru → «Ключи API».");

        private static string ResolveKey(string? key)
        {
            string? value = key ?? Environment.GetEnvironmentVariable("TYPIZER_API_KEY");
            if (string.IsNullOrEmpty(value)) throw MissingKey();
            foreach (char c in value!)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                    throw new TypizerConfigurationException("Ключ API содержит пробелы или управляющие символы — проверьте, что он скопирован целиком и без переноса строки.");
            }
            return value;
        }

        private static string ResolveBaseUrl(Uri? configured)
        {
            Uri url = configured ?? FromEnvironment() ?? TypizerClientOptions.DefaultBaseUrl;
            if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
                throw new TypizerConfigurationException("BaseUrl должен быть абсолютным http(s)-адресом: " + url);
            return url.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        private static Uri? FromEnvironment()
        {
            string? value = Environment.GetEnvironmentVariable("TYPIZER_BASE_URL");
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!Uri.TryCreate(value!.Trim(), UriKind.Absolute, out Uri? url))
                throw new TypizerConfigurationException("TYPIZER_BASE_URL — не адрес: " + value);
            return url;
        }

        private static HttpMessageHandler CreateDefaultHandler(IWebProxy? proxy, TimeSpan connectTimeout)
        {
#if NET8_0_OR_GREATER
            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = connectTimeout > TimeSpan.Zero ? connectTimeout : System.Threading.Timeout.InfiniteTimeSpan,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            };
#else
            var handler = new HttpClientHandler();
            if (handler.SupportsAutomaticDecompression)
                handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
#endif
            if (proxy != null)
            {
                handler.Proxy = proxy;
                handler.UseProxy = true;
            }
            return handler;
        }
    }
}
