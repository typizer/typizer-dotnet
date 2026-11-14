using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Typizer.Internal;

namespace Typizer
{
    /// <summary>Файлы: <c>client.Files</c>.</summary>
    public sealed class FilesResource
    {
        private readonly HttpPipeline _pipeline;

        internal FilesResource(HttpPipeline pipeline) => _pipeline = pipeline;

        /// <summary>
        /// Загружает документ (<c>POST /v1/files</c>), чтобы запускать по нему прогоны и пакеты по
        /// <c>file_id</c>. Для одного прогона загружать заранее не нужно — передайте файл прямо в <c>ExtractAsync</c>.
        /// </summary>
        public async Task<UploadedFile> UploadAsync(TypizerFile file, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await UploadWithResponseAsync(file, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="UploadAsync"/>
        public async Task<TypizerResponse<UploadedFile>> UploadWithResponseAsync(TypizerFile file, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            Require.NotNull(file, nameof(file));
            if (!file.IsLocal)
                throw new ArgumentException("Загрузить можно файл с диска, из памяти или из потока (TypizerFile.FromPath/FromBytes/FromStream).", nameof(file));
            MultipartBody.FilePart part = await file.PrepareAsync("file", cancellationToken).ConfigureAwait(false);
            var request = new ApiRequest("createFile", Http.Post, "/v1/files")
            {
                Body = () => new MultipartBody(Array.Empty<KeyValuePair<string, string>>(), new[] { part }),
            };
            return await _pipeline.SendJsonAsync<UploadedFile>(request, requestOptions, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Файл по id (<c>GET /v1/files/{id}</c>).</summary>
        public async Task<UploadedFile> GetAsync(string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await GetWithResponseAsync(id, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetAsync"/>
        public Task<TypizerResponse<UploadedFile>> GetWithResponseAsync(string id, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<UploadedFile>(new ApiRequest("getFile", Http.Get, "/v1/files/" + Url.Segment(id, nameof(id))),
                requestOptions, cancellationToken);

        /// <summary>Удаляет файл (<c>DELETE /v1/files/{id}</c>).</summary>
        public async Task<DeletedObject> DeleteAsync(string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await DeleteWithResponseAsync(id, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="DeleteAsync"/>
        public Task<TypizerResponse<DeletedObject>> DeleteWithResponseAsync(string id, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<DeletedObject>(new ApiRequest("deleteFile", Http.Delete, "/v1/files/" + Url.Segment(id, nameof(id))),
                requestOptions, cancellationToken);

        /// <summary>Содержимое файла (<c>GET /v1/files/{id}/content</c>).</summary>
        public Task<TypizerDownload> GetContentAsync(string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            _pipeline.SendDownloadAsync(new ApiRequest("getFileContent", Http.Get, "/v1/files/" + Url.Segment(id, nameof(id)) + "/content"),
                requestOptions, cancellationToken);
    }
}
