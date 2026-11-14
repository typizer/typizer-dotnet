using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Typizer.Tests
{
    /// <summary>Typizer.Extensions.DependencyInjection: services.AddTypizer(…) over IHttpClientFactory, ILogger bridged.</summary>
    [Collection("environment")] // both change TYPIZER_API_KEY
    public class DependencyInjectionTests
    {
        [Fact(DisplayName = "AddTypizer resolves a TypizerClient through IHttpClientFactory; its requests reach the mock")]
        public async Task AddTypizerResolvesAWorkingClient()
        {
            string key = Mock.Key("ok");
            var services = new ServiceCollection();
            services.AddTypizer(o =>
            {
                o.ApiKey = key;
                o.BaseUrl = new Uri(Mock.BaseUrl);
            });
            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                var client = provider.GetRequiredService<TypizerClient>();
                Me me = await client.GetMeAsync();
                Assert.Equal("api_key", me.Auth);
                Assert.NotNull(provider.GetRequiredService<IHttpClientFactory>().CreateClient(TypizerServiceCollectionExtensions.HttpClientName));
            }
            Assert.Single(await Mock.Requests(key));
        }

        [Fact(DisplayName = "The SDK log goes to ILogger<TypizerClient> (category Typizer.TypizerClient), without the key")]
        public async Task LoggerIsBridged()
        {
            string key = Mock.Key("maintenance");
            var provider = new ListLoggerProvider();
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(provider).SetMinimumLevel(LogLevel.Debug));
            services.AddTypizer(key).Services.Configure<TypizerClientOptions>(o => o.BaseUrl = new Uri(Mock.BaseUrl));
            using (ServiceProvider sp = services.BuildServiceProvider())
            {
                await sp.GetRequiredService<TypizerClient>().GetBalanceAsync();
            }
            List<(string Category, LogLevel Level, string Message)> lines = provider.Lines.ToList();
            Assert.Contains(lines, l => l.Category == "Typizer.TypizerClient" && l.Level == LogLevel.Debug && l.Message.StartsWith("typizer: GET /v1/balance -> 200", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Category == "Typizer.TypizerClient" && l.Level == LogLevel.Warning && l.Message.Contains("mnt_"));
            Assert.All(lines, l => Assert.DoesNotContain(key, l.Message));
        }

        [Fact(DisplayName = "AddTypizer without a key fails on resolve with TypizerConfigurationException")]
        public void MissingKey()
        {
            string? saved = Environment.GetEnvironmentVariable("TYPIZER_API_KEY");
            try
            {
                Environment.SetEnvironmentVariable("TYPIZER_API_KEY", null);
                var services = new ServiceCollection();
                services.AddTypizer();
                using (ServiceProvider sp = services.BuildServiceProvider())
                {
                    Assert.Throws<TypizerConfigurationException>(() => sp.GetRequiredService<TypizerClient>());
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("TYPIZER_API_KEY", saved);
            }
        }

        private sealed class ListLoggerProvider : ILoggerProvider
        {
            public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Lines { get; } = new ConcurrentQueue<(string, LogLevel, string)>();
            public ILogger CreateLogger(string categoryName) => new ListLogger(categoryName, Lines);
            public void Dispose() { }

            private sealed class ListLogger : ILogger
            {
                private readonly string _category;
                private readonly ConcurrentQueue<(string, LogLevel, string)> _lines;
                public ListLogger(string category, ConcurrentQueue<(string, LogLevel, string)> lines)
                {
                    _category = category;
                    _lines = lines;
                }
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => true;
                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                    _lines.Enqueue((_category, logLevel, formatter(state, exception)));
            }
        }
    }
}
