using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Typizer.Internal;

namespace Typizer.Tests
{
    /// <summary>Pieces that need no server: key masking, error classification, Retry-After, schemas, refs, output parsing.</summary>
    [Collection("environment")] // both change TYPIZER_API_KEY
    public class UnitTests
    {
        [Theory(DisplayName = "The key is masked to its prefix and last 4 characters")]
        [InlineData("tz_live_0123456789abcdefa1b2", "tz_live_…a1b2")]
        [InlineData("tz_test_ok", "tz_test_…")]
        [InlineData("sk_0123456789abcdef", "…cdef")]
        [InlineData(null, "(none)")]
        public void KeyIsMasked(string? key, string masked) => Assert.Equal(masked, KeyMask.Mask(key));

        [Fact(DisplayName = "No key and no TYPIZER_API_KEY: TypizerConfigurationException at construction")]
        public void MissingKeyFailsAtConstruction()
        {
            string? saved = Environment.GetEnvironmentVariable("TYPIZER_API_KEY");
            Environment.SetEnvironmentVariable("TYPIZER_API_KEY", null);
            try
            {
                var error = Assert.Throws<TypizerConfigurationException>(() => new TypizerClient());
                Assert.Contains("TYPIZER_API_KEY", error.Message);
                Assert.Throws<TypizerConfigurationException>(() => new TypizerClient(new TypizerClientOptions { ApiKey = "" }));
                Assert.Throws<TypizerConfigurationException>(() => new TypizerClient("tz_live_with space"));
                Environment.SetEnvironmentVariable("TYPIZER_API_KEY", "tz_live_fromenvironment0000");
                using (var client = new TypizerClient())
                {
                    Assert.Equal("TypizerClient(https://api.typizer.ru, tz_live_…0000)", client.ToString());
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("TYPIZER_API_KEY", saved);
            }
        }

        [Fact(DisplayName = "BaseUrl is normalized without the trailing slash; HttpClient with a Proxy is refused")]
        public void BaseUrlAndConflicts()
        {
            using (var client = new TypizerClient(new TypizerClientOptions { ApiKey = "tz_test_x", BaseUrl = new Uri("https://typizer.example.com/api/") }))
            {
                Assert.Equal("https://typizer.example.com/api", client.BaseUrl.ToString().TrimEnd('/'));
                Assert.StartsWith("TypizerClient(https://typizer.example.com/api, ", client.ToString());
            }
            using (var http = new HttpClient())
            {
                Assert.Throws<ArgumentException>(() => new TypizerClient(new TypizerClientOptions
                {
                    ApiKey = "tz_test_x",
                    HttpClient = http,
                    Proxy = new WebProxy("http://proxy:3128"),
                }));
            }
        }

        [Fact(DisplayName = "WithOptions returns a copy with other settings on the same connections")]
        public async Task WithOptionsSharesTheHttpClient()
        {
            var handler = new RecordingHandler(respond: (_, ct) => Task.FromResult(JsonResponse("{\"object\":\"balance\",\"pages_available\":5,\"pages_reserved\":0,\"lots\":[]}")));
            var logs = new LogSink();
            using (var client = new TypizerClient(new TypizerClientOptions { ApiKey = "tz_test_x", HttpMessageHandler = handler }))
            {
                TypizerClient patient = client.WithOptions(o =>
                {
                    o.MaxRetries = 7;
                    o.Logger = logs.Write;
                    o.DefaultHeaders["X-Trace"] = "abc";
                });
                Assert.Equal(5, (await patient.GetBalanceAsync()).PagesAvailable);
                patient.Dispose(); // does not close the shared connections
                Assert.Equal(5, (await client.GetBalanceAsync()).PagesAvailable);
                Assert.Throws<ArgumentException>(() => client.WithOptions(o => o.Proxy = new WebProxy("http://p:1")));
            }
            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal("abc", handler.Requests.First().Headers["X-Trace"]);
            Assert.False(handler.Requests.Last().Headers.ContainsKey("X-Trace"));
            Assert.Single(logs.Lines);
        }

        [Fact(DisplayName = "DefaultHeaders and ExtraHeaders never override Authorization; a custom User-Agent replaces the SDK's")]
        public async Task HeadersDoNotOverrideAuthorization()
        {
            var handler = new RecordingHandler(respond: (_, ct) => Task.FromResult(JsonResponse("{}")));
            using (var client = new TypizerClient(new TypizerClientOptions
            {
                ApiKey = "tz_test_real",
                HttpMessageHandler = handler,
                DefaultHeaders = { ["Authorization"] = "Bearer evil", ["User-Agent"] = "custom/1.0" },
            }))
            {
                await client.GetMeAsync(new RequestOptions { ExtraHeaders = new Dictionary<string, string> { ["authorization"] = "Bearer evil2", ["X-Extra"] = "1" } });
            }
            RecordingHandler.Recorded sent = Assert.Single(handler.Requests);
            Assert.Equal("Bearer tz_test_real", sent.Headers["Authorization"]);
            Assert.Equal("custom/1.0", sent.Headers["User-Agent"]);
            Assert.Equal("1", sent.Headers["X-Extra"]);
        }

        public static IEnumerable<object[]> Classification() => new[]
        {
            new object[] { 400, "invalid_schema", typeof(TypizerBadRequestException), false },
            new object[] { 400, "too_many_pages", typeof(TypizerBadRequestException), false },
            new object[] { 401, "unauthorized", typeof(TypizerAuthenticationException), false },
            new object[] { 402, "insufficient_balance", typeof(TypizerInsufficientBalanceException), false },
            new object[] { 403, "forbidden", typeof(TypizerPermissionDeniedException), false },
            new object[] { 404, "not_found", typeof(TypizerNotFoundException), false },
            new object[] { 408, "invalid_request", typeof(TypizerApiException), true },
            new object[] { 409, "conflict", typeof(TypizerConflictException), false },
            new object[] { 409, "idempotency_conflict", typeof(TypizerIdempotencyConflictException), false },
            new object[] { 413, "file_too_large", typeof(TypizerFileTooLargeException), false },
            new object[] { 415, "unsupported_file_type", typeof(TypizerUnsupportedFileTypeException), false },
            new object[] { 422, "document_unreadable", typeof(TypizerUnprocessableDocumentException), false },
            new object[] { 422, "extraction_failed", typeof(TypizerUnprocessableDocumentException), false },
            new object[] { 429, "rate_limited", typeof(TypizerRateLimitException), true },
            new object[] { 500, "internal_error", typeof(TypizerInternalServerException), true },
            new object[] { 503, "upstream_unavailable", typeof(TypizerServiceUnavailableException), true },
            new object[] { 400, "brand_new_code", typeof(TypizerBadRequestException), false },
            new object[] { 409, "brand_new_code", typeof(TypizerConflictException), false },
            new object[] { 418, "brand_new_code", typeof(TypizerApiException), false },
            new object[] { 501, "brand_new_code", typeof(TypizerInternalServerException), false },
        };

        [Theory(DisplayName = "problem+json: the class follows the code, an unknown code the status (SPEC §6.2)")]
        [MemberData(nameof(Classification))]
        public void ProblemIsClassified(int status, string code, Type expected, bool retryable)
        {
            string body = "{\"type\":\"https://docs.typizer.ru/errors/" + code + "\",\"title\":\"T\",\"status\":" + status +
                          ",\"detail\":\"D\",\"code\":\"" + code + "\",\"request_id\":\"req_1\",\"doc_url\":\"https://docs.typizer.ru/errors/" + code +
                          "\",\"batch_id\":\"batch_9\"}";
            TypizerApiException error = Errors.FromResponse(status, Headers(("Content-Type", "application/problem+json")), Encoding.UTF8.GetBytes(body));
            Assert.Equal(expected, error.GetType());
            Assert.Equal(code, error.Code);
            Assert.Equal(retryable, error.IsRetryable);
            Assert.Equal("req_1", error.RequestId);
            Assert.Equal("batch_9", error.Extra["batch_id"].GetString());
            Assert.Equal("[" + code + "] D (HTTP " + status + ", request_id=req_1, https://docs.typizer.ru/errors/" + code + ")", error.Message);
        }

        [Theory(DisplayName = "No body or a non-JSON body: the class and code follow the status, detail is the first 200 characters")]
        [InlineData(502, typeof(TypizerServiceUnavailableException), "upstream_unavailable", true)]
        [InlineData(504, typeof(TypizerServiceUnavailableException), "upstream_unavailable", true)]
        [InlineData(429, typeof(TypizerRateLimitException), "rate_limited", true)]
        [InlineData(401, typeof(TypizerAuthenticationException), "unauthorized", false)]
        [InlineData(500, typeof(TypizerInternalServerException), "internal_error", true)]
        public void NonJsonBodyIsClassifiedByStatus(int status, Type expected, string code, bool retryable)
        {
            string html = "<html>" + new string('x', 400) + "</html>";
            TypizerApiException error = Errors.FromResponse(status, Headers(("Content-Type", "text/html"), ("X-Request-Id", "req_edge")), Encoding.UTF8.GetBytes(html));
            Assert.Equal(expected, error.GetType());
            Assert.Equal(code, error.Code);
            Assert.Equal(retryable, error.IsRetryable);
            Assert.Equal(200, error.Detail!.Length);
            Assert.Equal("req_edge", error.RequestId);
            Assert.Equal(html, error.Body);
        }

        [Fact(DisplayName = "Retry-After: seconds or an HTTP date")]
        public void RetryAfterParses()
        {
            Assert.Equal(TimeSpan.FromSeconds(3), Errors.ParseRetryAfter("3"));
            Assert.Null(Errors.ParseRetryAfter(null));
            Assert.Null(Errors.ParseRetryAfter("soon"));
            TimeSpan? fromDate = Errors.ParseRetryAfter(DateTimeOffset.UtcNow.AddSeconds(30).ToString("r"));
            Assert.InRange(fromDate!.Value.TotalSeconds, 25, 31);
        }

        [Fact(DisplayName = "Backoff: min(8 s, 0.5 s × 2^n) × random(0.75, 1.0)")]
        public void BackoffIsExponentialWithJitter()
        {
            for (int n = 0; n < 6; n++)
            {
                double max = Math.Min(8, 0.5 * Math.Pow(2, n));
                for (int i = 0; i < 50; i++)
                    Assert.InRange(HttpPipeline.Backoff(n).TotalSeconds, max * 0.75 - 1e-9, max + 1e-9);
            }
        }

        [Fact(DisplayName = "A Retry-After over MaxRetryDelay is not waited for: the error comes at once with RetryAfter set")]
        public async Task LongRetryAfterIsNotWaited()
        {
            var handler = new RecordingHandler(respond: (_, ct) =>
            {
                HttpResponseMessage r = JsonResponse("{\"type\":\"t\",\"title\":\"Rate limited\",\"status\":429,\"detail\":\"slow down\",\"code\":\"rate_limited\",\"request_id\":\"req_1\",\"doc_url\":\"d\"}", (HttpStatusCode)429);
                r.Headers.TryAddWithoutValidation("Retry-After", "120");
                return Task.FromResult(r);
            });
            using (var client = new TypizerClient(new TypizerClientOptions { ApiKey = "tz_test_x", HttpMessageHandler = handler }))
            {
                var error = await Assert.ThrowsAsync<TypizerRateLimitException>(() => client.GetMeAsync());
                Assert.Equal(TimeSpan.FromSeconds(120), error.RetryAfter);
            }
            Assert.Single(handler.Requests);
        }

        [Fact(DisplayName = "PATCH is not retried after a timeout (the update may have happened)")]
        public async Task PatchIsNotRetriedAfterTimeout()
        {
            var handler = new RecordingHandler(respond: async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return JsonResponse("{}");
            });
            using (var client = new TypizerClient(new TypizerClientOptions { ApiKey = "tz_test_x", HttpMessageHandler = handler, Timeout = TimeSpan.FromMilliseconds(200) }))
            {
                await Assert.ThrowsAsync<TypizerTimeoutException>(() => client.Typizers.UpdateAsync("tz_1", new TypizerUpdateOptions { Name = "x" }));
                await Assert.ThrowsAsync<TypizerTimeoutException>(() => client.Typizers.GetAsync("tz_1"));
            }
            Assert.Equal(new[] { "PATCH", "GET", "GET", "GET" }, handler.Requests.Select(r => r.Method).ToArray());
        }

        [Fact(DisplayName = "Schemas: JSON text, JsonNode, dictionaries and anonymous objects keep their property order; non-objects are refused")]
        public void SchemaInputs()
        {
            string[] order = { "zeta", "alpha", "mid" };
            var node = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["zeta"] = new JsonObject(), ["alpha"] = new JsonObject(), ["mid"] = new JsonObject() } };
            var dict = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object> { ["zeta"] = new { type = "string" }, ["alpha"] = new { type = "number" }, ["mid"] = new { type = "string" } },
            };
            var anonymous = new { type = "object", properties = new { zeta = new { type = "string" }, alpha = new { type = "number" }, mid = new { type = "string" } } };
            foreach (object schema in new object[] { "{\"type\":\"object\",\"properties\":{\"zeta\":{},\"alpha\":{},\"mid\":{}}}", node, dict, anonymous })
            {
                JsonElement element = Json.ToSchema(schema, "schema");
                Assert.Equal(order, element.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray());
            }
            Assert.Throws<ArgumentException>(() => Json.ToSchema("[1,2]", "schema"));
            Assert.Throws<ArgumentException>(() => Json.ToSchema("not json", "schema"));
            Assert.Throws<ArgumentException>(() => Json.ToSchema(default(JsonElement), "schema"));
        }

        [Theory(DisplayName = "Catalog refs: slug, publisher/slug, @version")]
        [InlineData("schet-faktura", null, "schet-faktura", null)]
        [InlineData("schet-faktura@1", null, "schet-faktura", "1")]
        [InlineData("acme/akt-sverki@1.2.0", "acme", "akt-sverki", "1.2.0")]
        [InlineData("cat_0123", null, "cat_0123", null)]
        public void CatalogRefs(string reference, string? publisher, string slug, string? version)
        {
            CatalogRef r = CatalogRef.Parse(reference, null, "ref");
            Assert.Equal(publisher, r.Publisher);
            Assert.Equal(slug, r.Slug);
            Assert.Equal(version, r.Version);
        }

        [Fact(DisplayName = "Catalog refs: two different versions or a malformed ref are argument errors")]
        public void BadCatalogRefs()
        {
            Assert.Throws<ArgumentException>(() => CatalogRef.Parse("upd@1", "2", "ref"));
            Assert.Throws<ArgumentException>(() => CatalogRef.Parse("a/b/c", null, "ref"));
            Assert.Throws<ArgumentException>(() => CatalogRef.Parse("upd@", null, "ref"));
            Assert.Equal("1", CatalogRef.Parse("upd@1", "1", "ref").Version);
        }

        private sealed class Invoice
        {
            public string? InvoiceNumber { get; set; }
            public decimal Total { get; set; }
            public Seller? Seller { get; set; }
        }

        private sealed class Seller
        {
            public string? Inn { get; set; }
        }

        [Fact(DisplayName = "GetOutput<T> maps snake_case keys to PascalCase properties")]
        public void OutputIntoOwnType()
        {
            Run run = Json.Deserialize<Run>(Encoding.UTF8.GetBytes(
                "{\"id\":\"run_1\",\"status\":\"succeeded\",\"output\":{\"invoice_number\":\"А-1\",\"total\":\"3660.50\",\"seller\":{\"inn\":\"7707083893\"}}}"));
            Invoice? invoice = run.GetOutput<Invoice>();
            Assert.Equal("А-1", invoice!.InvoiceNumber);
            Assert.Equal(3660.50m, invoice.Total);
            Assert.Equal("7707083893", invoice.Seller!.Inn);
        }

        [Fact(DisplayName = "Open enums: comparisons, strings, defaults")]
        public void OpenEnums()
        {
            Assert.True(RunStatus.Succeeded == "succeeded");
            Assert.Equal("precise", RunMode.Precise.ToString());
            Assert.Equal(string.Empty, default(OutputFormat).Value);
            Assert.NotEqual(RunStatus.Queued, new RunStatus("Queued"));
            Assert.Equal("\"run.succeeded\"", JsonSerializer.Serialize(WebhookEventType.RunSucceeded));
            // A value the SDK has no constant for (the removed catalog category `medical`) still
            // parses and keeps its text.
            CatalogCategory removed = JsonSerializer.Deserialize<CatalogCategory>("\"medical\"");
            Assert.Equal("medical", removed.Value);
            Assert.NotEqual(CatalogCategory.Registries, removed);
        }

        [Theory(DisplayName = "Download file names keep only the name")]
        [InlineData("../../etc/passwd", "passwd")]
        [InlineData("C:\\temp\\report.xlsx", "report.xlsx")]
        [InlineData("\"run_1.xml\"", "run_1.xml")]
        [InlineData("..", null)]
        [InlineData("a\u0000b.csv", "ab.csv")]
        [InlineData("C:report.xlsx", "Creport.xlsx")]
        [InlineData("report.xlsx:evil", "report.xlsxevil")]
        [InlineData("CON", null)]
        [InlineData("nul.txt", null)]
        [InlineData("com1 .xml", null)]
        [InlineData("...", null)]
        [InlineData("report.xlsx. ", "report.xlsx")]
        [InlineData("a<b>|?*.csv", "ab.csv")]
        [InlineData("счёт.xml", "счёт.xml")]
        public void DownloadFileName(string raw, string? safe) => Assert.Equal(safe, TypizerDownload.SafeFileName(raw));

        [Fact(DisplayName = "Usage dates are plain dates on every target framework")]
        public void UsageDates()
        {
            Usage usage = Json.Deserialize<Usage>(Encoding.UTF8.GetBytes("{\"from\":\"2026-11-01\",\"to\":\"2026-11-13\",\"group_by\":\"day\",\"data\":[],\"has_more\":false}"));
            Assert.Equal(new DateTime(2026, 11, 1), usage.From);
            Assert.Equal(new DateTime(2026, 11, 13), usage.To);
        }

        [Fact(DisplayName = "TypizerFile: explicit factories, implicit Uri/byte[]/Stream/FileInfo, names from paths and FileStream")]
        public void Files()
        {
            Assert.Equal("invoice.pdf", TypizerFile.FromPath("/tmp/docs/invoice.pdf").FileName);
            Assert.Equal("document", TypizerFile.FromBytes(new byte[1]).FileName);
            TypizerFile fromUri = new Uri("https://example.com/a.pdf");
            Assert.Equal("TypizerFile(url)", fromUri.ToString());
            Assert.Equal("TypizerFile(file_1)", TypizerFile.FromId("file_1").ToString());
            Assert.Throws<ArgumentException>(() => TypizerFile.FromPath(" "));
            Assert.Throws<ArgumentException>(() => TypizerFile.FromUrl(new Uri("/relative", UriKind.Relative)));
        }

        [Fact(DisplayName = "Validator models keep unknown fields and enum values; spec stays free JSON (SPEC §5.4)")]
        public void ValidatorModelsAreForwardCompatible()
        {
            ValidatorUpdateResult update = Json.Deserialize<ValidatorUpdateResult>(Encoding.UTF8.GetBytes(
                "{\"object\":\"validator_update\",\"bump\":\"epoch\",\"reasons\":[],\"propagated_to\":[{\"typizer_id\":\"tz_1\",\"version\":3,\"auto\":true}],"
                + "\"suggested_to\":[],\"audit_id\":\"a_1\",\"validator\":{\"id\":\"val_1\",\"object\":\"validator\",\"ref\":\"my/x\",\"slug\":\"x\","
                + "\"name\":\"X\",\"description\":null,\"versions_count\":1,\"catalog\":null,\"forked_from\":null,\"owner\":\"org_1\","
                + "\"created_at\":\"2026-11-15T10:00:00Z\",\"updated_at\":\"2026-11-15T10:00:00Z\",\"version\":{\"id\":\"valv_1\","
                + "\"object\":\"validator_version\",\"version\":\"1.0.0\",\"spec\":{\"kind\":\"ml\",\"model\":\"m-2\",\"rules\":[{\"type\":\"vibes\"}]},"
                + "\"tests\":[{\"value\":42,\"expect\":\"maybe\",\"weight\":2}],\"explain\":\"\",\"rule_preview\":[],\"changelog\":\"\","
                + "\"created_at\":\"2026-11-15T10:00:00Z\"}}}"));
            Assert.Equal("epoch", update.Bump.Value);
            Assert.NotEqual(ValidatorBump.Major, update.Bump);
            Assert.Equal("a_1", update.ExtensionData!["audit_id"].GetString());
            Assert.True(update.PropagatedTo[0].ExtensionData!["auto"].GetBoolean());
            Assert.Equal("org_1", update.Validator.ExtensionData!["owner"].GetString());
            ValidatorVersion version = update.Validator.Version;
            Assert.Equal("ml", version.Spec.GetProperty("kind").GetString());
            Assert.Equal("vibes", version.Spec.GetProperty("rules")[0].GetProperty("type").GetString());
            ValidatorTest test = Assert.Single(version.Tests);
            Assert.Equal(42, test.Value.GetInt32());
            Assert.Equal("maybe", test.Expect.Value);
            Assert.Equal(2, test.ExtensionData!["weight"].GetInt32());

            ValidatorDetail detail = Json.Deserialize<ValidatorDetail>(Encoding.UTF8.GetBytes(
                "{\"id\":\"val_1\",\"ref\":\"my/x\",\"versions\":[],\"used_by\":[{\"typizer_id\":\"tz_1\",\"name\":\"T\",\"version\":1,\"ref\":\"my/x@1\","
                + "\"locked_version\":\"1.0.0\",\"update_available\":\"1.1.0\",\"pinned\":true}],\"trend\":\"up\"}"));
            Assert.Equal("up", detail.ExtensionData!["trend"].GetString());
            Assert.Equal("1.1.0", detail.UsedBy[0].UpdateAvailable);
            Assert.True(detail.UsedBy[0].ExtensionData!["pinned"].GetBoolean());

            ValidatorSuggestion suggestion = Json.Deserialize<ValidatorSuggestion>(Encoding.UTF8.GetBytes(
                "{\"name\":\"N\",\"spec\":{\"kind\":\"rules\"},\"tests\":[{\"value\":\"a\",\"expect\":\"pass\",\"got\":\"unsure\"}],\"converged\":false,"
                + "\"explain\":\"\",\"rule_preview\":[],\"iterations\":2,\"source\":\"crowd\",\"candidates_from_catalog\":[{\"ref\":\"inn@1\",\"name\":\"ИНН\","
                + "\"kind\":\"partner\",\"explain\":\"\",\"examples\":{\"correct\":2,\"total\":3},\"score\":0.9}],\"cost\":1}"));
            Assert.Equal("unsure", suggestion.Tests[0].Got.Value);
            Assert.Equal("crowd", suggestion.Source);
            Assert.Equal("partner", suggestion.CandidatesFromCatalog[0].Kind);
            Assert.Equal(2, suggestion.CandidatesFromCatalog[0].Examples!.Correct);
            Assert.Equal(0.9, suggestion.CandidatesFromCatalog[0].ExtensionData!["score"].GetDouble());

            SavedTypizer typizer = Json.Deserialize<SavedTypizer>(Encoding.UTF8.GetBytes("{\"id\":\"tz_1\",\"name\":\"T\"}"));
            Assert.Empty(typizer.Checks); // a response from before the field
        }

        [Fact(DisplayName = "ValidatorTest factories and check values: strings and numbers only, written as JSON strings and numbers")]
        public void ValidatorTestValues()
        {
            Assert.Equal(JsonValueKind.String, ValidatorTest.Pass("ДГ-2026/0153").Value.ValueKind);
            Assert.Equal(ValidatorOutcome.Fail, ValidatorTest.Fail("x", "note").Expect);
            Assert.Equal(12.5m, new ValidatorTest(12.5m, "pass").Value.GetDecimal());
            byte[] body = Json.WriteObject(w =>
            {
                w.WritePropertyName("v");
                w.WriteStartArray();
                foreach (object o in new object[] { "a", 1, 2L, 3.5m, 4.25, JsonDocument.Parse("7").RootElement }) Json.WriteCheckValue(w, o, "v");
                w.WriteEndArray();
            });
            Assert.Equal("{\"v\":[\"a\",1,2,3.5,4.25,7]}", Encoding.UTF8.GetString(body));
            Assert.Throws<ArgumentException>(() => Json.WriteObject(w => Json.WriteCheckValue(w, double.NaN, "v")));
            Assert.Throws<ArgumentException>(() => Json.WriteObject(w => Json.WriteCheckValue(w, null, "v")));
            Assert.Throws<ArgumentException>(() => Json.WriteObject(w => Json.WriteCheckValue(w, new { a = 1 }, "v")));
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> Headers(params (string Name, string Value)[] headers) =>
            headers.ToDictionary(h => h.Name, h => (IReadOnlyList<string>)new[] { h.Value }, StringComparer.OrdinalIgnoreCase);

        internal static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, status >= HttpStatusCode.BadRequest ? "application/problem+json" : "application/json") };
    }
}
