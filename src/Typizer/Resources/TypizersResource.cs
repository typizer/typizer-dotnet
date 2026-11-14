using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Typizer.Internal;

namespace Typizer
{
    /// <summary>Типизаторы — сохранённые схемы: <c>client.Typizers</c>.</summary>
    public sealed class TypizersResource
    {
        private readonly HttpPipeline _pipeline;

        internal TypizersResource(HttpPipeline pipeline)
        {
            _pipeline = pipeline;
            Versions = new TypizerVersionsResource(pipeline);
            CatalogUpdate = new TypizerCatalogUpdateResource(pipeline);
        }

        /// <summary>Версии типизатора.</summary>
        public TypizerVersionsResource Versions { get; }

        /// <summary>Обновления схемы каталога, из которой установлен типизатор.</summary>
        public TypizerCatalogUpdateResource CatalogUpdate { get; }

        private static string PathOf(string id) => "/v1/typizers/" + Url.Segment(id, nameof(id));

        /// <summary>Сохраняет схему как типизатор (<c>POST /v1/typizers</c>).</summary>
        public Task<SavedTypizer> CreateAsync(string name, object schema, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            CreateAsync(new TypizerCreateOptions { Name = name, Schema = schema }, requestOptions, cancellationToken);

        /// <inheritdoc cref="CreateAsync(string, object, RequestOptions?, CancellationToken)"/>
        public async Task<SavedTypizer> CreateAsync(TypizerCreateOptions options, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await CreateWithResponseAsync(options, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="CreateAsync(string, object, RequestOptions?, CancellationToken)"/>
        public Task<TypizerResponse<SavedTypizer>> CreateWithResponseAsync(TypizerCreateOptions options, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            Require.NotNull(options, nameof(options));
            string name = Require.NotEmpty(options.Name, nameof(options.Name));
            JsonElement schema = Json.ToSchema(Require.NotNull(options.Schema, nameof(options.Schema)), nameof(options.Schema));
            byte[] body = Json.WriteObject(w =>
            {
                w.WriteString("name", name);
                Json.WriteStringIf(w, "description", options.Description);
                w.WritePropertyName("schema");
                schema.WriteTo(w);
                Json.WriteStringIf(w, "instructions", options.Instructions);
                Json.WriteStringIf(w, "default_format", options.DefaultFormat?.Value);
                Json.WriteStringIf(w, "mode", options.Mode?.Value);
            });
            return _pipeline.SendJsonAsync<SavedTypizer>(new ApiRequest("createTypizer", Http.Post, "/v1/typizers") { Body = Http.JsonBody(body) },
                requestOptions, cancellationToken);
        }

        /// <summary>Типизатор по id (<c>GET /v1/typizers/{id}</c>).</summary>
        public async Task<SavedTypizer> GetAsync(string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await GetWithResponseAsync(id, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetAsync"/>
        public Task<TypizerResponse<SavedTypizer>> GetWithResponseAsync(string id, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<SavedTypizer>(new ApiRequest("getTypizer", Http.Get, PathOf(id)), requestOptions, cancellationToken);

        /// <summary>Меняет типизатор (<c>PATCH /v1/typizers/{id}</c>); новая схема — новая версия.</summary>
        public async Task<SavedTypizer> UpdateAsync(string id, TypizerUpdateOptions options, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await UpdateWithResponseAsync(id, options, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="UpdateAsync"/>
        public Task<TypizerResponse<SavedTypizer>> UpdateWithResponseAsync(string id, TypizerUpdateOptions options,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        {
            Require.NotNull(options, nameof(options));
            JsonElement? schema = options.Schema != null ? Json.ToSchema(options.Schema, nameof(options.Schema)) : (JsonElement?)null;
            byte[] body = Json.WriteObject(w =>
            {
                Json.WriteStringIf(w, "name", options.Name);
                if (options.DescriptionSet)
                {
                    if (options.Description == null) w.WriteNull("description");
                    else w.WriteString("description", options.Description);
                }
                if (schema.HasValue)
                {
                    w.WritePropertyName("schema");
                    schema.Value.WriteTo(w);
                }
                Json.WriteStringIf(w, "instructions", options.Instructions);
                Json.WriteStringIf(w, "default_format", options.DefaultFormat?.Value);
                Json.WriteStringIf(w, "mode", options.Mode?.Value);
            });
            return _pipeline.SendJsonAsync<SavedTypizer>(new ApiRequest("updateTypizer", Http.Patch, PathOf(id)) { Body = Http.JsonBody(body) },
                requestOptions, cancellationToken);
        }

        /// <summary>Удаляет типизатор (<c>DELETE /v1/typizers/{id}</c>).</summary>
        public async Task<DeletedObject> DeleteAsync(string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await DeleteWithResponseAsync(id, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="DeleteAsync"/>
        public Task<TypizerResponse<DeletedObject>> DeleteWithResponseAsync(string id, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<DeletedObject>(new ApiRequest("deleteTypizer", Http.Delete, PathOf(id)), requestOptions, cancellationToken);

        /// <summary>Типизаторы организации, новые первыми (<c>GET /v1/typizers</c>).</summary>
        public TypizerPageable<SavedTypizer> ListAsync(ListOptions? options = null, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            Lists.Create<SavedTypizer>(_pipeline, () => new ApiRequest("listTypizers", Http.Get, "/v1/typizers"),
                options, "starting_after", requestOptions, cancellationToken);

        /// <summary>Черновик схемы по описанию своими словами (<c>POST /v1/typizers/generate</c>); не сохраняется.</summary>
        public Task<TypizerDraft> GenerateAsync(string description, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            GenerateAsync(new TypizerGenerateOptions { Description = description }, requestOptions, cancellationToken);

        /// <summary>Черновик схемы по описанию и/или образцу документа (<c>POST /v1/typizers/generate</c>).</summary>
        public async Task<TypizerDraft> GenerateAsync(TypizerGenerateOptions options, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await GenerateWithResponseAsync(options, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GenerateAsync(TypizerGenerateOptions, RequestOptions?, CancellationToken)"/>
        public Task<TypizerResponse<TypizerDraft>> GenerateWithResponseAsync(TypizerGenerateOptions options, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            Require.NotNull(options, nameof(options));
            if (options.Description == null && options.FileId == null)
                throw new ArgumentException("Нужно описание (Description) и/или образец (FileId).", nameof(options));
            byte[] body = Json.WriteObject(w =>
            {
                Json.WriteStringIf(w, "description", options.Description);
                Json.WriteStringIf(w, "file_id", options.FileId);
            });
            return _pipeline.SendJsonAsync<TypizerDraft>(new ApiRequest("generateTypizer", Http.Post, "/v1/typizers/generate") { Body = Http.JsonBody(body) },
                requestOptions, cancellationToken);
        }

        /// <summary>XSD-схема XML-результата типизатора — для загрузки в 1С (<c>GET /v1/typizers/{id}/xsd</c>).</summary>
        public Task<TypizerDownload> GetXsdAsync(string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            _pipeline.SendDownloadAsync(new ApiRequest("getTypizerXsd", Http.Get, PathOf(id) + "/xsd"), requestOptions, cancellationToken);
    }

    /// <summary>Версии типизатора: <c>client.Typizers.Versions</c>.</summary>
    public sealed class TypizerVersionsResource
    {
        private readonly HttpPipeline _pipeline;

        internal TypizerVersionsResource(HttpPipeline pipeline) => _pipeline = pipeline;

        /// <summary>Версии, новые первыми (<c>GET /v1/typizers/{id}/versions</c>).</summary>
        public TypizerPageable<TypizerVersion> ListAsync(string typizerId, ListOptions? options = null, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            string path = "/v1/typizers/" + Url.Segment(typizerId, nameof(typizerId)) + "/versions";
            return Lists.Create<TypizerVersion>(_pipeline, () => new ApiRequest("listTypizerVersions", Http.Get, path),
                options, "starting_after", requestOptions, cancellationToken);
        }

        /// <summary>Одна версия (<c>GET /v1/typizers/{id}/versions/{n}</c>).</summary>
        public async Task<TypizerVersion> GetAsync(string typizerId, int version, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await GetWithResponseAsync(typizerId, version, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetAsync"/>
        public Task<TypizerResponse<TypizerVersion>> GetWithResponseAsync(string typizerId, int version, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version), "Версии нумеруются с 1");
            return _pipeline.SendJsonAsync<TypizerVersion>(new ApiRequest("getTypizerVersion", Http.Get,
                "/v1/typizers/" + Url.Segment(typizerId, nameof(typizerId)) + "/versions/" + Url.Int(version)), requestOptions, cancellationToken);
        }
    }

    /// <summary>Обновление схемы каталога в типизаторе: <c>client.Typizers.CatalogUpdate</c>.</summary>
    public sealed class TypizerCatalogUpdateResource
    {
        private readonly HttpPipeline _pipeline;

        internal TypizerCatalogUpdateResource(HttpPipeline pipeline) => _pipeline = pipeline;

        /// <summary>Что изменится при переходе на версию каталога (<c>GET /v1/typizers/{id}/catalog_update</c>).</summary>
        public async Task<CatalogUpdate> GetAsync(string typizerId, string? version = null, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await GetWithResponseAsync(typizerId, version, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetAsync"/>
        public Task<TypizerResponse<CatalogUpdate>> GetWithResponseAsync(string typizerId, string? version = null,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<CatalogUpdate>(new ApiRequest("getTypizerCatalogUpdate", Http.Get,
                    "/v1/typizers/" + Url.Segment(typizerId, nameof(typizerId)) + "/catalog_update").With("version", version),
                requestOptions, cancellationToken);

        /// <summary>
        /// Переводит типизатор на версию каталога (<c>POST /v1/typizers/{id}/catalog_update</c>); для копии
        /// (<c>forked</c>) — только изменения по путям <paramref name="paths"/>.
        /// </summary>
        public async Task<SavedTypizer> ApplyAsync(string typizerId, string version, IEnumerable<string>? paths = null,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await ApplyWithResponseAsync(typizerId, version, paths, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="ApplyAsync"/>
        public Task<TypizerResponse<SavedTypizer>> ApplyWithResponseAsync(string typizerId, string version, IEnumerable<string>? paths = null,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        {
            string v = Require.NotEmpty(version, nameof(version));
            byte[] body = Json.WriteObject(w =>
            {
                w.WriteString("version", v);
                if (paths != null)
                {
                    w.WritePropertyName("paths");
                    w.WriteStartArray();
                    foreach (string path in paths) w.WriteStringValue(path);
                    w.WriteEndArray();
                }
            });
            return _pipeline.SendJsonAsync<SavedTypizer>(new ApiRequest("applyTypizerCatalogUpdate", Http.Post,
                    "/v1/typizers/" + Url.Segment(typizerId, nameof(typizerId)) + "/catalog_update") { Body = Http.JsonBody(body) },
                requestOptions, cancellationToken);
        }
    }
}
