using System;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Typizer;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <c>services.AddTypizer(…)</c>: <see cref="TypizerClient"/> как типизированный клиент
    /// <c>IHttpClientFactory</c> (соединения и DNS — по правилам фабрики), настройки — через
    /// <c>IOptions&lt;TypizerClientOptions&gt;</c>, журнал SDK — в <c>ILogger&lt;TypizerClient&gt;</c>.
    /// </summary>
    public static class TypizerServiceCollectionExtensions
    {
        /// <summary>Имя HTTP-клиента в <c>IHttpClientFactory</c>.</summary>
        public const string HttpClientName = "Typizer";

        /// <summary>
        /// Регистрирует <see cref="TypizerClient"/>. Ключ — из <paramref name="configure"/>, иначе из
        /// <c>TYPIZER_API_KEY</c>. Возвращает <see cref="IHttpClientBuilder"/>: к нему можно добавить свои
        /// обработчики или <c>ConfigurePrimaryHttpMessageHandler</c> (корпоративный сертификат, ГОСТ-TLS).
        /// <code>
        /// builder.Services.AddTypizer(o => o.ApiKey = builder.Configuration["Typizer:ApiKey"]);
        /// </code>
        /// </summary>
        public static IHttpClientBuilder AddTypizer(this IServiceCollection services, Action<TypizerClientOptions>? configure = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            OptionsBuilder<TypizerClientOptions> options = services.AddOptions<TypizerClientOptions>();
            if (configure != null) options.Configure(configure);
            options.PostConfigure<IServiceProvider>((o, provider) =>
            {
                if (o.Logger != null) return;
                ILoggerFactory? factory = provider.GetService<ILoggerFactory>();
                if (factory == null) return;
                ILogger logger = factory.CreateLogger<TypizerClient>();
                o.Logger = (level, message) =>
                {
                    LogLevel mapped = level switch
                    {
                        TypizerLogLevel.Debug => LogLevel.Debug,
                        TypizerLogLevel.Information => LogLevel.Information,
                        _ => LogLevel.Warning,
                    };
                    if (logger.IsEnabled(mapped)) logger.Log(mapped, default, message, null, (state, _) => state);
                };
            });

            IHttpClientBuilder builder = services.AddHttpClient(HttpClientName);
            builder.AddTypedClient<TypizerClient>((HttpClient http, IServiceProvider provider) =>
                new TypizerClient(provider.GetRequiredService<IOptions<TypizerClientOptions>>().Value, http));
            return builder
                .ConfigureHttpClient(http => http.Timeout = System.Threading.Timeout.InfiniteTimeSpan)
                // IHttpClientFactory's own request logging must never show the key.
                .RedactLoggedHeaders(new[] { "Authorization" })
                .ConfigurePrimaryHttpMessageHandler(provider =>
                    CreateHandler(provider.GetRequiredService<IOptions<TypizerClientOptions>>().Value));
        }

        /// <summary>Регистрирует <see cref="TypizerClient"/> с ключом API.</summary>
        public static IHttpClientBuilder AddTypizer(this IServiceCollection services, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("Пустой ключ API", nameof(apiKey));
            return services.AddTypizer(o => o.ApiKey = apiKey);
        }

        private static HttpMessageHandler CreateHandler(TypizerClientOptions options)
        {
            if (options.HttpMessageHandler != null || options.HttpClient != null)
                throw new InvalidOperationException(
                    "В AddTypizer свой обработчик подключается через ConfigurePrimaryHttpMessageHandler или AddHttpMessageHandler у возвращённого IHttpClientBuilder, а не через TypizerClientOptions.");
#if NET8_0_OR_GREATER
            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = options.ConnectTimeout > TimeSpan.Zero ? options.ConnectTimeout : System.Threading.Timeout.InfiniteTimeSpan,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            };
#else
            var handler = new HttpClientHandler();
            if (handler.SupportsAutomaticDecompression)
                handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
#endif
            if (options.Proxy != null)
            {
                handler.Proxy = options.Proxy;
                handler.UseProxy = true;
            }
            return handler;
        }
    }
}
