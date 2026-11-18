using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Typizer.Tests
{
    /// <summary>
    /// The SDK against apps/docs/openapi.public.json — the only API contract. Every public operation has
    /// exactly one SDK call, and that call sends exactly that method, path, query parameters, body kind
    /// and Idempotency-Key rule; the models carry exactly the schemas' fields. A new operation or field in
    /// the spec fails this test until the SDK covers it.
    /// </summary>
    public class ContractTests
    {
        internal sealed class SpecOperation
        {
            public string Id = string.Empty;
            public string Method = string.Empty;
            public string Path = string.Empty;
            public HashSet<string> Query = new HashSet<string>();
            public HashSet<string> BodyTypes = new HashSet<string>();
            public bool BodyRequired;
            public bool Idempotent;
            public bool Download;
        }

        private static readonly Lazy<JsonElement> Spec = new Lazy<JsonElement>(() =>
        {
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Repo.OpenApiPath)))
            {
                return doc.RootElement.Clone();
            }
        });

        internal static JsonElement Resolve(JsonElement node)
        {
            while (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("$ref", out JsonElement reference))
            {
                JsonElement target = Spec.Value;
                foreach (string segment in reference.GetString()!.TrimStart('#', '/').Split('/'))
                    target = target.GetProperty(segment.Replace("~1", "/").Replace("~0", "~"));
                node = target;
            }
            return node;
        }

        internal static Dictionary<string, SpecOperation> SpecOperations()
        {
            var result = new Dictionary<string, SpecOperation>();
            foreach (JsonProperty path in Spec.Value.GetProperty("paths").EnumerateObject())
            {
                var shared = path.Value.TryGetProperty("parameters", out JsonElement p) ? p.EnumerateArray().ToList() : new List<JsonElement>();
                foreach (JsonProperty method in path.Value.EnumerateObject())
                {
                    if (method.Value.ValueKind != JsonValueKind.Object || !method.Value.TryGetProperty("operationId", out JsonElement id)) continue;
                    var op = new SpecOperation { Id = id.GetString()!, Method = method.Name.ToUpperInvariant(), Path = path.Name };
                    var parameters = shared.Concat(method.Value.TryGetProperty("parameters", out JsonElement own) ? own.EnumerateArray() : Enumerable.Empty<JsonElement>());
                    foreach (JsonElement raw in parameters)
                    {
                        bool idem = raw.TryGetProperty("$ref", out JsonElement r) && r.GetString() == "#/components/parameters/IdempotencyKey";
                        JsonElement param = Resolve(raw);
                        if (param.GetProperty("in").GetString() == "query") op.Query.Add(param.GetProperty("name").GetString()!);
                        op.Idempotent |= idem;
                    }
                    if (method.Value.TryGetProperty("requestBody", out JsonElement body))
                    {
                        foreach (JsonProperty ct in body.GetProperty("content").EnumerateObject()) op.BodyTypes.Add(ct.Name);
                        op.BodyRequired = body.TryGetProperty("required", out JsonElement req) && req.GetBoolean();
                    }
                    foreach (JsonProperty response in method.Value.GetProperty("responses").EnumerateObject())
                    {
                        if (!response.Name.StartsWith("2", StringComparison.Ordinal)) continue;
                        JsonElement resolved = Resolve(response.Value);
                        if (resolved.TryGetProperty("content", out JsonElement content))
                            op.Download |= content.EnumerateObject().Any(c => c.Name != "application/json");
                    }
                    result[op.Id] = op;
                }
            }
            return result;
        }

        private const string Id = "x_1";

        /// <summary>One SDK call per public operation (official slugs, so entry-scoped catalog calls need no lookup).</summary>
        internal static readonly Dictionary<string, Func<TypizerClient, Task>> Calls = new Dictionary<string, Func<TypizerClient, Task>>
        {
            ["getStatus"] = c => c.GetStatusAsync(),
            ["getOpenApiDocument"] = c => c.GetOpenApiDocumentAsync(),
            ["getMe"] = c => c.GetMeAsync(),
            ["getOrganization"] = c => c.GetOrganizationAsync(),
            ["getBalance"] = c => c.GetBalanceAsync(),
            ["getUsage"] = c => c.GetUsageAsync(new UsageOptions { From = new DateTime(2026, 11, 1), To = new DateTime(2026, 11, 13), GroupBy = "typizer" }),
            ["createFile"] = c => c.Files.UploadAsync(TypizerFile.FromBytes(Schemas.Pdf, "a.pdf")),
            ["getFile"] = c => c.Files.GetAsync(Id),
            ["deleteFile"] = c => c.Files.DeleteAsync(Id),
            ["getFileContent"] = c => c.Files.GetContentAsync(Id),
            ["listTypizers"] = c => c.Typizers.ListAsync(new ListOptions { Limit = 5, Cursor = "tz_0" }).GetPageAsync(),
            ["createTypizer"] = c => c.Typizers.CreateAsync(new TypizerCreateOptions { Name = "A", Schema = Schemas.Invoice, Description = "d", Instructions = "i", DefaultFormat = OutputFormat.Csv, Mode = RunMode.Standard }),
            ["generateTypizer"] = c => c.Typizers.GenerateAsync(new TypizerGenerateOptions { Description = "Счёт", FileId = "file_1" }),
            ["getTypizer"] = c => c.Typizers.GetAsync(Id),
            ["updateTypizer"] = c => c.Typizers.UpdateAsync(Id, new TypizerUpdateOptions { Name = "B", Schema = Schemas.Invoice, Description = null, Instructions = "i", DefaultFormat = OutputFormat.Json, Mode = RunMode.Precise }),
            ["deleteTypizer"] = c => c.Typizers.DeleteAsync(Id),
            ["listTypizerVersions"] = c => c.Typizers.Versions.ListAsync(Id, new ListOptions { Limit = 2, Cursor = "tzv_1" }).GetPageAsync(),
            ["getTypizerVersion"] = c => c.Typizers.Versions.GetAsync(Id, 2),
            ["getTypizerXsd"] = c => c.Typizers.GetXsdAsync(Id),
            ["getCatalogPublisher"] = c => c.Catalog.Publishers.GetAsync("acme"),
            ["listCatalog"] = c => c.Catalog.Validators.ListAsync(new CatalogListOptions { Query = "счёт", Category = CatalogCategory.Invoices, Kind = CatalogKind.Official, Publisher = "typizer", Favorites = true, Sort = "popular", Limit = 3, Cursor = "c" }).GetPageAsync(),
            ["installCatalogEntry"] = c => c.Catalog.InstallAsync("schet-faktura@1", new CatalogInstallOptions { Name = "СФ" }),
            ["forkCatalogEntry"] = c => c.Catalog.Validators.ForkAsync("inn", new CatalogForkValidatorOptions { Version = "1.0.0", Slug = "my-inn", Name = "Мой ИНН" }),
            ["favoriteCatalogEntry"] = c => c.Catalog.Validators.FavoriteAsync("inn"),
            ["unfavoriteCatalogEntry"] = c => c.Catalog.Validators.UnfavoriteAsync("inn"),
            ["listCatalogVersions"] = c => c.Catalog.Validators.ListVersionsAsync("inn"),
            ["reportCatalogEntry"] = c => c.Catalog.Validators.ReportAsync("inn@1", "false_positive", "Отклоняет верный ИНН"),
            ["getCatalogEntry"] = c => c.Catalog.Validators.GetAsync("inn@1"),
            ["getCommunityCatalogEntry"] = c => c.Catalog.Validators.GetAsync("acme/contract-no", "1.2.0"),
            ["getTypizerCatalogUpdate"] = c => c.Typizers.CatalogUpdate.GetAsync(Id, "2"),
            ["applyTypizerCatalogUpdate"] = c => c.Typizers.CatalogUpdate.ApplyAsync(Id, "2.0.0", new[] { "seller.inn" }),
            ["createRun"] = c => c.Runs.CreateAsync(TypizerFile.FromBytes(Schemas.Pdf, "a.pdf"), new RunCreateOptions { Typizer = Id, Wait = TimeSpan.FromSeconds(30), Format = OutputFormat.Xml, Mode = RunMode.Standard, WebhookUrl = "https://example.com/h", Metadata = new Dictionary<string, string> { ["k"] = "v" } }),
            ["listRuns"] = c => c.Runs.ListAsync(new RunListOptions { Status = RunStatus.Failed, TypizerId = "tz_1", BatchId = "batch_1", Source = "api", CreatedAfter = DateTimeOffset.UtcNow.AddDays(-1), CreatedBefore = DateTimeOffset.UtcNow, Limit = 10, Cursor = "run_1" }).GetPageAsync(),
            ["getRun"] = c => c.Runs.GetAsync(Id),
            ["getRunOutput"] = c => c.Runs.GetOutputAsync(Id, OutputFormat.Xlsx),
            ["retryRun"] = c => c.Runs.RetryAsync(Id),
            ["createBatch"] = c => c.Batches.CreateAsync("tz_1", new[] { TypizerFile.FromBytes(Schemas.Pdf, "a.pdf"), TypizerFile.FromBytes(Schemas.Pdf, "b.pdf") }, new BatchCreateOptions { Format = OutputFormat.Csv, Mode = RunMode.Standard, Metadata = new Dictionary<string, string> { ["k"] = "v" } }),
            ["listBatches"] = c => c.Batches.ListAsync(new ListOptions { Limit = 1, Cursor = "batch_0" }).GetPageAsync(),
            ["getBatch"] = c => c.Batches.GetAsync(Id),
            ["exportBatch"] = c => c.Batches.ExportAsync(Id, "zip"),
            ["extract"] = c => c.Runs.CreateAsync(new Uri(Schemas.SampleUrl), new RunCreateOptions { Schema = Schemas.Invoice, Instructions = "i", Wait = TimeSpan.FromSeconds(10), Format = OutputFormat.Json, Mode = RunMode.Precise, WebhookUrl = "https://example.com/h", Metadata = new Dictionary<string, string> { ["k"] = "v" } }),
            ["listWebhookEndpoints"] = c => c.WebhookEndpoints.ListAsync(new ListOptions { Limit = 1, Cursor = "whe_0" }).GetPageAsync(),
            ["createWebhookEndpoint"] = c => c.WebhookEndpoints.CreateAsync("https://example.com/h", new[] { WebhookEventType.RunSucceeded, WebhookEventType.RunFailed }),
            ["getWebhookEndpoint"] = c => c.WebhookEndpoints.GetAsync(Id),
            ["updateWebhookEndpoint"] = c => c.WebhookEndpoints.UpdateAsync(Id, new WebhookEndpointUpdateOptions { Url = "https://example.com/x", Events = new[] { WebhookEventType.BatchCompleted }, Status = WebhookEndpointStatus.Disabled }),
            ["deleteWebhookEndpoint"] = c => c.WebhookEndpoints.DeleteAsync(Id),
            ["testWebhookEndpoint"] = c => c.WebhookEndpoints.TestAsync(Id),
            ["listWebhookDeliveries"] = c => c.WebhookEndpoints.Deliveries.ListAsync(Id, new ListOptions { Limit = 1, Cursor = "whd_0" }).GetPageAsync(),
            ["retryWebhookDelivery"] = c => c.WebhookEndpoints.Deliveries.RetryAsync(Id),
            ["listValidators"] = c => c.Validators.ListAsync(new ListOptions { Limit = 5, Cursor = "val_0" }).GetPageAsync(),
            ["createValidator"] = c => c.Validators.CreateAsync(new ValidatorCreateOptions { Name = "Номер договора", Slug = "contract-no", Description = "d", Spec = Specs.ContractNo, Tests = new[] { ValidatorTest.Pass("ДГ-2026/0153"), ValidatorTest.Fail("ДГ-26/153", "коротко") }, Changelog = "c" }),
            ["getValidator"] = c => c.Validators.GetAsync(Id),
            ["getValidatorBySlug"] = c => c.Validators.GetAsync("my/contract-no@1"),
            ["updateValidator"] = c => c.Validators.UpdateAsync(Id, new ValidatorUpdateOptions { Name = "Номер", Description = null, Spec = Specs.ContractNo, Tests = new[] { new ValidatorTest(42m, ValidatorOutcome.Fail) }, Changelog = "c" }),
            ["deleteValidator"] = c => c.Validators.ArchiveAsync(Id),
            ["testValidator"] = c => c.Validators.TestAsync(new object[] { "ДГ-2026/0153", 42 }, reference: "my/contract-no@1"),
            ["suggestValidator"] = c => c.Validators.SuggestAsync(new ValidatorSuggestOptions { Description = "ДГ, год и номер", Examples = new ValidatorExamples { Pass = new[] { "ДГ-2026/0153" }, Fail = new object[] { "ДГ-26/153", 7 } }, Field = new ValidatorSuggestField { Name = "contract_no", Description = "Номер договора", Type = "string" } }),
        };

        [Fact(DisplayName = "Every public operation has exactly one SDK call, and nothing else (57 today)")]
        public void EveryOperationIsCovered()
        {
            var spec = SpecOperations().Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
            Assert.Equal(spec, Calls.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
            Assert.Equal(57, spec.Length);
        }

        public static IEnumerable<object[]> OperationIds() => Calls.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => new object[] { k });

        [Theory(DisplayName = "The SDK call sends the operation's method, path, query, body kind and Idempotency-Key rule")]
        [MemberData(nameof(OperationIds))]
        public async Task CallMatchesTheOperation(string operationId)
        {
            SpecOperation op = SpecOperations()[operationId];
            var handler = new RecordingHandler(respond: (request, ct) => Task.FromResult(Stub(op)));
            using (var client = new TypizerClient(new TypizerClientOptions { ApiKey = "tz_test_contract", BaseUrl = new Uri("https://api.example.test/"), HttpMessageHandler = handler }))
            {
                object result = Calls[operationId](client);
                await (Task)result;
                if (result is Task<TypizerDownload> download) (await download).Dispose();
            }
            RecordingHandler.Recorded sent = Assert.Single(handler.Requests);
            Assert.Equal(op.Method, sent.Method);
            var template = new Regex("^" + Regex.Replace(Regex.Escape(op.Path), @"\\\{[^}]+}", "[^/]+") + "$");
            Assert.Matches(template, sent.Uri.AbsolutePath);
            Assert.Equal("api.example.test", sent.Uri.Host);

            string[] query = sent.Uri.Query.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(q => Uri.UnescapeDataString(q.Split('=')[0])).ToArray();
            Assert.All(query, q => Assert.Contains(q, op.Query));
            // Every documented query parameter is reachable from the SDK (the calls above set them all).
            Assert.Equal(op.Query.OrderBy(q => q, StringComparer.Ordinal), query.OrderBy(q => q, StringComparer.Ordinal));

            Assert.Equal(op.Method == "POST", sent.Headers.ContainsKey("Idempotency-Key"));
            Assert.Equal("Bearer tz_test_contract", sent.Headers["Authorization"]);
            Assert.Equal(op.Download ? "*/*" : "application/json", sent.Headers["Accept"]);

            if (sent.Body == null || sent.Body.Length == 0)
            {
                Assert.False(op.BodyRequired, operationId + " requires a body");
            }
            else
            {
                Assert.Contains(sent.ContentType!, op.BodyTypes);
                if (sent.ContentType == "application/json")
                {
                    using (JsonDocument body = JsonDocument.Parse(sent.Body))
                    {
                        JsonElement schema = Resolve(Spec.Value.GetProperty("paths").GetProperty(op.Path).GetProperty(op.Method.ToLowerInvariant())
                            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
                        // A body that is one of several shapes (anyOf: a schema fork or a validator fork) allows the union.
                        IEnumerable<JsonElement> shapes = schema.TryGetProperty("anyOf", out JsonElement any)
                            ? any.EnumerateArray().Select(Resolve)
                            : new[] { schema };
                        var allowed = new HashSet<string>(shapes.SelectMany(x => x.GetProperty("properties").EnumerateObject()).Select(p => p.Name));
                        Assert.All(body.RootElement.EnumerateObject(), p => Assert.Contains(p.Name, allowed));
                    }
                }
            }
        }

        /// <summary>A canned 2xx answer the SDK can parse.</summary>
        private static HttpResponseMessage Stub(SpecOperation op)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("X-Request-Id", "req_contract");
            if (op.Download)
            {
                response.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("<x/>"));
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                return response;
            }
            string json = op.Id.StartsWith("list", StringComparison.Ordinal)
                ? "{\"object\":\"list\",\"data\":[],\"has_more\":false,\"next_cursor\":null}"
                : "{}";
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return response;
        }

        // ---- models ----------------------------------------------------------------------------

        /// <summary>SDK model → its schema in openapi.public.json (a JSON pointer).</summary>
        internal static readonly Dictionary<Type, string> Models = new Dictionary<Type, string>
        {
            [typeof(Run)] = "#/components/schemas/Run",
            [typeof(RunPages)] = "#/components/schemas/Run/properties/pages",
            [typeof(RunError)] = "#/components/schemas/Run/properties/error",
            [typeof(RunFile)] = "#/components/schemas/Run/properties/file",
            [typeof(RunCatalog)] = "#/components/schemas/Run/properties/catalog",
            [typeof(UploadedFile)] = "#/components/schemas/File",
            [typeof(DeletedObject)] = "#/components/schemas/DeletedFile",
            [typeof(SavedTypizer)] = "#/components/schemas/Typizer",
            [typeof(TypizerCatalogSource)] = "#/components/schemas/Typizer/properties/catalog_source",
            [typeof(TypizerCatalogSourceUpdate)] = "#/components/schemas/Typizer/properties/catalog_source/properties/update",
            [typeof(TypizerDraft)] = "#/components/schemas/TypizerDraft",
            [typeof(TypizerVersion)] = "#/components/schemas/TypizerVersion",
            [typeof(Batch)] = "#/components/schemas/Batch",
            [typeof(WebhookEndpoint)] = "#/components/schemas/WebhookEndpoint",
            [typeof(WebhookDelivery)] = "#/components/schemas/WebhookDelivery",
            [typeof(Balance)] = "#/components/schemas/Balance",
            [typeof(BalanceLot)] = "#/components/schemas/BalanceLot",
            [typeof(Usage)] = "#/components/schemas/Usage",
            [typeof(UsageRow)] = "#/components/schemas/UsageRow",
            [typeof(Me)] = "#/components/schemas/Me",
            [typeof(MeOrganization)] = "#/components/schemas/Me/properties/organization",
            [typeof(MeUser)] = "#/components/schemas/Me/properties/user",
            [typeof(MeApiKey)] = "#/components/schemas/Me/properties/api_key",
            [typeof(MeMembership)] = "#/components/schemas/Me/properties/organizations/items",
            [typeof(MeFeatures)] = "#/components/schemas/Me/properties/features",
            [typeof(Organization)] = "#/components/schemas/Organization",
            [typeof(OrganizationSettings)] = "#/components/schemas/Organization/properties/settings",
            [typeof(Status)] = "#/components/schemas/Status",
            [typeof(MaintenanceEvent)] = "#/components/schemas/MaintenanceEvent",
            [typeof(CatalogEntryBrief)] = "#/components/schemas/CatalogEntryBrief",
            [typeof(CatalogEntry)] = "#/components/schemas/CatalogEntry",
            [typeof(CatalogEntryStats)] = "#/components/schemas/CatalogEntry/properties/stats",
            [typeof(CatalogViewer)] = "#/components/schemas/CatalogEntry/properties/viewer",
            [typeof(CatalogViewerTypizer)] = "#/components/schemas/CatalogEntry/properties/viewer/properties/typizers/items",
            [typeof(CatalogMajor)] = "#/components/schemas/CatalogEntry/properties/majors/items",
            [typeof(CatalogLineage)] = "#/components/schemas/CatalogEntry/properties/lineage",
            [typeof(CatalogOwner)] = "#/components/schemas/CatalogEntry/properties/owner",
            [typeof(CatalogPublisherBrief)] = "#/components/schemas/CatalogPublisherBrief",
            [typeof(CatalogPublisher)] = "#/components/schemas/CatalogPublisher",
            [typeof(CatalogPublisherStats)] = "#/components/schemas/CatalogPublisher/properties/stats",
            [typeof(CatalogVersionBrief)] = "#/components/schemas/CatalogVersionBrief",
            [typeof(CatalogVersion)] = "#/components/schemas/CatalogVersion",
            [typeof(SchemaChange)] = "#/components/schemas/SchemaChange",
            [typeof(CatalogFavorite)] = "#/components/schemas/CatalogFavorite",
            [typeof(CatalogReport)] = "#/components/schemas/CatalogReport",
            [typeof(CatalogUpdate)] = "#/components/schemas/CatalogUpdate",
            [typeof(CatalogChangelogEntry)] = "#/components/schemas/CatalogUpdate/properties/changelog/items",
            [typeof(CatalogUpdateChange)] = "#/components/schemas/CatalogUpdate/properties/changes/items",
            [typeof(Problem)] = "#/components/schemas/Problem",
            [typeof(FieldResult)] = "#/components/schemas/FieldResult",
            [typeof(FieldCheck)] = "#/components/schemas/FieldCheck",
            [typeof(FieldSource)] = "#/components/schemas/FieldSource",
            [typeof(RunCheckUsed)] = "#/components/schemas/RunCheckUsed",
            [typeof(TypizerCheck)] = "#/components/schemas/TypizerCheck",
            [typeof(Validator)] = "#/components/schemas/Validator",
            [typeof(ValidatorCatalog)] = "#/components/schemas/Validator/properties/catalog",
            [typeof(CatalogValidatorBrief)] = "#/components/schemas/CatalogValidatorBrief",
            [typeof(CatalogValidatorVersion)] = "#/components/schemas/CatalogValidatorVersion",
            [typeof(CatalogViewerValidatorUse)] = "#/components/schemas/CatalogEntry/properties/viewer/properties/used_by/items",
            [typeof(CatalogViewerValidatorFork)] = "#/components/schemas/CatalogEntry/properties/viewer/properties/forks/items",
            [typeof(ValidatorForkedFrom)] = "#/components/schemas/Validator/properties/forked_from",
            [typeof(ValidatorDetail)] = "#/components/schemas/ValidatorDetail",
            [typeof(ValidatorVersion)] = "#/components/schemas/ValidatorVersion",
            [typeof(ValidatorTest)] = "#/components/schemas/ValidatorTest",
            [typeof(ValidatorUsedBy)] = "#/components/schemas/ValidatorUsedBy",
            [typeof(ValidatorUpdateResult)] = "#/components/schemas/ValidatorUpdateResult",
            [typeof(ValidatorPropagation)] = "#/components/schemas/ValidatorUpdateResult/properties/propagated_to/items",
            [typeof(ValidatorTestResult)] = "#/components/schemas/ValidatorTestResult",
            [typeof(ValidatorTestValueResult)] = "#/components/schemas/ValidatorTestResult/properties/results/items",
            [typeof(ValidatorSuggestion)] = "#/components/schemas/ValidatorSuggestion",
            [typeof(ValidatorSuggestionTest)] = "#/components/schemas/ValidatorSuggestion/properties/tests/items",
            [typeof(ValidatorCandidate)] = "#/components/schemas/ValidatorSuggestion/properties/candidates_from_catalog/items",
            [typeof(ValidatorCandidateExamples)] = "#/components/schemas/ValidatorSuggestion/properties/candidates_from_catalog/items/properties/examples",
        };

        /// <summary>Problem fields the SDK keeps in ExtensionData (ApiException.Extra) on purpose.</summary>
        private static readonly HashSet<string> ProblemExtras = new HashSet<string> { "batch_id", "created", "expected", "findings", "retry_after" };

        public static IEnumerable<object[]> ModelTypes() => Models.Keys.Select(t => new object[] { t.Name });

        [Theory(DisplayName = "Each model carries exactly the fields of its schema (unknown ones go to ExtensionData)")]
        [MemberData(nameof(ModelTypes))]
        public void ModelMatchesSchema(string typeName)
        {
            Type type = Models.Keys.Single(t => t.Name == typeName);
            JsonElement schema = Pointer(Models[type]);
            var expected = new HashSet<string>(SchemaProperties(schema));
            var actual = new HashSet<string>(JsonNames(type));
            if (type == typeof(Problem)) expected.ExceptWith(ProblemExtras);
            Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal), actual.OrderBy(n => n, StringComparer.Ordinal));
            Assert.NotNull(type.GetProperty("ExtensionData"));
        }

        [Fact(DisplayName = "Every object schema of the spec has an SDK model")]
        public void EverySchemaHasAModel()
        {
            var covered = new HashSet<string>(Models.Values.Where(v => v.Count(c => c == '/') == 3).Select(v => v.Split('/').Last()));
            // Request bodies are built by the SDK's options classes; lists and the open string enums need no model.
            var notModels = new HashSet<string>
            {
                "CreateTypizerRequest", "UpdateTypizerRequest", "GenerateTypizerRequest", "CatalogInstallRequest", "CatalogForkValidatorRequest", "CatalogReportRequest",
                "CatalogUpdateRequest", "CreateRunRequest", "CreateBatchRequest", "ExtractRequest", "CreateWebhookEndpointRequest",
                "UpdateWebhookEndpointRequest", "CatalogList", "Scope", "WebhookEventType", "DeletedTypizer", "DeletedWebhookEndpoint",
                "CreateValidatorRequest", "UpdateValidatorRequest", "TestValidatorRequest", "SuggestValidatorRequest", "DeletedValidator",
            };
            foreach (JsonProperty schema in Spec.Value.GetProperty("components").GetProperty("schemas").EnumerateObject())
            {
                if (notModels.Contains(schema.Name)) continue;
                Assert.True(covered.Contains(schema.Name), schema.Name + " has no SDK model");
            }
        }

        internal static JsonElement Pointer(string pointer)
        {
            JsonElement node = Spec.Value;
            foreach (string segment in pointer.TrimStart('#', '/').Split('/'))
            {
                node = Resolve(node);
                node = node.GetProperty(segment);
            }
            return Resolve(node);
        }

        private static IEnumerable<string> SchemaProperties(JsonElement schema)
        {
            schema = Resolve(schema);
            if (schema.TryGetProperty("allOf", out JsonElement all))
            {
                foreach (JsonElement part in all.EnumerateArray())
                    foreach (string name in SchemaProperties(part)) yield return name;
            }
            if (schema.TryGetProperty("properties", out JsonElement props))
            {
                foreach (JsonProperty p in props.EnumerateObject()) yield return p.Name;
            }
        }

        private static IEnumerable<string> JsonNames(Type type)
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                if (property.GetCustomAttribute<JsonExtensionDataAttribute>() != null) continue;
                if (property.SetMethod == null || !property.SetMethod.IsPublic) continue;
                JsonPropertyNameAttribute? name = property.GetCustomAttribute<JsonPropertyNameAttribute>();
                yield return name?.Name ?? JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);
            }
        }
    }
}
