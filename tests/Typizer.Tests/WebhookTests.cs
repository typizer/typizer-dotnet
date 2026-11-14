using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Typizer.Tests
{
    /// <summary>SPEC §9: packages/sdk-spec/conformance/fixtures/webhook-vectors.json, case by case (SC-32).</summary>
    public class WebhookTests
    {
        private static readonly Lazy<JsonElement> Vectors = new Lazy<JsonElement>(() =>
        {
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Repo.WebhookVectorsPath)))
            {
                return doc.RootElement.Clone();
            }
        });

        public static IEnumerable<object[]> Cases() =>
            Vectors.Value.GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("name").GetString()! });

        private static JsonElement Case(string name) =>
            Vectors.Value.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);

        [Fact(DisplayName = "SC-32 The vector file has all 19 cases")]
        public void SC32_AllCases() => Assert.Equal(19, Cases().Count());

        [Theory(DisplayName = "SC-32 Webhook vector")]
        [MemberData(nameof(Cases))]
        public void SC32_Vector(string name)
        {
            JsonElement c = Case(name);
            string secret = c.GetProperty("secret").GetString()!;
            string body = c.GetProperty("body").GetString()!;
            var headers = c.GetProperty("headers").EnumerateObject().ToDictionary(h => h.Name, h => h.Value.GetString()!);
            DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(c.GetProperty("now").GetInt64());
            bool valid = c.GetProperty("valid").GetBoolean();

            Assert.Equal(valid, TypizerWebhooks.IsValid(body, headers, secret, now: now));
            Assert.Equal(valid, TypizerWebhooks.IsValid(Encoding.UTF8.GetBytes(body), headers, secret, now: now));
            if (valid)
            {
                WebhookEvent evt = TypizerWebhooks.Verify(body, headers, secret, now: now);
                string type = c.GetProperty("event_type").GetString()!;
                Assert.Equal(type, evt.Type.Value);
                Assert.Equal(headers.First(h => h.Key.Equals("webhook-id", StringComparison.OrdinalIgnoreCase)).Value, evt.Id);
                if (type.StartsWith("run.", StringComparison.Ordinal))
                {
                    Assert.NotNull(evt.Run);
                    Assert.Equal(evt.Data.Object.GetProperty("id").GetString(), evt.Run!.Id);
                    Assert.Null(evt.Batch);
                }
                else
                {
                    Assert.Null(evt.Batch);
                    Assert.Equal(JsonValueKind.Object, evt.Data.Object.ValueKind);
                }
            }
            else
            {
                var error = Assert.Throws<TypizerWebhookVerificationException>(() => TypizerWebhooks.Verify(body, headers, secret, now: now));
                Assert.Equal(c.GetProperty("reason").GetString(), error.Reason);
                Assert.DoesNotContain(secret, error.Message);
            }
        }

        [Fact(DisplayName = "SC-32 run.succeeded with a full Run: output, fields with warn/missing, NeedsReview")]
        public void SC32_FullRunEvent()
        {
            JsonElement c = Case("full_run_event");
            var headers = c.GetProperty("headers").EnumerateObject().ToDictionary(h => h.Name, h => h.Value.GetString()!);
            WebhookEvent evt = TypizerWebhooks.Verify(c.GetProperty("body").GetString()!, headers, c.GetProperty("secret").GetString()!,
                now: DateTimeOffset.FromUnixTimeSeconds(c.GetProperty("now").GetInt64()));
            Run run = evt.Run!;
            Assert.Equal(RunStatus.Succeeded, run.Status);
            Assert.True(run.NeedsReview);
            Assert.Equal("invoice_number", run.FieldsToReview()[0].Path);
            Assert.Equal("ООО «Ромашка»", run.Output!.Value.GetProperty("seller").GetProperty("name").GetString());
            Assert.Equal(WebhookEventType.RunSucceeded, evt.Type);
        }

        [Fact(DisplayName = "SC-32 Header collections: multi-value (HttpHeaders, string[]) and NameValueCollection, any name case")]
        public void SC32_HeaderShapes()
        {
            JsonElement c = Case("uppercase_header_names");
            string body = c.GetProperty("body").GetString()!;
            string secret = c.GetProperty("secret").GetString()!;
            DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(c.GetProperty("now").GetInt64());
            var pairs = c.GetProperty("headers").EnumerateObject().Select(h => (h.Name, Value: h.Value.GetString()!)).ToList();

            var arrays = pairs.ToDictionary(p => p.Name, p => new[] { p.Value });
            Assert.True(TypizerWebhooks.IsValid(body, arrays, secret, now: now));

            using (var message = new HttpRequestMessage())
            {
                foreach (var (name, value) in pairs) message.Headers.TryAddWithoutValidation(name, value);
                Assert.True(TypizerWebhooks.IsValid(body, message.Headers, secret, now: now));
            }

            var collection = new NameValueCollection();
            foreach (var (name, value) in pairs) collection.Add(name, value);
            Assert.True(TypizerWebhooks.IsValid(body, collection, secret, now: now));
            Assert.Equal("run.succeeded", TypizerWebhooks.Verify(Encoding.UTF8.GetBytes(body), collection, secret, now: now).Type.Value);
        }

#if !TYPIZER_NETSTANDARD
        [Fact(DisplayName = "SC-32 ASP.NET Core IHeaderDictionary works as is")]
        public void SC32_AspNetCoreHeaders()
        {
            JsonElement c = Case("docs_vector");
            var headers = new Microsoft.AspNetCore.Http.HeaderDictionary();
            foreach (JsonProperty h in c.GetProperty("headers").EnumerateObject()) headers[h.Name] = h.Value.GetString();
            WebhookEvent evt = TypizerWebhooks.Verify(c.GetProperty("body").GetString()!, headers, c.GetProperty("secret").GetString()!,
                now: DateTimeOffset.FromUnixTimeSeconds(c.GetProperty("now").GetInt64()));
            Assert.Equal("run_test", evt.Run!.Id);
        }
#endif

        [Fact(DisplayName = "SC-32 A bad secret is invalid_secret; tolerance is configurable")]
        public void SC32_SecretAndTolerance()
        {
            JsonElement c = Case("docs_vector");
            string body = c.GetProperty("body").GetString()!;
            var headers = c.GetProperty("headers").EnumerateObject().ToDictionary(h => h.Name, h => h.Value.GetString()!);
            DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(c.GetProperty("now").GetInt64());
            Assert.Equal("invalid_secret", Assert.Throws<TypizerWebhookVerificationException>(() =>
                TypizerWebhooks.Verify(body, headers, "whsec_not base64!", now: now)).Reason);
            Assert.Equal("invalid_secret", Assert.Throws<TypizerWebhookVerificationException>(() =>
                TypizerWebhooks.Verify(body, headers, "whsec_c2hvcnQ=", now: now)).Reason);
            Assert.False(TypizerWebhooks.IsValid(body, headers, c.GetProperty("secret").GetString()!, now: now.AddSeconds(61), tolerance: TimeSpan.FromSeconds(60)));
            Assert.True(TypizerWebhooks.IsValid(body, headers, c.GetProperty("secret").GetString()!, now: now.AddSeconds(60), tolerance: TimeSpan.FromSeconds(60)));
        }

        [Fact(DisplayName = "SC-32 A webhook the mock signs right now verifies with the real clock")]
        public async Task SC32_MockSampleSignedNow()
        {
            const string secret = "whsec_J8G0iNL6JM6usEvy6InDrrH8E1crlPG4a9TjsmmHN/A=";
            foreach (string type in new[] { "run.succeeded", "run.failed", "batch.completed", "balance.low" })
            {
                JsonElement sample = await Mock.Get("/_mock/webhooks/sample?secret=" + Uri.EscapeDataString(secret) + "&type=" + type);
                var headers = sample.GetProperty("headers").EnumerateObject().ToDictionary(h => h.Name, h => h.Value.GetString()!);
                WebhookEvent evt = TypizerWebhooks.Verify(sample.GetProperty("body").GetString()!, headers, secret);
                Assert.Equal(type, evt.Type.Value);
                if (type.StartsWith("run.", StringComparison.Ordinal)) Assert.NotNull(evt.Run);
                if (type == "batch.completed") Assert.NotNull(evt.Batch);
            }
        }
    }
}
