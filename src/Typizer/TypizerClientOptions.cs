using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using Typizer.Internal;

namespace Typizer
{
    /// <summary>Настройки <see cref="TypizerClient"/>. Всё, кроме ключа, необязательно.</summary>
    [DebuggerDisplay("{ToString(),nq}")]
    public sealed class TypizerClientOptions
    {
        /// <summary>Адрес API по умолчанию.</summary>
        public static readonly Uri DefaultBaseUrl = new Uri("https://api.typizer.ru");

        /// <summary>Ключ API (<c>tz_live_…</c>). Не задан — берётся из переменной окружения <c>TYPIZER_API_KEY</c>.</summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public string? ApiKey { get; set; }

        /// <summary>Адрес API. Не задан — <c>TYPIZER_BASE_URL</c>, иначе <c>https://api.typizer.ru</c>. Путь <c>/v1/…</c> SDK добавляет сам.</summary>
        public Uri? BaseUrl { get; set; }

        /// <summary>Таймаут одной HTTP-попытки (соединение, отправка, ответ). По умолчанию 75 с.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(75);

        /// <summary>Таймаут установки соединения (.NET 8+; в .NET Framework его задаёт сам HTTP-стек). По умолчанию 10 с.</summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Сколько раз повторить запрос после сетевой ошибки, 429, 500, 502–504 (итого до <c>MaxRetries + 1</c> попыток). По умолчанию 2.</summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>Если <c>Retry-After</c> длиннее — SDK не ждёт, а сразу бросает ошибку. По умолчанию 60 с.</summary>
        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Свой <see cref="System.Net.Http.HttpClient"/> (например, из <c>IHttpClientFactory</c>). SDK его не
        /// закрывает; прокси и TLS настраиваются в нём.
        /// </summary>
        public HttpClient? HttpClient { get; set; }

        /// <summary>
        /// Свой обработчик (прокси, корпоративный корневой сертификат, ГОСТ-TLS). SDK оборачивает его
        /// своим <see cref="System.Net.Http.HttpClient"/> и закрывает вместе с клиентом.
        /// </summary>
        public HttpMessageHandler? HttpMessageHandler { get; set; }

        /// <summary>
        /// Прокси для встроенного HTTP-стека, например <c>TypizerProxy.FromUrl("http://user:pass@proxy:3128")</c>
        /// или <see cref="WebProxy"/>. Не задан — как у .NET по умолчанию (в .NET 8 — <c>HTTPS_PROXY</c>/<c>NO_PROXY</c>).
        /// </summary>
        public IWebProxy? Proxy { get; set; }

        /// <summary>Заголовки каждого запроса. <c>Authorization</c> ими не переопределяется.</summary>
        public IDictionary<string, string> DefaultHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Ваше приложение — дописывается в конец <c>User-Agent</c>.</summary>
        public AppInfo? AppInfo { get; set; }

        /// <summary>
        /// Журнал SDK: строка на HTTP-попытку (<see cref="TypizerLogLevel.Debug"/>), повторы
        /// (<see cref="TypizerLogLevel.Information"/>), плановые работы (<see cref="TypizerLogLevel.Warning"/>).
        /// Ключа, тел запросов и ответов, имён файлов и metadata в журнале нет. В ASP.NET Core пакет
        /// <c>Typizer.Extensions.DependencyInjection</c> подключает сюда <c>ILogger</c> сам.
        /// </summary>
        public Action<TypizerLogLevel, string>? Logger { get; set; }

        internal TypizerClientOptions Clone() => new TypizerClientOptions
        {
            ApiKey = ApiKey,
            BaseUrl = BaseUrl,
            Timeout = Timeout,
            ConnectTimeout = ConnectTimeout,
            MaxRetries = MaxRetries,
            MaxRetryDelay = MaxRetryDelay,
            HttpClient = HttpClient,
            HttpMessageHandler = HttpMessageHandler,
            Proxy = Proxy,
            DefaultHeaders = new Dictionary<string, string>(DefaultHeaders ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            AppInfo = AppInfo,
            Logger = Logger,
        };

        /// <summary>Без ключа целиком: <c>tz_live_…a1b2</c>.</summary>
        public override string ToString() =>
            "TypizerClientOptions(ApiKey=" + KeyMask.Mask(ApiKey) + ", BaseUrl=" + (BaseUrl?.ToString() ?? "default") + ")";
    }

    /// <summary>Имя и версия вашего приложения для <c>User-Agent</c>: <c>acme-erp/2.1.0</c>.</summary>
    public sealed class AppInfo
    {
        public AppInfo(string name, string? version = null, string? url = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Имя приложения не может быть пустым", nameof(name));
            Name = name;
            Version = version;
            Url = url;
        }

        public string Name { get; }
        public string? Version { get; }
        public string? Url { get; }
    }

    /// <summary>Уровень сообщения журнала SDK.</summary>
    public enum TypizerLogLevel
    {
        Debug = 0,
        Information = 1,
        Warning = 2,
    }

    /// <summary>
    /// Настройки одного вызова: свой <c>Idempotency-Key</c> (для <c>POST</c>), таймаут попытки, число
    /// повторов, дополнительные заголовки.
    /// </summary>
    public sealed class RequestOptions
    {
        /// <summary>
        /// Ключ идемпотентности (1–255 печатных символов ASCII). Не задан — SDK создаёт UUID v4 на каждый
        /// вызов и повторяет с ним же. Свой ключ нужен для повторов между процессами: «тот же счёт из той
        /// же очереди не создаст второй прогон».
        /// </summary>
        public string? IdempotencyKey { get; set; }

        /// <summary>Таймаут одной HTTP-попытки этого вызова.</summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>Число повторов этого вызова.</summary>
        public int? MaxRetries { get; set; }

        /// <summary>Дополнительные заголовки этого вызова (<c>Authorization</c> ими не переопределяется).</summary>
        public IDictionary<string, string>? ExtraHeaders { get; set; }
    }

    /// <summary>Прокси из URL, в том числе с логином и паролем: <c>http://user:pass@proxy.local:3128</c>.</summary>
    public static class TypizerProxy
    {
        public static IWebProxy FromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("Пустой адрес прокси", nameof(url));
            var uri = new Uri(url, UriKind.Absolute);
            var proxy = new WebProxy(new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri);
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                string[] parts = uri.UserInfo.Split(new[] { ':' }, 2);
                proxy.Credentials = new NetworkCredential(
                    Uri.UnescapeDataString(parts[0]),
                    parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
            }
            return proxy;
        }
    }
}
