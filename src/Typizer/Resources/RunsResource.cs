using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Typizer.Internal;

namespace Typizer
{
    /// <summary>Прогоны: <c>client.Runs</c>.</summary>
    public sealed class RunsResource
    {
        internal static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(300);
        internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MaxPollInterval = TimeSpan.FromSeconds(5);
        private readonly HttpPipeline _pipeline;

        internal RunsResource(HttpPipeline pipeline) => _pipeline = pipeline;

        /// <summary>
        /// Создаёт прогон (<c>POST /v1/extract</c> или, с <see cref="RunOptionsBase.Typizer"/>,
        /// <c>POST /v1/typizers/{id}/runs</c>) и возвращает его — готовым, если он уложился в
        /// <see cref="RunCreateOptions.Wait"/>, иначе в статусе <c>queued</c>/<c>processing</c>.
        /// </summary>
        public async Task<Run> CreateAsync(TypizerFile file, RunCreateOptions options, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await CreateWithResponseAsync(file, options, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="CreateAsync"/>
        public Task<TypizerResponse<Run>> CreateWithResponseAsync(TypizerFile file, RunCreateOptions options,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        {
            Require.NotNull(options, nameof(options));
            int? wait = null;
            if (options.Wait.HasValue)
            {
                if (options.Wait.Value < TimeSpan.Zero || options.Wait.Value > TimeSpan.FromSeconds(60))
                    throw new ArgumentOutOfRangeException(nameof(options), "Wait: от 0 до 60 секунд");
                wait = (int)Math.Floor(options.Wait.Value.TotalSeconds);
            }
            return CreateCoreAsync(file, options, wait, requestOptions, null, cancellationToken);
        }

        internal async Task<TypizerResponse<Run>> CreateCoreAsync(TypizerFile file, RunOptionsBase options, int? wait,
            RequestOptions? requestOptions, long? deadline, CancellationToken cancellationToken)
        {
            Require.NotNull(file, nameof(file));
            ValidateTarget(options);
            IdempotencyKeys.Validate(requestOptions?.IdempotencyKey, nameof(RequestOptions.IdempotencyKey));
            JsonElement? schema = options.Schema != null ? Json.ToSchema(options.Schema, nameof(options.Schema)) : (JsonElement?)null;

            ApiRequest request = options.Typizer != null
                ? new ApiRequest("createRun", Http.Post, "/v1/typizers/" + Url.Segment(options.Typizer, nameof(options.Typizer)) + "/runs")
                : new ApiRequest("extract", Http.Post, "/v1/extract");
            if (wait.HasValue)
            {
                request.With("wait", Url.Int(wait.Value));
                request.MinAttemptTimeout = TimeSpan.FromSeconds(wait.Value + 15);
            }
            request.Deadline = deadline;

            if (file.IsLocal)
            {
                MultipartBody.FilePart part = await file.PrepareAsync("file", cancellationToken).ConfigureAwait(false);
                var fields = new List<KeyValuePair<string, string>>();
                void Field(string name, string? value)
                {
                    if (value != null) fields.Add(new KeyValuePair<string, string>(name, value));
                }
                Field("schema", schema.HasValue ? Json.ToText(schema.Value) : null);
                Field("catalog", options.Catalog);
                Field("instructions", options.Instructions);
                Field("format", options.Format?.Value);
                Field("mode", options.Mode?.Value);
                Field("webhook_url", options.WebhookUrl);
                Field("metadata", options.Metadata != null ? Json.MetadataText(options.Metadata) : null);
                request.Body = () => new MultipartBody(fields, new[] { part });
            }
            else
            {
                byte[] body = Json.WriteObject(w =>
                {
                    if (file.Kind == TypizerFile.SourceKind.Url) w.WriteString("url", file.Url);
                    else w.WriteString("file_id", file.Id);
                    if (schema.HasValue)
                    {
                        w.WritePropertyName("schema");
                        schema.Value.WriteTo(w);
                    }
                    Json.WriteStringIf(w, "catalog", options.Catalog);
                    Json.WriteStringIf(w, "instructions", options.Instructions);
                    Json.WriteStringIf(w, "webhook_url", options.WebhookUrl);
                    Json.WriteMetadata(w, options.Metadata);
                    Json.WriteStringIf(w, "format", options.Format?.Value);
                    Json.WriteStringIf(w, "mode", options.Mode?.Value);
                });
                request.Body = Http.JsonBody(body);
            }
            return await _pipeline.SendJsonAsync<Run>(request, requestOptions, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>SPEC §3: exactly one of schema, catalog, typizer; instructions only with schema — before any request.</summary>
        internal static void ValidateTarget(RunOptionsBase options)
        {
            int targets = (options.Schema != null ? 1 : 0) + (options.Catalog != null ? 1 : 0) + (options.Typizer != null ? 1 : 0);
            if (targets != 1)
            {
                throw new ArgumentException(targets == 0
                    ? "Укажите, по какой схеме распознавать: Schema (JSON Schema), Catalog (\"schet-faktura@1\") или Typizer (\"tz_…\")."
                    : "Укажите ровно одно из Schema, Catalog, Typizer.", nameof(options));
            }
            if (options.Catalog != null && string.IsNullOrWhiteSpace(options.Catalog))
                throw new ArgumentException("Catalog не может быть пустым", nameof(options));
            if (options.Typizer != null && string.IsNullOrWhiteSpace(options.Typizer))
                throw new ArgumentException("Typizer не может быть пустым", nameof(options));
            if (options.Instructions != null && options.Schema == null)
                throw new ArgumentException("Instructions задаются только вместе со Schema (у каталога и типизатора свои).", nameof(options));
        }

        /// <summary>Прогон по id (<c>GET /v1/runs/{id}</c>).</summary>
        public async Task<Run> GetAsync(string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await GetWithResponseAsync(id, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetAsync"/>
        public Task<TypizerResponse<Run>> GetWithResponseAsync(string id, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<Run>(new ApiRequest("getRun", Http.Get, "/v1/runs/" + Url.Segment(id, nameof(id))),
                requestOptions, cancellationToken);

        /// <summary>
        /// Дожидается конечного статуса прогона опросом <c>GET /v1/runs/{id}</c> (пауза 1 с, ×1,5, не
        /// больше 5 с) и возвращает его — и <c>succeeded</c>, и <c>failed</c>, без исключения. Срок истёк —
        /// <see cref="TypizerWaitTimeoutException"/>. По умолчанию срок — 300 с.
        /// </summary>
        public Task<Run> WaitAsync(string id, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            WaitAsync(id, timeout, null, null, cancellationToken);

        /// <inheritdoc cref="WaitAsync(string, TimeSpan?, CancellationToken)"/>
        public async Task<Run> WaitAsync(string id, TimeSpan? timeout, TimeSpan? pollInterval, RequestOptions? requestOptions,
            CancellationToken cancellationToken = default)
        {
            Url.Segment(id, nameof(id));
            TimeSpan total = CheckPositive(timeout ?? DefaultWaitTimeout, nameof(timeout));
            TimeSpan interval = CheckPositive(pollInterval ?? DefaultPollInterval, nameof(pollInterval));
            long started = Clock.Now();
            long deadline = Clock.After(total);
            Run run = await GetPolledAsync(id, requestOptions, deadline, cancellationToken).ConfigureAwait(false);
            return await PollAsync(run, started, deadline, interval, requestOptions, cancellationToken).ConfigureAwait(false);
        }

        internal static TimeSpan CheckPositive(TimeSpan value, string paramName)
        {
            if (value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(paramName, "Должно быть больше нуля");
            return value;
        }

        /// <summary>SPEC §4.4: poll until terminal; an unknown status is not terminal; never past the deadline.</summary>
        internal async Task<Run> PollAsync(Run run, long started, long deadline, TimeSpan interval, RequestOptions? requestOptions,
            CancellationToken cancellationToken)
        {
            while (!run.IsTerminal)
            {
                TimeSpan remaining = Clock.Remaining(deadline);
                if (remaining <= TimeSpan.Zero) throw new TypizerWaitTimeoutException(run, Clock.Since(started));
                await Task.Delay(interval < remaining ? interval : remaining, cancellationToken).ConfigureAwait(false);
                interval = TimeSpan.FromTicks(Math.Min((long)(interval.Ticks * 1.5), MaxPollInterval.Ticks));
                run = await GetPolledAsync(run.Id, requestOptions, deadline, cancellationToken).ConfigureAwait(false);
            }
            return run;
        }

        private async Task<Run> GetPolledAsync(string id, RequestOptions? requestOptions, long deadline, CancellationToken cancellationToken)
        {
            var request = new ApiRequest("getRun", Http.Get, "/v1/runs/" + Url.Segment(id, nameof(id))) { Deadline = deadline };
            return (await _pipeline.SendJsonAsync<Run>(request, WithoutKey(requestOptions), cancellationToken).ConfigureAwait(false)).Value;
        }

        private static RequestOptions? WithoutKey(RequestOptions? options) =>
            options == null ? null : new RequestOptions { Timeout = options.Timeout, MaxRetries = options.MaxRetries, ExtraHeaders = options.ExtraHeaders };

        /// <summary>
        /// Прогоны, новые первыми (<c>GET /v1/runs</c>). <c>await foreach</c> — все прогоны всех страниц;
        /// <c>await</c> — первая страница. <c>Output</c> и <c>Fields</c> у элементов списка пусты.
        /// </summary>
        public TypizerPageable<Run> ListAsync(RunListOptions? options = null, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            Lists.Create<Run>(_pipeline, () => new ApiRequest("listRuns", Http.Get, "/v1/runs")
                    .With("status", options?.Status?.Value)
                    .With("typizer_id", options?.TypizerId)
                    .With("batch_id", options?.BatchId)
                    .With("source", options?.Source)
                    .With("created_after", options?.CreatedAfter != null ? Url.Timestamp(options.CreatedAfter.Value) : null)
                    .With("created_before", options?.CreatedBefore != null ? Url.Timestamp(options.CreatedBefore.Value) : null),
                options, "starting_after", requestOptions, cancellationToken);

        /// <summary>
        /// Результат прогона файлом (<c>GET /v1/runs/{id}/output</c>): JSON, XML для 1С, CSV или XLSX. По
        /// умолчанию — в формате прогона.
        /// </summary>
        public Task<TypizerDownload> GetOutputAsync(string id, OutputFormat? format = null, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendDownloadAsync(new ApiRequest("getRunOutput", Http.Get, "/v1/runs/" + Url.Segment(id, nameof(id)) + "/output")
                .With("format", format?.Value), requestOptions, cancellationToken);

        /// <summary>Повторяет завершившийся <c>failed</c> прогон (<c>POST /v1/runs/{id}/retry</c>) — новый прогон того же документа.</summary>
        public async Task<Run> RetryAsync(string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await RetryWithResponseAsync(id, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="RetryAsync"/>
        public Task<TypizerResponse<Run>> RetryWithResponseAsync(string id, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<Run>(new ApiRequest("retryRun", Http.Post, "/v1/runs/" + Url.Segment(id, nameof(id)) + "/retry"),
                requestOptions, cancellationToken);
    }
}
