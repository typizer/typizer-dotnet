using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Typizer.Tests
{
    /// <summary>
    /// Every public operation, called through the SDK against the mock (which validates each request
    /// against openapi.public.json and answers with spec-valid bodies the SDK must parse).
    /// </summary>
    public class AllOperationsTests
    {
        [Fact(DisplayName = "All 57 operations succeed against the mock and parse into models")]
        public async Task EveryOperationAgainstTheMock()
        {
            string key = Mock.Key("ok");
            using (TypizerClient c = Mock.Client(key))
            {
                // Account
                Assert.Equal("status", (await c.GetStatusAsync()).Object);
                JsonElement openapi = await c.GetOpenApiDocumentAsync();
                Assert.StartsWith("3.", openapi.GetProperty("openapi").GetString());
                Assert.Equal("api_key", (await c.GetMeAsync()).Auth);
                Assert.False(string.IsNullOrEmpty((await c.GetOrganizationAsync()).Settings.FileRetention));
                Assert.True((await c.GetBalanceAsync()).PagesAvailable > 0);
                Usage usage = await c.GetUsageAsync(new UsageOptions { From = new DateTime(2026, 11, 1), To = new DateTime(2026, 11, 13), GroupBy = "day" });
                Assert.Equal(new DateTime(2026, 11, 1), usage.From);

                // Files
                UploadedFile file = await c.Files.UploadAsync(TypizerFile.FromBytes(Schemas.Pdf, "invoice.pdf"));
                Assert.Equal(file.Id, (await c.Files.GetAsync(file.Id)).Id);
                using (TypizerDownload content = await c.Files.GetContentAsync(file.Id))
                {
                    Assert.Equal(Schemas.Pdf, await content.ReadAsBytesAsync());
                }

                // Typizers
                SavedTypizer typizer = await c.Typizers.CreateAsync("Счета", Schemas.Invoice);
                Assert.Equal(typizer.Id, (await c.Typizers.GetAsync(typizer.Id)).Id);
                SavedTypizer updated = await c.Typizers.UpdateAsync(typizer.Id, new TypizerUpdateOptions { Schema = Schemas.Invoice.Replace("total", "amount") });
                Assert.Equal(2, updated.Version);
                Page<SavedTypizer> typizers = await c.Typizers.ListAsync();
                Assert.Contains(typizers, t => t.Id == typizer.Id);
                Page<TypizerVersion> versions = await c.Typizers.Versions.ListAsync(typizer.Id);
                Assert.Equal(2, versions.Count);
                Assert.Equal(1, (await c.Typizers.Versions.GetAsync(typizer.Id, 1)).Version);
                using (TypizerDownload xsd = await c.Typizers.GetXsdAsync(typizer.Id))
                {
                    Assert.Contains("xs:schema", await xsd.ReadAsStringAsync());
                }
                TypizerDraft draft = await c.Typizers.GenerateAsync("Счёт: номер, дата и сумма");
                Assert.Equal(JsonValueKind.Object, draft.Schema.ValueKind);

                // Catalog
                Page<CatalogEntryBrief> catalog = await c.Catalog.ListAsync(new CatalogListOptions { Kind = CatalogKind.Official });
                Assert.NotEmpty(catalog);
                CatalogEntry entry = await c.Catalog.GetAsync("schet-faktura@1");
                Assert.Equal("schet-faktura", entry.Ref);
                Assert.Equal(JsonValueKind.Object, entry.Version.Schema.ValueKind);
                CatalogEntry community = await c.Catalog.GetAsync("acme/akt-sverki");
                Assert.Equal(CatalogKind.Community, community.Kind);
                IReadOnlyList<CatalogVersionBrief> catalogVersions = await c.Catalog.ListVersionsAsync("schet-faktura");
                Assert.NotEmpty(catalogVersions);
                SavedTypizer installed = await c.Catalog.InstallAsync("schet-faktura");
                Assert.Equal("installed", installed.CatalogSource!.Relation);
                SavedTypizer forked = await c.Catalog.ForkAsync("acme/akt-sverki", new CatalogInstallOptions { Name = "Мой акт" });
                Assert.Equal("forked", forked.CatalogSource!.Relation);
                Assert.True((await c.Catalog.FavoriteAsync("upd")).Favorited);
                Assert.False((await c.Catalog.UnfavoriteAsync("upd")).Favorited);
                Assert.Equal("open", (await c.Catalog.ReportAsync("acme/akt-sverki", "broken", "Не находит ИНН")).Status);
                Assert.Equal("acme", (await c.Catalog.Publishers.GetAsync("acme")).Slug);
                CatalogUpdate update = await c.Typizers.CatalogUpdate.GetAsync(installed.Id);
                Assert.Equal(installed.Id, update.TypizerId);
                SavedTypizer applied = await c.Typizers.CatalogUpdate.ApplyAsync(installed.Id, update.To);
                Assert.Equal(installed.Id, applied.Id);

                // Runs
                Run extracted = await c.Runs.CreateAsync(new Uri(Schemas.SampleUrl), new RunCreateOptions { Schema = Schemas.Invoice, Wait = TimeSpan.FromSeconds(30) });
                Assert.Equal(RunStatus.Succeeded, extracted.Status);
                Run viaTypizer = await c.Runs.CreateAsync(TypizerFile.FromId(file), new RunCreateOptions { Typizer = typizer.Id, Wait = TimeSpan.FromSeconds(30) });
                Assert.Equal(typizer.Id, viaTypizer.TypizerId);
                Assert.Equal(extracted.Id, (await c.Runs.GetAsync(extracted.Id)).Id);
                Page<Run> failed = await c.Runs.ListAsync(new RunListOptions { Status = RunStatus.Failed, Limit = 1 });
                Run failedRun = Assert.Single(failed);
                using (TypizerDownload xml = await c.Runs.GetOutputAsync(extracted.Id, OutputFormat.Xml))
                {
                    Assert.Equal("application/xml", xml.ContentType);
                    Assert.Equal(extracted.Id + ".xml", xml.FileName);
                    Assert.Contains("<invoice_number>", await xml.ReadAsStringAsync());
                }
                Run retried = await c.Runs.RetryAsync(failedRun.Id);
                Assert.NotEqual(failedRun.Id, retried.Id);

                // Batches
                Batch batch = await c.Batches.CreateAsync(typizer.Id, new[] { TypizerFile.FromId(file) });
                Assert.Equal(1, batch.Total);
                Batch byFiles = await c.Batches.CreateAsync(typizer.Id, new[] { TypizerFile.FromBytes(Schemas.Pdf, "a.pdf"), TypizerFile.FromBytes(Schemas.Pdf, "b.pdf") });
                Assert.Equal(2, byFiles.Total);
                Assert.Equal(batch.Id, (await c.Batches.GetAsync(batch.Id)).Id);
                Assert.Contains(await c.Batches.ListAsync(), b => b.Id == batch.Id);
                using (TypizerDownload csv = await c.Batches.ExportAsync(batch.Id, "csv"))
                {
                    Assert.False(string.IsNullOrEmpty(await csv.ReadAsStringAsync()));
                }

                // Webhook endpoints
                WebhookEndpoint endpoint = await c.WebhookEndpoints.CreateAsync("https://example.com/typizer", new[] { WebhookEventType.RunSucceeded });
                Assert.StartsWith("whsec_", endpoint.Secret);
                Assert.Equal(endpoint.Id, (await c.WebhookEndpoints.GetAsync(endpoint.Id)).Id);
                WebhookDelivery test = await c.WebhookEndpoints.TestAsync(endpoint.Id);
                // before disabling it: a disabled endpoint gets no test events
                Assert.Equal(WebhookEventType.RunSucceeded, test.EventType);
                WebhookEndpoint changed = await c.WebhookEndpoints.UpdateAsync(endpoint.Id, new WebhookEndpointUpdateOptions
                {
                    Events = new[] { WebhookEventType.RunSucceeded, WebhookEventType.RunFailed },
                    Status = WebhookEndpointStatus.Disabled,
                });
                Assert.Equal(WebhookEndpointStatus.Disabled, changed.Status);
                Assert.Contains(await c.WebhookEndpoints.ListAsync(), e => e.Id == endpoint.Id);
                Page<WebhookDelivery> deliveries = await c.WebhookEndpoints.Deliveries.ListAsync(endpoint.Id);
                Assert.NotEmpty(deliveries);
                WebhookDelivery redelivered = await c.WebhookEndpoints.Deliveries.RetryAsync(deliveries[0].Id);
                Assert.Equal(deliveries[0].EventId, redelivered.EventId);
                Assert.True((await c.WebhookEndpoints.DeleteAsync(endpoint.Id)).Deleted);

                // Validators
                var own = new List<Validator>();
                await foreach (Validator v in c.Validators.ListAsync()) own.Add(v);
                Validator seeded = Assert.Single(own, v => v.Ref == "my/contract-no");
                Assert.Equal(seeded.Id, (await c.Validators.GetAsync("my/contract-no")).Id);
                Assert.NotEmpty((await c.Validators.GetAsync(seeded.Id)).Versions);
                Validator created = await c.Validators.CreateAsync("Номер спецификации", Specs.ContractNo, new[] { ValidatorTest.Pass("ДГ-2026/0001") });
                Assert.Equal("1.0.0", created.Version.Version);
                ValidatorUpdateResult bumped = await c.Validators.UpdateAsync(created.Id, new ValidatorUpdateOptions { Name = "Номер спецификации (СП)" });
                Assert.Equal(ValidatorBump.Patch, bumped.Bump);
                ValidatorTestResult tested = await c.Validators.TestAsync(new[] { "ДГ-2026/0153" }, reference: "my/contract-no@1");
                Assert.True(Assert.Single(tested.Results).Pass);
                ValidatorSuggestion suggestion = await c.Validators.SuggestAsync(new ValidatorSuggestOptions { Examples = new ValidatorExamples { Pass = new[] { "СП-2026/0001" } } });
                Assert.Equal(JsonValueKind.Object, suggestion.Spec.ValueKind);
                Assert.True((await c.Validators.ArchiveAsync(created.Id)).Deleted);

                Assert.True((await c.Typizers.DeleteAsync(typizer.Id)).Deleted);
                Assert.True((await c.Files.DeleteAsync(file.Id)).Deleted);
            }

            List<JsonElement> log = await Mock.Requests(key);
            var failedCalls = log.Where(e => e.GetProperty("response").GetProperty("status").GetInt32() >= 300)
                .Select(e => Mock.Str(e, "operation_id") + " " + e.GetProperty("response").GetProperty("status").GetInt32()).ToList();
            Assert.Empty(failedCalls);
            var covered = new HashSet<string>(log.Select(e => Mock.Str(e, "operation_id")));
            var missing = ContractTests.SpecOperations().Keys.Where(id => !covered.Contains(id)).OrderBy(id => id).ToList();
            Assert.Empty(missing);
        }
    }
}
