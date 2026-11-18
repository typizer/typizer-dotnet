using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Typizer.Tests
{
    /// <summary>
    /// The conformance mock (packages/sdk-spec/conformance): TYPIZER_MOCK_URL, default
    /// http://127.0.0.1:4010. Every test takes its own key (tz_test_&lt;scenario&gt;__dotnet…), so tests
    /// never share mock state and can run in parallel.
    /// </summary>
    internal static class Mock
    {
        private static int _counter;
        private static readonly HttpClient Control = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly string Run = Guid.NewGuid().ToString("N").Substring(0, 8);

        public static string BaseUrl { get; } =
            (Environment.GetEnvironmentVariable("TYPIZER_MOCK_URL") ?? "http://127.0.0.1:4010").TrimEnd('/');

        public static string Key(string scenario) =>
            "tz_test_" + scenario + "__dotnet" + Run + "x" + Interlocked.Increment(ref _counter);

        public static TypizerClient Client(string key, Action<TypizerClientOptions>? configure = null)
        {
            var options = new TypizerClientOptions { ApiKey = key, BaseUrl = new Uri(BaseUrl) };
            configure?.Invoke(options);
            return new TypizerClient(options);
        }

        /// <summary>The mock's request log for one key, in arrival order.</summary>
        public static async Task<List<JsonElement>> Requests(string key, string? operationId = null)
        {
            string url = BaseUrl + "/_mock/requests?key=" + Uri.EscapeDataString(key) +
                         (operationId != null ? "&operation_id=" + Uri.EscapeDataString(operationId) : string.Empty);
            string text;
            try
            {
                text = await Control.GetStringAsync(url).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    "The conformance mock is not reachable at " + BaseUrl + " — start it with `node packages/sdk-spec/conformance/server.js` " +
                    "or run scripts/test.sh (TYPIZER_MOCK_URL overrides the address).", ex);
            }
            using (JsonDocument doc = JsonDocument.Parse(text))
            {
                return doc.RootElement.GetProperty("data").EnumerateArray().Select(e => e.Clone()).ToList();
            }
        }

        public static async Task<JsonElement> Get(string path)
        {
            string text = await Control.GetStringAsync(BaseUrl + path).ConfigureAwait(false);
            using (JsonDocument doc = JsonDocument.Parse(text))
            {
                return doc.RootElement.Clone();
            }
        }

        public static string Header(JsonElement entry, string name) =>
            entry.GetProperty("headers").TryGetProperty(name, out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;

        public static string Str(JsonElement element, params string[] path)
        {
            JsonElement e = element;
            foreach (string p in path) e = e.GetProperty(p);
            return e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.GetRawText();
        }
    }

    internal static class Schemas
    {
        public const string Invoice = @"{
  ""type"": ""object"",
  ""properties"": {
    ""invoice_number"": { ""type"": ""string"" },
    ""total"": { ""type"": ""number"" },
    ""seller"": {
      ""type"": ""object"",
      ""properties"": { ""inn"": { ""type"": ""string"", ""x-typizer-check"": ""inn"" } }
    }
  },
  ""required"": [""invoice_number"", ""total""]
}";

        /// <summary>SC-36: an own validator by ref, an inline one (passed as is) and an official one.</summary>
        public const string Checked = @"{
  ""type"": ""object"",
  ""properties"": {
    ""contract_no"": { ""type"": ""string"", ""x-typizer-check"": ""my/contract-no@1"" },
    ""spec_no"": {
      ""type"": ""string"",
      ""x-typizer-check"": {
        ""kind"": ""rules"",
        ""applies_to"": ""string"",
        ""rules"": [{ ""type"": ""length"", ""min"": 5, ""max"": 12 }],
        ""message"": ""Номер спецификации — от 5 до 12 знаков""
      }
    },
    ""seller"": {
      ""type"": ""object"",
      ""properties"": { ""inn"": { ""type"": ""string"", ""x-typizer-check"": ""inn"" } }
    }
  }
}";

        public static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%mock\n");
        public const string SampleUrl = "https://typizer.ru/samples/invoice.pdf";
    }

    internal static class Specs
    {
        /// <summary>A validator spec: «ДГ-ГГГГ/NNNN», like the mock's seeded my/contract-no.</summary>
        public const string ContractNo = @"{
  ""kind"": ""rules"",
  ""applies_to"": ""string"",
  ""normalize"": { ""strip"": [""spaces""], ""case"": ""upper"" },
  ""rules"": [{ ""type"": ""pattern"", ""pattern"": ""ДГ-\\d{4}/\\d{4}"" }],
  ""message"": ""Номер договора не по шаблону ДГ-ГГГГ/NNNN""
}";
    }

    /// <summary>Collects SDK log lines.</summary>
    internal sealed class LogSink
    {
        public ConcurrentQueue<(TypizerLogLevel Level, string Message)> Lines { get; } = new ConcurrentQueue<(TypizerLogLevel, string)>();
        public void Write(TypizerLogLevel level, string message) => Lines.Enqueue((level, message));
        public IEnumerable<string> Messages => Lines.Select(l => l.Message);
    }

    /// <summary>A handler that records requests (and may answer them itself).</summary>
    internal sealed class RecordingHandler : DelegatingHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _respond;

        public RecordingHandler(HttpMessageHandler? inner = null, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond = null)
            : base(inner ?? new HttpClientHandler())
        {
            _respond = respond;
        }

        public ConcurrentQueue<Recorded> Requests { get; } = new ConcurrentQueue<Recorded>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[]? body = request.Content != null ? await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false) : null;
            Requests.Enqueue(new Recorded(request.Method.Method, request.RequestUri!, request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase),
                request.Content?.Headers.ContentType?.MediaType, body));
            if (_respond != null) return await _respond(request, cancellationToken).ConfigureAwait(false);
            if (body != null)
            {
                var replay = new ByteArrayContent(body);
                foreach (var h in request.Content!.Headers) replay.Headers.TryAddWithoutValidation(h.Key, h.Value);
                request.Content = replay;
            }
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        internal sealed class Recorded
        {
            public Recorded(string method, Uri uri, Dictionary<string, string> headers, string? contentType, byte[]? body)
            {
                Method = method;
                Uri = uri;
                Headers = headers;
                ContentType = contentType;
                Body = body;
            }

            public string Method { get; }
            public Uri Uri { get; }
            public Dictionary<string, string> Headers { get; }
            public string? ContentType { get; }
            public byte[]? Body { get; }
        }
    }

    internal static class Repo
    {
        /// <summary>packages/sdk-dotnet (where this test project lives), found from the test binaries.</summary>
        public static string PackageDir
        {
            get
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir, "Directory.Build.props")) && Directory.Exists(Path.Combine(dir, "src", "Typizer")))
                        return dir;
                    dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
                }
                throw new InvalidOperationException("packages/sdk-dotnet not found above " + AppContext.BaseDirectory);
            }
        }

        /// <summary>A shared fixture: the monorepo copy, else the snapshot the public mirror keeps in tests/Fixtures.</summary>
        public static string Fixture(string monorepoRelative, string fileName)
        {
            string monorepo = Path.GetFullPath(Path.Combine(PackageDir, "..", "..", monorepoRelative));
            if (File.Exists(monorepo)) return monorepo;
            string mirror = Path.Combine(PackageDir, "tests", "Fixtures", fileName);
            if (File.Exists(mirror)) return mirror;
            throw new FileNotFoundException("Neither " + monorepo + " nor " + mirror + " exists");
        }

        public static string OpenApiPath => Fixture(Path.Combine("apps", "docs", "openapi.public.json"), "openapi.public.json");

        public static string WebhookVectorsPath =>
            Fixture(Path.Combine("packages", "sdk-spec", "conformance", "fixtures", "webhook-vectors.json"), "webhook-vectors.json");
    }
}
