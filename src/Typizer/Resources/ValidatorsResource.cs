using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Typizer.Internal;

namespace Typizer
{
    /// <summary>
    /// Свои проверки полей организации (<c>my/…</c>): <c>client.Validators</c>. На проверку ссылаются из
    /// схемы — <c>"x-typizer-check": "my/contract-no@1"</c>; встроенную проверку (объект прямо в схеме)
    /// создавать не нужно: SDK передаёт схему как есть.
    /// </summary>
    public sealed class ValidatorsResource
    {
        private const int MaxTestValues = 100;
        private readonly HttpPipeline _pipeline;

        internal ValidatorsResource(HttpPipeline pipeline) => _pipeline = pipeline;

        private static string PathOf(string id) => "/v1/validators/" + Url.Segment(id, nameof(id));

        /// <summary>
        /// <c>my/contract-no</c> и <c>my/contract-no@1</c> → <c>GET /v1/validators/my/contract-no</c> (версия
        /// отбрасывается: ответ несёт все версии), иначе <c>val_…</c> → <c>GET /v1/validators/{id}</c>.
        /// </summary>
        private static ApiRequest GetRequest(string idOrRef)
        {
            string value = Require.NotEmpty(idOrRef, nameof(idOrRef));
            if (value.StartsWith("my/", StringComparison.Ordinal))
            {
                string slug = value.Substring(3);
                int at = slug.IndexOf('@');
                if (at >= 0) slug = slug.Substring(0, at);
                return new ApiRequest("getValidatorBySlug", Http.Get, "/v1/validators/my/" + Url.Segment(slug, nameof(idOrRef)));
            }
            return new ApiRequest("getValidator", Http.Get, PathOf(value));
        }

        /// <summary>Проверки организации (<c>GET /v1/validators</c>); <c>await foreach</c> перебирает все страницы.</summary>
        public TypizerPageable<Validator> ListAsync(ListOptions? options = null, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            Lists.Create<Validator>(_pipeline, () => new ApiRequest("listValidators", Http.Get, "/v1/validators"),
                options, "starting_after", requestOptions, cancellationToken);

        /// <summary>
        /// Проверка со всеми версиями и типизаторами, которые на неё ссылаются: по id (<c>val_…</c>) или
        /// ссылке (<c>my/contract-no</c>, <c>my/contract-no@1</c>).
        /// </summary>
        public async Task<ValidatorDetail> GetAsync(string idOrRef, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await GetWithResponseAsync(idOrRef, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetAsync"/>
        public Task<TypizerResponse<ValidatorDetail>> GetWithResponseAsync(string idOrRef, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<ValidatorDetail>(GetRequest(idOrRef), requestOptions, cancellationToken);

        /// <summary>Версии проверки, новые первыми (из <see cref="GetAsync"/>): по id или ссылке <c>my/…</c>.</summary>
        public async Task<IReadOnlyList<ValidatorVersion>> VersionsAsync(string idOrRef, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await GetAsync(idOrRef, requestOptions, cancellationToken).ConfigureAwait(false)).Versions;

        /// <summary>
        /// Создаёт проверку версии <c>1.0.0</c> (<c>POST /v1/validators</c>). Красный тест — <see cref="TypizerBadRequestException"/>
        /// с <c>Param = "tests/&lt;i&gt;"</c>; занятый slug — <see cref="TypizerConflictException"/>.
        /// </summary>
        public Task<Validator> CreateAsync(string name, object spec, IEnumerable<ValidatorTest>? tests = null,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            CreateAsync(new ValidatorCreateOptions { Name = name, Spec = spec, Tests = tests }, requestOptions, cancellationToken);

        /// <inheritdoc cref="CreateAsync(string, object, IEnumerable{ValidatorTest}?, RequestOptions?, CancellationToken)"/>
        public async Task<Validator> CreateAsync(ValidatorCreateOptions options, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await CreateWithResponseAsync(options, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="CreateAsync(string, object, IEnumerable{ValidatorTest}?, RequestOptions?, CancellationToken)"/>
        public Task<TypizerResponse<Validator>> CreateWithResponseAsync(ValidatorCreateOptions options, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            Require.NotNull(options, nameof(options));
            string name = Require.NotEmpty(options.Name, nameof(options.Name));
            JsonElement spec = Json.ToSpec(Require.NotNull(options.Spec, nameof(options.Spec)), nameof(options.Spec));
            List<ValidatorTest>? tests = options.Tests?.ToList();
            byte[] body = Json.WriteObject(w =>
            {
                w.WriteString("name", name);
                Json.WriteStringIf(w, "slug", options.Slug);
                Json.WriteStringIf(w, "description", options.Description);
                w.WritePropertyName("spec");
                spec.WriteTo(w);
                if (tests != null) WriteTests(w, tests, nameof(options.Tests));
                Json.WriteStringIf(w, "changelog", options.Changelog);
            });
            return _pipeline.SendJsonAsync<Validator>(new ApiRequest("createValidator", Http.Post, "/v1/validators") { Body = Http.JsonBody(body) },
                requestOptions, cancellationToken);
        }

        /// <summary>
        /// Меняет проверку (<c>PATCH /v1/validators/{id}</c>). Новые правила или тесты — новая версия: что
        /// с ней стало, скажет <see cref="ValidatorUpdateResult.Bump"/> (<c>major</c>/<c>minor</c>/<c>patch</c>/<c>none</c>)
        /// и <see cref="ValidatorUpdateResult.Reasons"/>.
        /// </summary>
        public async Task<ValidatorUpdateResult> UpdateAsync(string id, ValidatorUpdateOptions options, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await UpdateWithResponseAsync(id, options, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="UpdateAsync"/>
        public Task<TypizerResponse<ValidatorUpdateResult>> UpdateWithResponseAsync(string id, ValidatorUpdateOptions options,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        {
            Require.NotNull(options, nameof(options));
            string path = PathOf(id);
            JsonElement? spec = options.Spec != null ? Json.ToSpec(options.Spec, nameof(options.Spec)) : (JsonElement?)null;
            List<ValidatorTest>? tests = options.Tests?.ToList();
            byte[] body = Json.WriteObject(w =>
            {
                Json.WriteStringIf(w, "name", options.Name);
                if (options.DescriptionSet)
                {
                    if (options.Description == null) w.WriteNull("description");
                    else w.WriteString("description", options.Description);
                }
                if (spec.HasValue)
                {
                    w.WritePropertyName("spec");
                    spec.Value.WriteTo(w);
                }
                if (tests != null) WriteTests(w, tests, nameof(options.Tests));
                Json.WriteStringIf(w, "changelog", options.Changelog);
            });
            return _pipeline.SendJsonAsync<ValidatorUpdateResult>(new ApiRequest("updateValidator", Http.Patch, path) { Body = Http.JsonBody(body) },
                requestOptions, cancellationToken);
        }

        /// <summary>
        /// Убирает проверку в архив (<c>DELETE /v1/validators/{id}</c>): её версии остаются у прогонов и прежних
        /// версий типизаторов. Пока на неё ссылается типизатор — <see cref="TypizerConflictException"/> с
        /// <c>Param = "used_by"</c>.
        /// </summary>
        public async Task<DeletedObject> ArchiveAsync(string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await ArchiveWithResponseAsync(id, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="ArchiveAsync"/>
        public Task<TypizerResponse<DeletedObject>> ArchiveWithResponseAsync(string id, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<DeletedObject>(new ApiRequest("deleteValidator", Http.Delete, PathOf(id)), requestOptions, cancellationToken);

        /// <summary>
        /// Прогоняет значения через проверку (<c>POST /v1/validators/test</c>), ничего не сохраняя: ровно одно из
        /// <paramref name="reference"/> (<c>my/contract-no@1</c>, <c>inn@1</c>) и <paramref name="spec"/> (объект
        /// проверки, как <see cref="ValidatorCreateOptions.Spec"/>) — иначе <see cref="ArgumentException"/> до
        /// запроса. <paramref name="values"/> — от 1 до 100 строк или чисел (<c>new object[] { "ДГ-2026/0153", 42 }</c>).
        /// </summary>
        public async Task<ValidatorTestResult> TestAsync(IEnumerable<object> values, string? reference = null, object? spec = null,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await TestWithResponseAsync(values, reference, spec, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="TestAsync"/>
        public Task<TypizerResponse<ValidatorTestResult>> TestWithResponseAsync(IEnumerable<object> values, string? reference = null,
            object? spec = null, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        {
            List<object> list = Require.NotNull(values, nameof(values)).ToList();
            if (list.Count < 1 || list.Count > MaxTestValues)
                throw new ArgumentException("Нужно от 1 до " + MaxTestValues + " значений, а не " + list.Count + ".", nameof(values));
            if ((reference == null) == (spec == null))
                throw new ArgumentException("Задайте ровно одно: reference (ссылку на проверку, «my/contract-no@1») или spec (объект проверки).",
                    reference == null ? nameof(reference) : nameof(spec));
            string? target = reference != null ? Require.NotEmpty(reference, nameof(reference)) : null;
            JsonElement? specElement = spec != null ? Json.ToSpec(spec, nameof(spec)) : (JsonElement?)null;
            byte[] body = Json.WriteObject(w =>
            {
                Json.WriteStringIf(w, "ref", target);
                if (specElement.HasValue)
                {
                    w.WritePropertyName("spec");
                    specElement.Value.WriteTo(w);
                }
                w.WritePropertyName("values");
                w.WriteStartArray();
                foreach (object value in list) Json.WriteCheckValue(w, value, nameof(values));
                w.WriteEndArray();
            });
            return _pipeline.SendJsonAsync<ValidatorTestResult>(new ApiRequest("testValidator", Http.Post, "/v1/validators/test") { Body = Http.JsonBody(body) },
                requestOptions, cancellationToken);
        }

        /// <summary>
        /// Предлагает проверку по описанию словами (<c>POST /v1/validators/suggest</c>); не сохраняется. Расходует
        /// агентский лимит организации.
        /// </summary>
        public Task<ValidatorSuggestion> SuggestAsync(string description, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            SuggestAsync(new ValidatorSuggestOptions { Description = description }, requestOptions, cancellationToken);

        /// <summary>
        /// Предлагает проверку по описанию, примерам и полю схемы (<c>POST /v1/validators/suggest</c>); не
        /// сохраняется. Нет ни описания, ни примеров — <see cref="TypizerBadRequestException"/> от сервера.
        /// </summary>
        public async Task<ValidatorSuggestion> SuggestAsync(ValidatorSuggestOptions options, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await SuggestWithResponseAsync(options, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="SuggestAsync(ValidatorSuggestOptions, RequestOptions?, CancellationToken)"/>
        public Task<TypizerResponse<ValidatorSuggestion>> SuggestWithResponseAsync(ValidatorSuggestOptions options,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        {
            Require.NotNull(options, nameof(options));
            List<object>? pass = options.Examples?.Pass?.ToList();
            List<object>? fail = options.Examples?.Fail?.ToList();
            byte[] body = Json.WriteObject(w =>
            {
                Json.WriteStringIf(w, "description", options.Description);
                if (options.Examples != null)
                {
                    w.WritePropertyName("examples");
                    w.WriteStartObject();
                    WriteValues(w, "pass", pass);
                    WriteValues(w, "fail", fail);
                    w.WriteEndObject();
                }
                if (options.Field != null)
                {
                    w.WritePropertyName("field");
                    w.WriteStartObject();
                    Json.WriteStringIf(w, "name", options.Field.Name);
                    Json.WriteStringIf(w, "description", options.Field.Description);
                    Json.WriteStringIf(w, "type", options.Field.Type);
                    w.WriteEndObject();
                }
            });
            return _pipeline.SendJsonAsync<ValidatorSuggestion>(new ApiRequest("suggestValidator", Http.Post, "/v1/validators/suggest") { Body = Http.JsonBody(body) },
                requestOptions, cancellationToken);
        }

        private static void WriteValues(Utf8JsonWriter w, string name, List<object>? values)
        {
            if (values == null) return;
            w.WritePropertyName(name);
            w.WriteStartArray();
            foreach (object value in values) Json.WriteCheckValue(w, value, "options.Examples." + (name == "pass" ? "Pass" : "Fail"));
            w.WriteEndArray();
        }

        private static void WriteTests(Utf8JsonWriter w, IEnumerable<ValidatorTest> tests, string paramName)
        {
            w.WritePropertyName("tests");
            w.WriteStartArray();
            foreach (ValidatorTest test in tests)
            {
                if (test == null) throw new ArgumentException("Тест не может быть null", paramName);
                w.WriteStartObject();
                w.WritePropertyName("value");
                Json.WriteCheckValue(w, test.Value, paramName);
                w.WriteString("expect", test.Expect.Value);
                Json.WriteStringIf(w, "note", test.Note);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
    }
}
