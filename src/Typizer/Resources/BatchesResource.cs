using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Typizer.Internal;

namespace Typizer
{
    /// <summary>Пакеты — много документов одним типизатором: <c>client.Batches</c>.</summary>
    public sealed class BatchesResource
    {
        private readonly HttpPipeline _pipeline;

        internal BatchesResource(HttpPipeline pipeline) => _pipeline = pipeline;

        /// <summary>
        /// Запускает пакет (<c>POST /v1/batches</c>): файлы с диска, из памяти или потоков уходят в одном
        /// запросе (multipart), уже загруженные (<see cref="TypizerFile.FromId(string)"/>) — списком
        /// <c>file_ids</c>. Смешивать оба вида в одном пакете нельзя.
        /// </summary>
        public async Task<Batch> CreateAsync(string typizerId, IEnumerable<TypizerFile> files, BatchCreateOptions? options = null,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await CreateWithResponseAsync(typizerId, files, options, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="CreateAsync"/>
        public async Task<TypizerResponse<Batch>> CreateWithResponseAsync(string typizerId, IEnumerable<TypizerFile> files,
            BatchCreateOptions? options = null, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        {
            string typizer = Require.NotEmpty(typizerId, nameof(typizerId));
            List<TypizerFile> list = Require.NotNull(files, nameof(files)).ToList();
            if (list.Count == 0) throw new ArgumentException("Пакет без файлов", nameof(files));
            if (list.Any(f => f == null)) throw new ArgumentException("null в списке файлов", nameof(files));
            if (list.Any(f => f.Kind == TypizerFile.SourceKind.Url))
                throw new ArgumentException("Пакет принимает файлы и file_id, но не URL.", nameof(files));
            bool local = list[0].IsLocal;
            if (list.Any(f => f.IsLocal != local))
                throw new ArgumentException("Все файлы пакета — либо локальные (FromPath/FromBytes/FromStream), либо загруженные (FromId).", nameof(files));

            var request = new ApiRequest("createBatch", Http.Post, "/v1/batches");
            if (local)
            {
                var parts = new List<MultipartBody.FilePart>(list.Count);
                foreach (TypizerFile file in list)
                    parts.Add(await file.PrepareAsync("files", cancellationToken).ConfigureAwait(false));
                var fields = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("typizer_id", typizer) };
                if (options?.Format != null) fields.Add(new KeyValuePair<string, string>("format", options.Format.Value.Value));
                if (options?.Mode != null) fields.Add(new KeyValuePair<string, string>("mode", options.Mode.Value.Value));
                if (options?.Metadata != null) fields.Add(new KeyValuePair<string, string>("metadata", Json.MetadataText(options.Metadata)));
                request.Body = () => new MultipartBody(fields, parts);
            }
            else
            {
                byte[] body = Json.WriteObject(w =>
                {
                    w.WriteString("typizer_id", typizer);
                    w.WritePropertyName("file_ids");
                    w.WriteStartArray();
                    foreach (TypizerFile file in list) w.WriteStringValue(file.Id);
                    w.WriteEndArray();
                    Json.WriteStringIf(w, "format", options?.Format?.Value);
                    Json.WriteStringIf(w, "mode", options?.Mode?.Value);
                    Json.WriteMetadata(w, options?.Metadata);
                });
                request.Body = Http.JsonBody(body);
            }
            return await _pipeline.SendJsonAsync<Batch>(request, requestOptions, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Пакет по id (<c>GET /v1/batches/{id}</c>).</summary>
        public async Task<Batch> GetAsync(string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await GetWithResponseAsync(id, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetAsync"/>
        public Task<TypizerResponse<Batch>> GetWithResponseAsync(string id, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<Batch>(new ApiRequest("getBatch", Http.Get, "/v1/batches/" + Url.Segment(id, nameof(id))),
                requestOptions, cancellationToken);

        /// <summary>Пакеты, новые первыми (<c>GET /v1/batches</c>). Прогоны пакета — <c>client.Runs.ListAsync(new() { BatchId = … })</c>.</summary>
        public TypizerPageable<Batch> ListAsync(ListOptions? options = null, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            Lists.Create<Batch>(_pipeline, () => new ApiRequest("listBatches", Http.Get, "/v1/batches"),
                options, "starting_after", requestOptions, cancellationToken);

        /// <summary>Результаты пакета одним файлом (<c>GET /v1/batches/{id}/export</c>): <c>csv</c>, <c>xlsx</c> или <c>zip</c>.</summary>
        public Task<TypizerDownload> ExportAsync(string id, string format, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendDownloadAsync(new ApiRequest("exportBatch", Http.Get, "/v1/batches/" + Url.Segment(id, nameof(id)) + "/export")
                .With("format", Require.NotEmpty(format, nameof(format))), requestOptions, cancellationToken);
    }
}
