using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Typizer.Internal;

namespace Typizer
{
    /// <summary>
    /// Каталог готовых схем: <c>client.Catalog</c>. Ссылка на схему — slug официальной (<c>schet-faktura</c>),
    /// <c>издатель/slug</c> схемы сообщества (<c>acme/akt-sverki</c>) или <c>cat_…</c>; версия — через
    /// <c>@</c>: <c>schet-faktura@1</c>, <c>acme/akt-sverki@1.2.0</c>.
    /// </summary>
    public sealed class CatalogResource
    {
        private readonly HttpPipeline _pipeline;

        internal CatalogResource(HttpPipeline pipeline)
        {
            _pipeline = pipeline;
            Publishers = new CatalogPublishersResource(pipeline);
        }

        /// <summary>Издатели каталога.</summary>
        public CatalogPublishersResource Publishers { get; }

        /// <summary>Схемы каталога (<c>GET /v1/catalog</c>).</summary>
        public TypizerPageable<CatalogEntryBrief> ListAsync(CatalogListOptions? options = null, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            Lists.Create<CatalogEntryBrief>(_pipeline, () => new ApiRequest("listCatalog", Http.Get, "/v1/catalog")
                    .With("q", options?.Query)
                    .With("category", options?.Category?.Value)
                    .With("kind", options?.Kind?.Value)
                    .With("publisher", options?.Publisher)
                    .With("favorites", options?.Favorites == true ? "true" : null)
                    .With("sort", options?.Sort),
                options, "cursor", requestOptions, cancellationToken);

        /// <summary>
        /// Схема каталога со схемой нужной версии (<c>GET /v1/catalog/{entry}</c> или
        /// <c>/v1/catalog/{publisher}/{slug}</c>). Версия — из <c>@</c> в ссылке или <paramref name="version"/>.
        /// </summary>
        public async Task<CatalogEntry> GetAsync(string reference, string? version = null, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await GetWithResponseAsync(reference, version, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetAsync"/>
        public Task<TypizerResponse<CatalogEntry>> GetWithResponseAsync(string reference, string? version = null,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        {
            CatalogRef r = CatalogRef.Parse(reference, version, nameof(reference));
            ApiRequest request = r.Publisher != null
                ? new ApiRequest("getCommunityCatalogEntry", Http.Get, "/v1/catalog/" + Url.Segment(r.Publisher, nameof(reference)) + "/" + Url.Segment(r.Slug, nameof(reference)))
                : new ApiRequest("getCatalogEntry", Http.Get, "/v1/catalog/" + Url.Segment(r.Slug, nameof(reference)));
            return _pipeline.SendJsonAsync<CatalogEntry>(request.With("version", r.Version), requestOptions, cancellationToken);
        }

        /// <summary>Все версии схемы, новые первыми (<c>GET /v1/catalog/{entry}/versions</c>, без страниц).</summary>
        public async Task<IReadOnlyList<CatalogVersionBrief>> ListVersionsAsync(string reference, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await ListVersionsWithResponseAsync(reference, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="ListVersionsAsync"/>
        public async Task<TypizerResponse<IReadOnlyList<CatalogVersionBrief>>> ListVersionsWithResponseAsync(string reference,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        {
            string entry = await EntryPathAsync(reference, requestOptions, cancellationToken).ConfigureAwait(false);
            TypizerResponse<ListEnvelope<CatalogVersionBrief>> response = await _pipeline.SendJsonAsync<ListEnvelope<CatalogVersionBrief>>(
                new ApiRequest("listCatalogVersions", Http.Get, entry + "/versions"), requestOptions, cancellationToken).ConfigureAwait(false);
            return new TypizerResponse<IReadOnlyList<CatalogVersionBrief>>(response.Value.Data, response.StatusCode, response.Headers);
        }

        /// <summary>
        /// Устанавливает схему как типизатор, который следует за обновлениями каталога
        /// (<c>POST /v1/catalog/{entry}/install</c>). Повторная установка той же схемы вернёт тот же типизатор.
        /// </summary>
        public async Task<SavedTypizer> InstallAsync(string reference, CatalogInstallOptions? options = null,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await InstallWithResponseAsync(reference, options, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="InstallAsync"/>
        public Task<TypizerResponse<SavedTypizer>> InstallWithResponseAsync(string reference, CatalogInstallOptions? options = null,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            InstallOrForkAsync("installCatalogEntry", "/install", reference, options, requestOptions, cancellationToken);

        /// <summary>Копирует схему в свой типизатор, который можно менять (<c>POST /v1/catalog/{entry}/fork</c>).</summary>
        public async Task<SavedTypizer> ForkAsync(string reference, CatalogInstallOptions? options = null,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await ForkWithResponseAsync(reference, options, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="ForkAsync"/>
        public Task<TypizerResponse<SavedTypizer>> ForkWithResponseAsync(string reference, CatalogInstallOptions? options = null,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            InstallOrForkAsync("forkCatalogEntry", "/fork", reference, options, requestOptions, cancellationToken);

        private async Task<TypizerResponse<SavedTypizer>> InstallOrForkAsync(string operationId, string suffix, string reference,
            CatalogInstallOptions? options, RequestOptions? requestOptions, CancellationToken cancellationToken)
        {
            CatalogRef r = CatalogRef.Parse(reference, options?.Version, nameof(reference));
            string entry = await EntryPathAsync(reference, requestOptions, cancellationToken).ConfigureAwait(false);
            byte[] body = Json.WriteObject(w =>
            {
                Json.WriteStringIf(w, "version", r.Version);
                Json.WriteStringIf(w, "name", options?.Name);
            });
            return await _pipeline.SendJsonAsync<SavedTypizer>(new ApiRequest(operationId, Http.Post, entry + suffix) { Body = Http.JsonBody(body) },
                requestOptions, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Добавляет схему в избранное (<c>PUT /v1/catalog/{entry}/favorite</c>).</summary>
        public async Task<CatalogFavorite> FavoriteAsync(string reference, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await FavoriteWithResponseAsync(reference, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="FavoriteAsync"/>
        public async Task<TypizerResponse<CatalogFavorite>> FavoriteWithResponseAsync(string reference, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            string entry = await EntryPathAsync(reference, requestOptions, cancellationToken).ConfigureAwait(false);
            return await _pipeline.SendJsonAsync<CatalogFavorite>(new ApiRequest("favoriteCatalogEntry", Http.Put, entry + "/favorite"),
                requestOptions, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Убирает схему из избранного (<c>DELETE /v1/catalog/{entry}/favorite</c>).</summary>
        public async Task<CatalogFavorite> UnfavoriteAsync(string reference, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await UnfavoriteWithResponseAsync(reference, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="UnfavoriteAsync"/>
        public async Task<TypizerResponse<CatalogFavorite>> UnfavoriteWithResponseAsync(string reference, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            string entry = await EntryPathAsync(reference, requestOptions, cancellationToken).ConfigureAwait(false);
            return await _pipeline.SendJsonAsync<CatalogFavorite>(new ApiRequest("unfavoriteCatalogEntry", Http.Delete, entry + "/favorite"),
                requestOptions, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Жалоба на схему (<c>POST /v1/catalog/{entry}/reports</c>): <paramref name="reason"/> — <c>pii</c>,
        /// <c>plagiarism</c>, <c>broken</c>, <c>spam</c> или <c>other</c>.
        /// </summary>
        public async Task<CatalogReport> ReportAsync(string reference, string reason, string? details = null,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await ReportWithResponseAsync(reference, reason, details, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="ReportAsync"/>
        public async Task<TypizerResponse<CatalogReport>> ReportWithResponseAsync(string reference, string reason, string? details = null,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        {
            string why = Require.NotEmpty(reason, nameof(reason));
            CatalogRef r = CatalogRef.Parse(reference, null, nameof(reference));
            string entry = await EntryPathAsync(reference, requestOptions, cancellationToken).ConfigureAwait(false);
            byte[] body = Json.WriteObject(w =>
            {
                w.WriteString("reason", why);
                Json.WriteStringIf(w, "details", details);
                Json.WriteStringIf(w, "version", r.Version);
            });
            return await _pipeline.SendJsonAsync<CatalogReport>(new ApiRequest("reportCatalogEntry", Http.Post, entry + "/reports") { Body = Http.JsonBody(body) },
                requestOptions, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// <c>/v1/catalog/{entry}</c> for the entry-scoped operations, which take a <c>cat_…</c> id or an
        /// official slug: a community <c>publisher/slug</c> is looked up first (one extra GET).
        /// </summary>
        private async Task<string> EntryPathAsync(string reference, RequestOptions? requestOptions, CancellationToken cancellationToken)
        {
            CatalogRef r = CatalogRef.Parse(reference, null, nameof(reference));
            string entry = r.Slug;
            if (r.Publisher != null)
            {
                CatalogEntry found = (await _pipeline.SendJsonAsync<CatalogEntry>(new ApiRequest("getCommunityCatalogEntry", Http.Get,
                        "/v1/catalog/" + Url.Segment(r.Publisher, nameof(reference)) + "/" + Url.Segment(r.Slug, nameof(reference))),
                    WithoutKey(requestOptions), cancellationToken).ConfigureAwait(false)).Value;
                entry = found.Id;
            }
            return "/v1/catalog/" + Url.Segment(entry, nameof(reference));
        }

        private static RequestOptions? WithoutKey(RequestOptions? options) =>
            options == null ? null : new RequestOptions { Timeout = options.Timeout, MaxRetries = options.MaxRetries, ExtraHeaders = options.ExtraHeaders };
    }

    /// <summary>Издатели каталога: <c>client.Catalog.Publishers</c>.</summary>
    public sealed class CatalogPublishersResource
    {
        private readonly HttpPipeline _pipeline;

        internal CatalogPublishersResource(HttpPipeline pipeline) => _pipeline = pipeline;

        /// <summary>Издатель и его схемы (<c>GET /v1/catalog/publishers/{slug}</c>).</summary>
        public async Task<CatalogPublisher> GetAsync(string slug, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await GetWithResponseAsync(slug, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetAsync"/>
        public Task<TypizerResponse<CatalogPublisher>> GetWithResponseAsync(string slug, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<CatalogPublisher>(new ApiRequest("getCatalogPublisher", Http.Get,
                "/v1/catalog/publishers/" + Url.Segment(slug, nameof(slug))), requestOptions, cancellationToken);
    }

    /// <summary><c>slug</c>, <c>publisher/slug</c> or <c>cat_…</c>, with an optional <c>@version</c>.</summary>
    internal readonly struct CatalogRef
    {
        private CatalogRef(string? publisher, string slug, string? version)
        {
            Publisher = publisher;
            Slug = slug;
            Version = version;
        }

        public string? Publisher { get; }
        public string Slug { get; }
        public string? Version { get; }

        public static CatalogRef Parse(string reference, string? version, string paramName)
        {
            string value = Require.NotEmpty(reference, paramName).Trim();
            string? inline = null;
            int at = value.IndexOf('@');
            if (at >= 0)
            {
                inline = value.Substring(at + 1);
                value = value.Substring(0, at);
                if (inline.Length == 0) throw new ArgumentException("Пустая версия после @ в «" + reference + "»", paramName);
            }
            if (inline != null && version != null && inline != version)
                throw new ArgumentException("Версия задана дважды и по-разному: «@" + inline + "» и «" + version + "»", paramName);
            string? publisher = null;
            int slash = value.IndexOf('/');
            if (slash >= 0)
            {
                publisher = value.Substring(0, slash);
                value = value.Substring(slash + 1);
                if (publisher.Length == 0 || value.Length == 0 || value.IndexOf('/') >= 0)
                    throw new ArgumentException("Ссылка на схему сообщества — «издатель/slug»: «" + reference + "»", paramName);
            }
            if (value.Length == 0) throw new ArgumentException("Пустая ссылка на схему каталога", paramName);
            return new CatalogRef(publisher, value, inline ?? version);
        }
    }
}
