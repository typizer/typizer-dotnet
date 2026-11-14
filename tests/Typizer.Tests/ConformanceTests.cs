using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Typizer.Tests
{
    /// <summary>
    /// packages/sdk-spec/conformance/SCENARIOS.md against the mock: one test (or more) per check,
    /// named SC-xx. The scenario is the API key; the request log tells what the SDK actually sent.
    /// </summary>
    public class ConformanceTests
    {
        private static readonly Regex UuidV4 = new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");

        // ---- tz_test_ok -------------------------------------------------------------------------

        [Fact(DisplayName = "SC-01 ExtractAsync from a path, bytes and a stream: one multipart POST, file part named and typed, schema as JSON text")]
        public async Task SC01_ExtractFromPathBytesAndStream()
        {
            string dir = Path.Combine(Path.GetTempPath(), "typizer-dotnet-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "invoice.pdf");
            File.WriteAllBytes(path, Schemas.Pdf);
            try
            {
                var cases = new List<(TypizerFile File, string Name, string Type)>
                {
                    (TypizerFile.FromPath(path), "invoice.pdf", "application/pdf"),
                    (TypizerFile.FromBytes(Schemas.Pdf, "scan.png"), "scan.png", "image/png"),
                    (TypizerFile.FromStream(new MemoryStream(Schemas.Pdf), "akt.docx"), "akt.docx",
                        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
                    (TypizerFile.FromStream(new NonSeekableStream(Schemas.Pdf)), "document", "application/octet-stream"),
                };
                foreach ((TypizerFile file, string name, string type) in cases)
                {
                    string key = Mock.Key("ok");
                    using (TypizerClient client = Mock.Client(key))
                    {
                        Run run = await client.ExtractAsync(file, Schemas.Invoice);

                        Assert.Equal(RunStatus.Succeeded, run.Status);
                        Assert.True(run.IsTerminal);
                        Assert.NotNull(run.Output);
                        Assert.Equal("А-1024", run.Output!.Value.GetProperty("invoice_number").GetString());
                        Assert.Equal(3660, run.Output.Value.GetProperty("total").GetDecimal());
                        Assert.Equal("7707083893", run.Output.Value.GetProperty("seller").GetProperty("inn").GetString());
                        Assert.NotNull(run.Fields);
                        Assert.Equal(new[] { "invoice_number", "total", "seller.inn" }, run.Fields!.Keys.ToArray());
                        Assert.All(run.Fields.Values, f => Assert.Equal(FieldStatus.Ok, f.Status));
                        Assert.False(run.NeedsReview);

                        List<JsonElement> log = await Mock.Requests(key);
                        JsonElement sent = Assert.Single(log);
                        Assert.Equal("extract", Mock.Str(sent, "operation_id"));
                        Assert.Equal("POST", Mock.Str(sent, "method"));
                        Assert.Equal("multipart", Mock.Str(sent, "body", "type"));
                        JsonElement part = Assert.Single(sent.GetProperty("body").GetProperty("files").EnumerateArray());
                        Assert.Equal("file", Mock.Str(part, "field"));
                        Assert.Equal(name, Mock.Str(part, "filename"));
                        Assert.Equal(type, Mock.Str(part, "content_type"));
                        Assert.Equal(Schemas.Pdf.Length, part.GetProperty("size").GetInt32());
                        string schemaText = Mock.Str(sent, "body", "fields", "schema");
                        using (JsonDocument schema = JsonDocument.Parse(schemaText))
                        {
                            // Property order kept: columns and XML elements follow it.
                            Assert.Equal(new[] { "invoice_number", "total", "seller" },
                                schema.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray());
                        }
                        Assert.Equal("60", Mock.Str(sent, "query", "wait"));
                    }
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact(DisplayName = "SC-01 ExtractAsync with a URL or a file id sends JSON, not multipart")]
        public async Task SC01_ExtractFromUrlAndFileId()
        {
            string key = Mock.Key("ok");
            using (TypizerClient client = Mock.Client(key))
            {
                Run byUrl = await client.ExtractAsync(new Uri(Schemas.SampleUrl), Schemas.Invoice);
                Assert.Equal(RunStatus.Succeeded, byUrl.Status);

                UploadedFile uploaded = await client.Files.UploadAsync(TypizerFile.FromBytes(Schemas.Pdf, "счёт №1.pdf"));
                Assert.Equal("счёт №1.pdf", uploaded.Filename);
                Run byId = await client.ExtractAsync(TypizerFile.FromId(uploaded), Schemas.Invoice);
                Assert.Equal(uploaded.Id, byId.FileId);

                List<JsonElement> extracts = await Mock.Requests(key, "extract");
                Assert.Equal(2, extracts.Count);
                Assert.Equal(Schemas.SampleUrl, Mock.Str(extracts[0], "body", "value", "url"));
                Assert.Equal(JsonValueKind.Object, extracts[0].GetProperty("body").GetProperty("value").GetProperty("schema").ValueKind);
                Assert.Equal(uploaded.Id, Mock.Str(extracts[1], "body", "value", "file_id"));
                JsonElement upload = Assert.Single(await Mock.Requests(key, "createFile"));
                Assert.Equal("счёт №1.pdf", Mock.Str(upload.GetProperty("body").GetProperty("files")[0], "filename"));
            }
        }

        [Fact(DisplayName = "SC-02 ExtractAsync with a catalog schema and with a saved typizer")]
        public async Task SC02_CatalogAndTypizer()
        {
            string key = Mock.Key("ok");
            using (TypizerClient client = Mock.Client(key))
            {
                Run byCatalog = await client.ExtractAsync(TypizerFile.FromBytes(Schemas.Pdf, "sf.pdf"),
                    new ExtractOptions { Catalog = "schet-faktura@1", Metadata = new Dictionary<string, string> { ["order_id"] = "42" } });
                Assert.Equal(RunStatus.Succeeded, byCatalog.Status);
                Assert.Equal("schet-faktura", byCatalog.Catalog!.Ref);
                Assert.Equal("42", byCatalog.Metadata["order_id"]);

                SavedTypizer typizer = await client.Typizers.CreateAsync("Счета поставщиков", Schemas.Invoice);
                Run byTypizer = await client.ExtractAsync(TypizerFile.FromBytes(Schemas.Pdf, "invoice.pdf"), typizer);
                Assert.Equal(typizer.Id, byTypizer.TypizerId);
                Run byTypizerId = await client.ExtractAsync(new Uri(Schemas.SampleUrl), new ExtractOptions { Typizer = typizer.Id });
                Assert.Equal(RunStatus.Succeeded, byTypizerId.Status);

                JsonElement viaCatalog = Assert.Single(await Mock.Requests(key, "extract"));
                Assert.Equal("schet-faktura@1", Mock.Str(viaCatalog, "body", "fields", "catalog"));
                Assert.Equal("{\"order_id\":\"42\"}", Mock.Str(viaCatalog, "body", "fields", "metadata"));
                Assert.False(viaCatalog.GetProperty("body").GetProperty("fields").TryGetProperty("schema", out _));
                List<JsonElement> runs = await Mock.Requests(key, "createRun");
                Assert.Equal(2, runs.Count);
                Assert.All(runs, r => Assert.Equal("/v1/typizers/" + typizer.Id + "/runs", Mock.Str(r, "path")));
            }
        }

        [Fact(DisplayName = "SC-03 Every request carries Authorization, User-Agent typizer-dotnet/<version> and Accept: application/json")]
        public async Task SC03_Headers()
        {
            string key = Mock.Key("ok");
            using (TypizerClient client = Mock.Client(key, o => o.AppInfo = new AppInfo("acme-erp", "2.1.0")))
            {
                await client.GetMeAsync();
                await client.ExtractAsync(new Uri(Schemas.SampleUrl), Schemas.Invoice);
                await client.Runs.ListAsync(new RunListOptions { Limit = 1 });
            }
            List<JsonElement> log = await Mock.Requests(key);
            Assert.Equal(3, log.Count);
            var ua = new Regex(@"^typizer-dotnet/" + Regex.Escape(TypizerClient.Version) +
                               @" \((dotnet|dotnet-framework|mono)/[0-9][^;]*; (linux|windows|darwin|freebsd|unknown)/[a-z0-9]+\) acme-erp/2\.1\.0$");
            foreach (JsonElement entry in log)
            {
                Assert.Equal("Bearer " + key, Mock.Header(entry, "authorization"));
                Assert.Matches(ua, Mock.Header(entry, "user-agent"));
                Assert.Equal("application/json", Mock.Header(entry, "accept"));
            }
        }

        [Fact(DisplayName = "SC-04 Every POST carries a generated UUID v4 Idempotency-Key, different per call; a caller's key goes as is")]
        public async Task SC04_IdempotencyKeys()
        {
            string key = Mock.Key("ok");
            using (TypizerClient client = Mock.Client(key))
            {
                await client.ExtractAsync(new Uri(Schemas.SampleUrl), Schemas.Invoice);
                await client.Typizers.CreateAsync("A", Schemas.Invoice);
                await client.Files.UploadAsync(TypizerFile.FromBytes(Schemas.Pdf, "a.pdf"));
                await client.ExtractAsync(new Uri(Schemas.SampleUrl), Schemas.Invoice, new ExtractOptions { IdempotencyKey = "order-42/invoice-7" });
                await client.Typizers.CreateAsync(new TypizerCreateOptions { Name = "B", Schema = Schemas.Invoice },
                    new RequestOptions { IdempotencyKey = "typizer-b" });
                await client.GetBalanceAsync();
            }
            List<JsonElement> log = await Mock.Requests(key);
            List<string> generated = log.Take(3).Select(e => Mock.Header(e, "idempotency-key")).ToList();
            Assert.All(generated, k => Assert.Matches(UuidV4, k));
            Assert.Equal(3, generated.Distinct().Count());
            Assert.Equal("order-42/invoice-7", Mock.Header(log[3], "idempotency-key"));
            Assert.Equal("typizer-b", Mock.Header(log[4], "idempotency-key"));
            Assert.Equal("GET", Mock.Str(log[5], "method"));
            Assert.Equal(string.Empty, Mock.Header(log[5], "idempotency-key"));
        }

        [Fact(DisplayName = "SC-04 An invalid caller Idempotency-Key is an argument error before any request")]
        public async Task SC04_InvalidIdempotencyKey()
        {
            string key = Mock.Key("ok");
            using (TypizerClient client = Mock.Client(key))
            {
                await Assert.ThrowsAsync<ArgumentException>(() =>
                    client.ExtractAsync(new Uri(Schemas.SampleUrl), Schemas.Invoice, new ExtractOptions { IdempotencyKey = "ключ" }));
                await Assert.ThrowsAsync<ArgumentException>(() =>
                    client.Typizers.CreateAsync("A", Schemas.Invoice, new RequestOptions { IdempotencyKey = new string('x', 256) }));
            }
            Assert.Empty(await Mock.Requests(key));
        }

        [Fact(DisplayName = "SC-05 Runs.ListAsync(limit 10) pages through all 25 runs in 3 requests via starting_after")]
        public async Task SC05_RunsAutoPaging()
        {
            string key = Mock.Key("ok");
            using (TypizerClient client = Mock.Client(key))
            {
                var runs = new List<Run>();
                await foreach (Run run in client.Runs.ListAsync(new RunListOptions { Limit = 10 }))
                {
                    Assert.Null(run.Output);
                    Assert.Null(run.Fields);
                    runs.Add(run);
                }
                Assert.Equal(25, runs.Count);
                Assert.Equal(25, runs.Select(r => r.Id).Distinct().Count());
                Assert.Equal(3, runs.Count(r => r.Status == RunStatus.Failed));
            }
            List<JsonElement> log = await Mock.Requests(key, "listRuns");
            Assert.Equal(3, log.Count);
            Assert.False(log[0].GetProperty("query").TryGetProperty("starting_after", out _));
            Assert.All(log, e => Assert.Equal("10", Mock.Str(e, "query", "limit")));
            Assert.NotEqual(Mock.Str(log[1], "query", "starting_after"), Mock.Str(log[2], "query", "starting_after"));
        }

        [Fact(DisplayName = "SC-05 Pages by hand: await gives the first page, NextCursor/GetNextPageAsync the rest; the catalog pages with cursor")]
        public async Task SC05_PagesAndCatalogCursor()
        {
            string key = Mock.Key("ok");
            using (TypizerClient client = Mock.Client(key))
            {
                Page<Run> first = await client.Runs.ListAsync(new RunListOptions { Limit = 10 });
                Assert.Equal(10, first.Count);
                Assert.True(first.HasMore);
                Assert.True(first.HasNextPage);
                Assert.Equal(first.Data[9].Id, first.NextCursor);
                Assert.NotNull(first.Response.RequestId);
                Page<Run>? second = await first.GetNextPageAsync();
                Page<Run>? third = await second!.GetNextPageAsync();
                Assert.Equal(5, third!.Count);
                Assert.False(third.HasMore);
                Assert.Null(await third.GetNextPageAsync());

                var refs = new List<string>();
                int pages = 0;
                await foreach (Page<CatalogEntryBrief> page in client.Catalog.ListAsync(new CatalogListOptions { Limit = 1 }).AsPages())
                {
                    pages++;
                    refs.AddRange(page.Select(e => e.Ref));
                }
                Assert.Equal(new[] { "acme/akt-sverki", "schet-faktura", "upd" }, refs.OrderBy(r => r, StringComparer.Ordinal).ToArray());
                Assert.Equal(3, pages);
            }
            List<JsonElement> catalog = await Mock.Requests(key, "listCatalog");
            Assert.Equal(3, catalog.Count);
            Assert.True(catalog[1].GetProperty("query").TryGetProperty("cursor", out _));
            Assert.False(catalog[1].GetProperty("query").TryGetProperty("starting_after", out _));
        }

        [Fact(DisplayName = "SC-06 GetMeAsync and GetBalanceAsync parse into models; an unknown run is TypizerNotFoundException with request_id")]
        public async Task SC06_MeBalanceNotFound()
        {
            string key = Mock.Key("ok");
            using (TypizerClient client = Mock.Client(key))
            {
                Me me = await client.GetMeAsync();
                Assert.Equal("api_key", me.Auth);
                Assert.False(string.IsNullOrEmpty(me.Organization.Id));
                Assert.Contains("runs:write", me.ApiKey!.Scopes);

                Balance balance = await client.GetBalanceAsync();
                Assert.Equal(950, balance.PagesAvailable);
                Assert.NotEmpty(balance.Lots);

                var error = await Assert.ThrowsAsync<TypizerNotFoundException>(() => client.Runs.GetAsync("run_00000000000000000000000000"));
                Assert.Equal(404, error.StatusCode);
                Assert.Equal("not_found", error.Code);
                Assert.False(string.IsNullOrEmpty(error.RequestId));
                Assert.False(error.IsRetryable);
                Assert.StartsWith("[not_found] ", error.Message);
                Assert.Contains("HTTP 404, request_id=" + error.RequestId, error.Message);
            }
        }

        [Fact(DisplayName = "SC-07 Typizers CRUD: create, get, update, list, delete")]
        public async Task SC07_TypizerCrud()
        {
            string key = Mock.Key("ok");
            using (TypizerClient client = Mock.Client(key))
            {
                SavedTypizer created = await client.Typizers.CreateAsync(new TypizerCreateOptions
                {
                    Name = "УПД поставщиков",
                    Schema = Schemas.Invoice,
                    Description = "Входящие УПД",
                    DefaultFormat = OutputFormat.Xml,
                    Mode = RunMode.Precise,
                });
                Assert.StartsWith("tz_", created.Id);
                Assert.Equal(OutputFormat.Xml, created.DefaultFormat);
                Assert.Equal(RunMode.Precise, created.Mode);
                Assert.Equal(new[] { "invoice_number", "total", "seller" },
                    created.Schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray());

                SavedTypizer fetched = await client.Typizers.GetAsync(created.Id);
                Assert.Equal("УПД поставщиков", fetched.Name);

                SavedTypizer updated = await client.Typizers.UpdateAsync(created.Id, new TypizerUpdateOptions { Name = "УПД", Description = null });
                Assert.Equal("УПД", updated.Name);
                Assert.Null(updated.Description);

                var all = new List<SavedTypizer>();
                await foreach (SavedTypizer t in client.Typizers.ListAsync()) all.Add(t);
                Assert.Contains(all, t => t.Id == created.Id);

                DeletedObject deleted = await client.Typizers.DeleteAsync(created.Id);
                Assert.True(deleted.Deleted);
                await Assert.ThrowsAsync<TypizerNotFoundException>(() => client.Typizers.GetAsync(created.Id));
            }
            JsonElement patch = Assert.Single(await Mock.Requests(key, "updateTypizer"));
            Assert.Equal("PATCH", Mock.Str(patch, "method"));
            Assert.Equal("{\"name\":\"УПД\",\"description\":null}", patch.GetProperty("body").GetProperty("value").GetRawText().Replace("\\u0423\\u041F\\u0414", "УПД"));
            Assert.Equal(string.Empty, Mock.Header(patch, "idempotency-key"));
        }

        [Fact(DisplayName = "SC-08 Unknown fields and enum values do not break parsing (SPEC §5.4)")]
        public void SC08_ForwardCompatibleModels()
        {
            const string json = @"{""id"":""run_1"",""object"":""run"",""status"":""archived"",""mode"":""turbo"",""format"":""parquet"",
                ""source"":""robot"",""pages"":{""physical"":1,""billed"":null,""color"":2},""output"":null,""fields"":null,
                ""output_expired"":false,""error"":null,""file_id"":""file_1"",""file"":null,""batch_id"":null,""metadata"":{},
                ""catalog"":null,""created_at"":""2026-11-13T10:00:00.000Z"",""completed_at"":null,""region"":""ru-msk"",
                ""typizer_id"":null,""typizer_version"":null}";
            Run run = Internal.Json.Deserialize<Run>(Encoding.UTF8.GetBytes(json));
            Assert.Equal("archived", run.Status.Value);
            Assert.Equal(new RunStatus("archived"), run.Status);
            Assert.False(run.IsTerminal);
            Assert.Equal("turbo", run.Mode.ToString());
            Assert.Equal("parquet", run.Format.Value);
            Assert.Equal("ru-msk", run.ExtensionData!["region"].GetString());
            Assert.Equal(2, run.Pages.ExtensionData!["color"].GetInt32());
            Assert.Null(run.Pages.Billed);
            Assert.Null(run.Output);
            Assert.Empty(run.FieldsToReview());

            WebhookDelivery delivery = Internal.Json.Deserialize<WebhookDelivery>(Encoding.UTF8.GetBytes(
                @"{""id"":""whd_1"",""event_type"":""run.archived"",""status"":""queued"",""attempt"":1,""created_at"":""2026-11-13T10:00:00Z""}"));
            Assert.Equal("run.archived", delivery.EventType.Value);
            Assert.Equal("queued", delivery.Status.Value);
        }

        // ---- result shapes ---------------------------------------------------------------------

        [Fact(DisplayName = "SC-10 needs_review: NeedsReview is true, FieldsToReview gives warn and missing with paths, reason and sources")]
        public async Task SC10_NeedsReview()
        {
            using (TypizerClient client = Mock.Client(Mock.Key("needs_review")))
            {
                Run run = await client.ExtractAsync(new Uri(Schemas.SampleUrl), Schemas.Invoice);
                Assert.Equal(RunStatus.Succeeded, run.Status);
                Assert.True(run.NeedsReview);
                IReadOnlyList<(string Path, FieldResult Field)> review = run.FieldsToReview();
                Assert.Equal(2, review.Count);
                Assert.Equal("invoice_number", review[0].Path);
                Assert.Equal(FieldStatus.Warn, review[0].Field.Status);
                Assert.Equal("checksum_mismatch", review[0].Field.Reason);
                FieldSource source = Assert.Single(review[0].Field.Sources);
                Assert.Equal(1, source.Page);
                Assert.Equal(4, source.Bbox!.Count);
                Assert.Equal("А-1024", source.Text);
                Assert.Equal("seller.inn", review[1].Path);
                Assert.Equal(FieldStatus.Missing, review[1].Field.Status);
                Assert.Empty(review[1].Field.Sources);
                Assert.Equal(JsonValueKind.Null, run.Output!.Value.GetProperty("seller").GetProperty("inn").ValueKind);
            }
        }

        [Fact(DisplayName = "SC-11 run_failed: ExtractAsync throws TypizerRunFailedException with code, message and run; Runs.WaitAsync returns the run")]
        public async Task SC11_RunFailed()
        {
            string key = Mock.Key("run_failed");
            using (TypizerClient client = Mock.Client(key))
            {
                var error = await Assert.ThrowsAsync<TypizerRunFailedException>(() =>
                    client.ExtractAsync(TypizerFile.FromBytes(Schemas.Pdf, "scan.jpg"), Schemas.Invoice));
                Assert.Equal("document_unreadable", error.Code);
                Assert.False(string.IsNullOrEmpty(error.ErrorMessage));
                Assert.Equal(RunStatus.Failed, error.Run.Status);
                Assert.False(error.IsRetryable);
                Assert.IsAssignableFrom<TypizerException>(error);

                Run waited = await client.Runs.WaitAsync(error.Run.Id);
                Assert.Equal(RunStatus.Failed, waited.Status);
                Assert.Equal("document_unreadable", waited.Error!.Code);

                Run created = await client.Runs.CreateAsync(new Uri(Schemas.SampleUrl), new RunCreateOptions { Schema = Schemas.Invoice });
                Assert.Equal(RunStatus.Queued, created.Status);
                Run done = await client.Runs.WaitAsync(created.Id, TimeSpan.FromSeconds(30));
                Assert.Equal(RunStatus.Failed, done.Status);
            }
        }

        [Fact(DisplayName = "SC-12 output_expired: OutputExpired is set, Output and Fields are null without throwing, output download is 404")]
        public async Task SC12_OutputExpired()
        {
            using (TypizerClient client = Mock.Client(Mock.Key("output_expired")))
            {
                Run run = await client.ExtractAsync(new Uri(Schemas.SampleUrl), Schemas.Invoice);
                Assert.True(run.OutputExpired);
                Assert.Null(run.Output);
                Assert.Null(run.Fields);
                Assert.False(run.NeedsReview);
                Assert.Empty(run.FieldsToReview());
                Assert.Null(run.GetOutput<Dictionary<string, object>>());
                await Assert.ThrowsAsync<TypizerNotFoundException>(() => client.Runs.GetOutputAsync(run.Id));
            }
        }

        // ---- waiting ---------------------------------------------------------------------------

        [Fact(DisplayName = "SC-13 slow: ExtractAsync waits — exactly 1 POST (wait ≤ 60) and 3 GET /v1/runs/{id}")]
        public async Task SC13_SlowRunIsAwaited()
        {
            string key = Mock.Key("slow");
            using (TypizerClient client = Mock.Client(key))
            {
                Run run = await client.ExtractAsync(new Uri(Schemas.SampleUrl), Schemas.Invoice,
                    new ExtractOptions { PollInterval = TimeSpan.FromMilliseconds(100) });
                Assert.Equal(RunStatus.Succeeded, run.Status);
            }
            List<JsonElement> log = await Mock.Requests(key);
            Assert.Equal(new[] { "extract", "getRun", "getRun", "getRun" }, log.Select(e => Mock.Str(e, "operation_id")).ToArray());
            Assert.InRange(int.Parse(Mock.Str(log[0], "query", "wait")), 0, 60);
        }

        [Fact(DisplayName = "SC-14 slow: a too short timeout throws TypizerWaitTimeoutException with run_id; the run is not cancelled")]
        public async Task SC14_WaitTimeout()
        {
            string key = Mock.Key("slow");
            using (TypizerClient client = Mock.Client(key))
            {
                var stopwatch = Stopwatch.StartNew();
                var error = await Assert.ThrowsAsync<TypizerWaitTimeoutException>(() =>
                    client.ExtractAsync(new Uri(Schemas.SampleUrl), Schemas.Invoice,
                        new ExtractOptions { Timeout = TimeSpan.FromMilliseconds(500), PollInterval = TimeSpan.FromMilliseconds(300) }));
                stopwatch.Stop();
                Assert.StartsWith("run_", error.RunId);
                Assert.Equal(error.RunId, error.Run.Id);
                Assert.False(error.Run.IsTerminal);
                Assert.Contains("billed", error.Message);
                Assert.Contains("WaitAsync", error.Message);
                Assert.InRange(stopwatch.Elapsed.TotalSeconds, 0.45, 5);
                Assert.Equal(2, (await Mock.Requests(key, "getRun")).Count);
                Assert.Equal("0", Mock.Str((await Mock.Requests(key, "extract"))[0], "query", "wait"));

                // The run keeps going: waiting for it later works.
                Run run = await client.Runs.WaitAsync(error.RunId, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(50), null);
                Assert.Equal(RunStatus.Succeeded, run.Status);
            }
            List<JsonElement> log = await Mock.Requests(key);
            Assert.DoesNotContain(log, e => Mock.Str(e, "method") == "DELETE");
            Assert.Single(log, e => Mock.Str(e, "method") == "POST");
        }

        // ---- errors ----------------------------------------------------------------------------

        [Fact(DisplayName = "SC-15 unauthorized: TypizerAuthenticationException, request_id = X-Request-Id, no retries, the key nowhere")]
        public async Task SC15_Unauthorized()
        {
            string key = Mock.Key("unauthorized");
            var logs = new LogSink();
            TypizerAuthenticationException error;
            using (TypizerClient client = Mock.Client(key, o => o.Logger = logs.Write))
            {
                error = await Assert.ThrowsAsync<TypizerAuthenticationException>(() => client.GetBalanceAsync());
                Assert.DoesNotContain(key, client.ToString());
                Assert.Contains("tz_test_…", client.ToString());
            }
            Assert.Equal(401, error.StatusCode);
            Assert.Equal("unauthorized", error.Code);
            Assert.False(error.IsRetryable);
            JsonElement sent = Assert.Single(await Mock.Requests(key));
            Assert.Equal(error.RequestId, error.Headers["X-Request-Id"][0]);
            Assert.False(string.IsNullOrEmpty(error.RequestId));
            Assert.Equal(401, sent.GetProperty("response").GetProperty("status").GetInt32());
            Assert.DoesNotContain(key, error.Message);
            Assert.DoesNotContain(key, error.ToString());
            Assert.DoesNotContain(error.Headers, h => h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
            Assert.NotEmpty(logs.Lines);
            Assert.All(logs.Messages, m => Assert.DoesNotContain(key, m));
            Assert.DoesNotContain(key, new TypizerClientOptions { ApiKey = key }.ToString());
        }

        [Fact(DisplayName = "SC-16 insufficient_scope: TypizerPermissionDeniedException with code insufficient_scope")]
        public async Task SC16_InsufficientScope()
        {
            using (TypizerClient client = Mock.Client(Mock.Key("insufficient_scope")))
            {
                Me me = await client.GetMeAsync();
                Assert.NotNull(me.ApiKey);
                var error = await Assert.ThrowsAsync<TypizerPermissionDeniedException>(() => client.GetBalanceAsync());
                Assert.Equal(403, error.StatusCode);
                Assert.Equal("insufficient_scope", error.Code);
            }
        }

        [Fact(DisplayName = "SC-17 insufficient_balance: TypizerInsufficientBalanceException, status 402, not retried")]
        public async Task SC17_InsufficientBalance()
        {
            string key = Mock.Key("insufficient_balance");
            using (TypizerClient client = Mock.Client(key))
            {
                var error = await Assert.ThrowsAsync<TypizerInsufficientBalanceException>(() =>
                    client.ExtractAsync(new Uri(Schemas.SampleUrl), Schemas.Invoice));
                Assert.Equal(402, error.StatusCode);
                Assert.False(error.IsRetryable);
                Assert.Equal("https://docs.typizer.ru/errors/insufficient_balance", error.DocUrl);
                Assert.Equal(
                    "[insufficient_balance] Not enough pages on the balance: 1 needed, 0 available (HTTP 402, request_id=" + error.RequestId +
                    ", https://docs.typizer.ru/errors/insufficient_balance)", error.Message);
                Assert.Equal(0, (await client.GetBalanceAsync()).PagesAvailable);
            }
            Assert.Single(await Mock.Requests(key, "extract"));
        }

        [Fact(DisplayName = "SC-18 file_too_large: TypizerFileTooLargeException (413), not retried; a local file over 50 MiB never leaves")]
        public async Task SC18_FileTooLarge()
        {
            string key = Mock.Key("file_too_large");
            using (TypizerClient client = Mock.Client(key))
            {
                var error = await Assert.ThrowsAsync<TypizerFileTooLargeException>(() =>
                    client.ExtractAsync(TypizerFile.FromBytes(Schemas.Pdf, "big.pdf"), Schemas.Invoice));
                Assert.Equal(413, error.StatusCode);
                Assert.False(error.IsRetryable);
                Assert.Single(await Mock.Requests(key));

                var huge = new ZeroStream(TypizerFile.MaxBytes + 1);
                var local = await Assert.ThrowsAsync<TypizerFileTooLargeException>(() =>
                    client.ExtractAsync(TypizerFile.FromStream(huge, "huge.pdf"), Schemas.Invoice));
                Assert.Equal(413, local.StatusCode);
                Assert.Equal("file_too_large", local.Code);
                Assert.Null(local.RequestId);
            }
            Assert.Single(await Mock.Requests(key));
        }

        // ---- retries ---------------------------------------------------------------------------

        [Fact(DisplayName = "SC-19 rate_limited: succeeds by default — 2 requests ≥ 1 s apart with the same Idempotency-Key")]
        public async Task SC19_RateLimitedRetries()
        {
            string key = Mock.Key("rate_limited");
            var logs = new LogSink();
            using (TypizerClient client = Mock.Client(key, o => o.Logger = logs.Write))
            {
                var stopwatch = Stopwatch.StartNew();
                Run run = await client.ExtractAsync(new Uri(Schemas.SampleUrl), Schemas.Invoice);
                Assert.Equal(RunStatus.Succeeded, run.Status);
                Assert.True(stopwatch.Elapsed >= TimeSpan.FromSeconds(0.95));
            }
            List<JsonElement> log = await Mock.Requests(key, "extract");
            Assert.Equal(2, log.Count);
            Assert.Equal(429, log[0].GetProperty("response").GetProperty("status").GetInt32());
            Assert.Equal(Mock.Header(log[0], "idempotency-key"), Mock.Header(log[1], "idempotency-key"));
            Assert.Contains(logs.Lines, l => l.Level == TypizerLogLevel.Information &&
                                             l.Message.StartsWith("typizer: retrying POST /v1/extract in 1.0s after 429 rate_limited (attempt 2/3", StringComparison.Ordinal));
            Assert.Contains(logs.Lines, l => l.Level == TypizerLogLevel.Debug &&
                                             Regex.IsMatch(l.Message, @"^typizer: POST /v1/extract -> 200 in \d+\.\d\ds \(request_id=\S+, attempt 2/3\)$"));
        }

        [Fact(DisplayName = "SC-20 rate_limited with MaxRetries = 0: TypizerRateLimitException, RetryAfter = 1 s, retryable")]
        public async Task SC20_RateLimitedNoRetries()
        {
            string key = Mock.Key("rate_limited");
            using (TypizerClient client = Mock.Client(key, o => o.MaxRetries = 0))
            {
                var error = await Assert.ThrowsAsync<TypizerRateLimitException>(() => client.GetMeAsync());
                Assert.Equal(TimeSpan.FromSeconds(1), error.RetryAfter);
                Assert.True(error.IsRetryable);
                Assert.Equal(429, error.StatusCode);
                Assert.Equal(0, error.RateLimit!.Remaining);
                Assert.Equal(20, error.RateLimit.Limit);
            }
            Assert.Single(await Mock.Requests(key));
        }

        [Fact(DisplayName = "SC-21 rate_limited_plain: a text/plain 429 does not break parsing; retried, or rate_limited with empty request_id")]
        public async Task SC21_PlainRateLimit()
        {
            string retried = Mock.Key("rate_limited_plain");
            using (TypizerClient client = Mock.Client(retried))
            {
                Me me = await client.GetMeAsync();
                Assert.Equal("me", me.Object);
            }
            Assert.Equal(2, (await Mock.Requests(retried)).Count);

            using (TypizerClient client = Mock.Client(Mock.Key("rate_limited_plain"), o => o.MaxRetries = 0))
            {
                var error = await Assert.ThrowsAsync<TypizerRateLimitException>(() => client.GetMeAsync());
                Assert.Equal("rate_limited", error.Code);
                Assert.True(string.IsNullOrEmpty(error.RequestId));
                Assert.Equal("Too Many Requests", error.Detail);
                Assert.Equal("Too Many Requests", error.Body);
                Assert.Null(error.Problem);
                Assert.Equal(TimeSpan.FromSeconds(1), error.RetryAfter);
            }
        }

        [Fact(DisplayName = "SC-22 unavailable: after 1 + MaxRetries attempts (3 requests) — TypizerServiceUnavailableException, retryable")]
        public async Task SC22_Unavailable()
        {
            string key = Mock.Key("unavailable");
            using (TypizerClient client = Mock.Client(key))
            {
                var error = await Assert.ThrowsAsync<TypizerServiceUnavailableException>(() => client.Runs.GetAsync("run_00000000000000000000000000"));
                Assert.True(error.IsRetryable);
                Assert.Equal(503, error.StatusCode);
                Assert.Equal("upstream_unavailable", error.Code);
            }
            Assert.Equal(3, (await Mock.Requests(key)).Count);
        }

        [Fact(DisplayName = "SC-23 unavailable_once: succeeds — 2 requests with the same Idempotency-Key")]
        public async Task SC23_UnavailableOnce()
        {
            string key = Mock.Key("unavailable_once");
            using (TypizerClient client = Mock.Client(key))
            {
                SavedTypizer typizer = await client.Typizers.CreateAsync("A", Schemas.Invoice);
                Assert.StartsWith("tz_", typizer.Id);
            }
            List<JsonElement> log = await Mock.Requests(key);
            Assert.Equal(2, log.Count);
            Assert.Equal(503, log[0].GetProperty("response").GetProperty("status").GetInt32());
            Assert.Equal(Mock.Header(log[0], "idempotency-key"), Mock.Header(log[1], "idempotency-key"));
        }

        [Fact(DisplayName = "SC-24 internal_error_once: succeeds after an exponential pause (≈0.375–0.5 s, no Retry-After)")]
        public async Task SC24_InternalErrorOnce()
        {
            string key = Mock.Key("internal_error_once");
            using (TypizerClient client = Mock.Client(key))
            {
                var stopwatch = Stopwatch.StartNew();
                Balance balance = await client.GetBalanceAsync();
                stopwatch.Stop();
                Assert.Equal(950, balance.PagesAvailable);
                Assert.InRange(stopwatch.Elapsed.TotalSeconds, 0.35, 3);
            }
            List<JsonElement> log = await Mock.Requests(key);
            Assert.Equal(new[] { 500, 200 }, log.Select(e => e.GetProperty("response").GetProperty("status").GetInt32()).ToArray());
        }

        [Fact(DisplayName = "SC-25 drop_first_response: ExtractAsync succeeds; 2 requests with one Idempotency-Key, the second replayed; 26 runs, not 27")]
        public async Task SC25_DroppedResponseReplayed()
        {
            string key = Mock.Key("drop_first_response");
            using (TypizerClient client = Mock.Client(key))
            {
                Run run = await client.ExtractAsync(TypizerFile.FromBytes(Schemas.Pdf, "invoice.pdf"), Schemas.Invoice);
                Assert.Equal(RunStatus.Succeeded, run.Status);

                List<JsonElement> log = await Mock.Requests(key, "extract");
                Assert.Equal(2, log.Count);
                Assert.True(log[0].GetProperty("response").GetProperty("dropped").GetBoolean());
                Assert.True(log[1].GetProperty("response").GetProperty("idempotent_replayed").GetBoolean());
                Assert.Equal(Mock.Header(log[0], "idempotency-key"), Mock.Header(log[1], "idempotency-key"));

                int count = 0;
                await foreach (Run _ in client.Runs.ListAsync(new RunListOptions { Limit = 100 })) count++;
                Assert.Equal(26, count);
            }
        }

        [Fact(DisplayName = "SC-26 slow_response: Timeout 0.5 s, MaxRetries 0 — TypizerTimeoutException (a TypizerConnectionException, retryable) in ≈0.5 s")]
        public async Task SC26_AttemptTimeout()
        {
            using (TypizerClient client = Mock.Client(Mock.Key("slow_response"), o =>
            {
                o.Timeout = TimeSpan.FromMilliseconds(500);
                o.MaxRetries = 0;
            }))
            {
                var stopwatch = Stopwatch.StartNew();
                var error = await Assert.ThrowsAsync<TypizerTimeoutException>(() => client.GetMeAsync());
                stopwatch.Stop();
                Assert.IsAssignableFrom<TypizerConnectionException>(error);
                Assert.True(error.IsRetryable);
                Assert.InRange(stopwatch.Elapsed.TotalSeconds, 0.4, 2.0);
            }
        }

        [Fact(DisplayName = "SC-27 maintenance: calls succeed; the Typizer-Maintenance warning is logged once per client")]
        public async Task SC27_MaintenanceWarning()
        {
            var logs = new LogSink();
            using (TypizerClient client = Mock.Client(Mock.Key("maintenance"), o => o.Logger = logs.Write))
            {
                await client.GetMeAsync();
                await client.GetBalanceAsync();
                TypizerResponse<Me> response = await client.GetMeWithResponseAsync();
                Assert.StartsWith("mnt_", response.Maintenance);
                Status status = await client.GetStatusAsync();
                Assert.NotEmpty(status.Data);
            }
            var warning = Assert.Single(logs.Lines, l => l.Level == TypizerLogLevel.Warning);
            Assert.Contains("mnt_", warning.Message);
        }

        // ---- checks without a scenario ---------------------------------------------------------

        [Fact(DisplayName = "SC-30 The same explicit Idempotency-Key with another body: TypizerIdempotencyConflictException (409, not retryable)")]
        public async Task SC30_IdempotencyConflict()
        {
            string key = Mock.Key("ok");
            using (TypizerClient client = Mock.Client(key))
            {
                var options = new RequestOptions { IdempotencyKey = "same-key" };
                await client.Typizers.CreateAsync("A", Schemas.Invoice, options);
                SavedTypizer replay = await client.Typizers.CreateAsync("A", Schemas.Invoice, options);
                Assert.Equal("A", replay.Name);
                var error = await Assert.ThrowsAsync<TypizerIdempotencyConflictException>(() =>
                    client.Typizers.CreateAsync("B", Schemas.Invoice, options));
                Assert.IsAssignableFrom<TypizerConflictException>(error);
                Assert.Equal(409, error.StatusCode);
                Assert.Equal("idempotency_conflict", error.Code);
                Assert.False(error.IsRetryable);
            }
            Assert.Equal(3, (await Mock.Requests(key)).Count);
        }

        [Fact(DisplayName = "SC-31 ExtractAsync without a schema is an argument error before any request; {type: string} is invalid_schema from the server")]
        public async Task SC31_Validation()
        {
            string key = Mock.Key("ok");
            using (TypizerClient client = Mock.Client(key))
            {
                await Assert.ThrowsAsync<ArgumentException>(() => client.ExtractAsync(new Uri(Schemas.SampleUrl), new ExtractOptions()));
                await Assert.ThrowsAsync<ArgumentException>(() =>
                    client.ExtractAsync(new Uri(Schemas.SampleUrl), new ExtractOptions { Catalog = "upd", Typizer = "tz_1" }));
                await Assert.ThrowsAsync<ArgumentException>(() =>
                    client.ExtractAsync(new Uri(Schemas.SampleUrl), new ExtractOptions { Catalog = "upd", Instructions = "Только первая страница" }));
                await Assert.ThrowsAsync<ArgumentNullException>(() => client.ExtractAsync(new Uri(Schemas.SampleUrl), (object)null!));
                await Assert.ThrowsAsync<ArgumentException>(() => client.ExtractAsync(new Uri(Schemas.SampleUrl), "schet-faktura@1"));
                Assert.Empty(await Mock.Requests(key));

                var error = await Assert.ThrowsAsync<TypizerBadRequestException>(() =>
                    client.ExtractAsync(new Uri(Schemas.SampleUrl), "{\"type\": \"string\"}"));
                Assert.Equal("invalid_schema", error.Code);
                Assert.Equal("schema", error.Param);
                Assert.Equal(400, error.StatusCode);
            }
        }

        [Fact(DisplayName = "SC-33 A caller's HttpClient and HttpMessageHandler carry the requests to the mock")]
        public async Task SC33_OwnHttpClientAndHandler()
        {
            string key = Mock.Key("ok");
            var recorder = new RecordingHandler();
            using (var http = new HttpClient(recorder))
            {
                using (var client = new TypizerClient(new TypizerClientOptions { ApiKey = key, BaseUrl = new Uri(Mock.BaseUrl), HttpClient = http }))
                {
                    await client.GetMeAsync();
                }
                // Disposing the SDK client leaves a caller's HttpClient usable.
                using (var client = new TypizerClient(new TypizerClientOptions { ApiKey = key, BaseUrl = new Uri(Mock.BaseUrl) }, http))
                {
                    await client.GetBalanceAsync();
                }
            }
            var handler = new RecordingHandler();
            using (var client = Mock.Client(key, o => o.HttpMessageHandler = handler))
            {
                await client.ExtractAsync(TypizerFile.FromBytes(Schemas.Pdf, "a.pdf"), Schemas.Invoice);
            }
            Assert.Equal(new[] { "/v1/me", "/v1/balance" }, recorder.Requests.Select(r => r.Uri.AbsolutePath).ToArray());
            Assert.Single(handler.Requests);
            Assert.Equal(3, (await Mock.Requests(key)).Count);
        }

        [Fact(DisplayName = "SC-33 Proxy: requests go through the configured proxy (with credentials from the URL)")]
        public async Task SC33_Proxy()
        {
            string key = Mock.Key("ok");
            using (var proxy = new TinyHttpProxy())
            {
                IWebProxy configured = TypizerProxy.FromUrl("http://user:p%40ss@127.0.0.1:" + proxy.Port);
                var credentials = (NetworkCredential)configured.Credentials!.GetCredential(new Uri("http://127.0.0.1"), "Basic")!;
                Assert.Equal("user", credentials.UserName);
                Assert.Equal("p@ss", credentials.Password);

                using (var client = Mock.Client(key, o => o.Proxy = new AlwaysProxy(new Uri("http://127.0.0.1:" + proxy.Port))))
                {
                    Me me = await client.GetMeAsync();
                    Assert.Equal("me", me.Object);
                }
#if !NETFRAMEWORK
                // .NET Framework never proxies a loopback destination (HttpWebRequest bypasses any
                // IWebProxy for 127.0.0.1/localhost), so there the mock is reached directly; the proxy's
                // credentials above are still checked.
                Assert.Contains(proxy.Seen, line => line.StartsWith("GET " + Mock.BaseUrl + "/v1/me", StringComparison.Ordinal));
#endif
            }
            Assert.Single(await Mock.Requests(key));
        }

        [Fact(DisplayName = "SC-34 Cancellation stops ExtractAsync's waiting at once, without further requests")]
        public async Task SC34_Cancellation()
        {
            string key = Mock.Key("slow");
            using (TypizerClient client = Mock.Client(key))
            using (var cts = new CancellationTokenSource())
            {
                Task<Run> extract = client.ExtractAsync(new Uri(Schemas.SampleUrl), Schemas.Invoice,
                    new ExtractOptions { PollInterval = TimeSpan.FromSeconds(10) }, cts.Token);
                // Let the POST land, then cancel during the 10-second pause.
                for (int i = 0; i < 50 && (await Mock.Requests(key)).Count == 0; i++) await Task.Delay(50);
                await Task.Delay(200);
                var stopwatch = Stopwatch.StartNew();
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => extract);
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
            }
            await Task.Delay(300);
            List<JsonElement> log = await Mock.Requests(key);
            JsonElement only = Assert.Single(log);
            Assert.Equal("extract", Mock.Str(only, "operation_id"));
        }

        [Fact(DisplayName = "SC-34 A token cancelled before the call sends nothing")]
        public async Task SC34_CancelledBeforehand()
        {
            string key = Mock.Key("ok");
            using (TypizerClient client = Mock.Client(key))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetMeAsync(cancellationToken: new CancellationToken(true)));
            }
            Assert.Empty(await Mock.Requests(key));
        }
    }

    /// <summary>A stream that cannot seek (a network or archive stream).</summary>
    internal sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;
        public NonSeekableStream(byte[] data) => _inner = new MemoryStream(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A seekable stream of zeros of any length, without the memory.</summary>
    internal sealed class ZeroStream : Stream
    {
        private readonly long _length;
        public ZeroStream(long length) => _length = length;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = (int)Math.Max(0, Math.Min(count, _length - Position));
            Array.Clear(buffer, offset, n);
            Position += n;
            return n;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) =>
            Position = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? Position + offset : _length + offset;
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    internal sealed class AlwaysProxy : IWebProxy
    {
        private readonly Uri _proxy;
        public AlwaysProxy(Uri proxy) => _proxy = proxy;
        public ICredentials? Credentials { get; set; }
        public Uri GetProxy(Uri destination) => _proxy;
        public bool IsBypassed(Uri host) => false;
    }

    /// <summary>
    /// A forward HTTP proxy in a few lines: reads the request head (absolute URI), forwards the request
    /// to the target and pipes the answer back. Enough to prove the SDK's requests take the proxy.
    /// </summary>
    internal sealed class TinyHttpProxy : IDisposable
    {
        private readonly System.Net.Sockets.TcpListener _listener;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        public TinyHttpProxy()
        {
            _listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

        public int Port { get; }
        public System.Collections.Concurrent.ConcurrentQueue<string> Seen { get; } = new System.Collections.Concurrent.ConcurrentQueue<string>();

        private async Task AcceptLoop()
        {
            while (!_stop.IsCancellationRequested)
            {
                System.Net.Sockets.TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }
                _ = Task.Run(() => Serve(client));
            }
        }

        private async Task Serve(System.Net.Sockets.TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                var head = new List<byte>();
                var one = new byte[1];
                while (!(head.Count >= 4 && head[head.Count - 4] == '\r' && head[head.Count - 3] == '\n' && head[head.Count - 2] == '\r' && head[head.Count - 1] == '\n'))
                {
                    if (await stream.ReadAsync(one, 0, 1).ConfigureAwait(false) == 0) return;
                    head.Add(one[0]);
                }
                string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                Seen.Enqueue(lines[0]);
                string[] requestLine = lines[0].Split(' ');
                using (var http = new HttpClient())
                using (var request = new HttpRequestMessage(new HttpMethod(requestLine[0]), requestLine[1]))
                {
                    foreach (string line in lines.Skip(1).Where(l => l.Length > 0))
                    {
                        int colon = line.IndexOf(':');
                        string name = line.Substring(0, colon);
                        if (name.Equals("Host", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
                        request.Headers.TryAddWithoutValidation(name, line.Substring(colon + 1).Trim());
                    }
                    using (HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false))
                    {
                        byte[] body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        var sb = new StringBuilder();
                        sb.Append("HTTP/1.1 ").Append((int)response.StatusCode).Append(' ').Append(response.ReasonPhrase).Append("\r\n");
                        foreach (var h in response.Headers.Concat(response.Content.Headers))
                        {
                            if (h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) || h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                                h.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
                            sb.Append(h.Key).Append(": ").Append(string.Join(", ", h.Value)).Append("\r\n");
                        }
                        sb.Append("Content-Length: ").Append(body.Length).Append("\r\nConnection: close\r\n\r\n");
                        byte[] responseHead = Encoding.ASCII.GetBytes(sb.ToString());
                        await stream.WriteAsync(responseHead, 0, responseHead.Length).ConfigureAwait(false);
                        await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                    }
                }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }
}
