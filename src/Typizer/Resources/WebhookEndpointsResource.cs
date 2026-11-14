using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Typizer.Internal;

namespace Typizer
{
    /// <summary>Конечные точки вебхуков: <c>client.WebhookEndpoints</c>.</summary>
    public sealed class WebhookEndpointsResource
    {
        private readonly HttpPipeline _pipeline;

        internal WebhookEndpointsResource(HttpPipeline pipeline)
        {
            _pipeline = pipeline;
            Deliveries = new WebhookDeliveriesResource(pipeline);
        }

        /// <summary>Доставки вебхуков.</summary>
        public WebhookDeliveriesResource Deliveries { get; }

        private static string PathOf(string id) => "/v1/webhook_endpoints/" + Url.Segment(id, nameof(id));

        /// <summary>
        /// Создаёт конечную точку (<c>POST /v1/webhook_endpoints</c>). В ответе — <see cref="WebhookEndpoint.Secret"/>
        /// (<c>whsec_…</c>) для <see cref="TypizerWebhooks.Verify(string, IEnumerable{KeyValuePair{string, string}}, string, TimeSpan?, DateTimeOffset?)"/>:
        /// он приходит один раз, сохраните его.
        /// </summary>
        public async Task<WebhookEndpoint> CreateAsync(string url, IEnumerable<WebhookEventType> events, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await CreateWithResponseAsync(url, events, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="CreateAsync"/>
        public Task<TypizerResponse<WebhookEndpoint>> CreateWithResponseAsync(string url, IEnumerable<WebhookEventType> events,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        {
            string target = Require.NotEmpty(url, nameof(url));
            List<WebhookEventType> list = Require.NotNull(events, nameof(events)).ToList();
            byte[] body = Json.WriteObject(w =>
            {
                w.WriteString("url", target);
                WriteEvents(w, list);
            });
            return _pipeline.SendJsonAsync<WebhookEndpoint>(new ApiRequest("createWebhookEndpoint", Http.Post, "/v1/webhook_endpoints") { Body = Http.JsonBody(body) },
                requestOptions, cancellationToken);
        }

        private static void WriteEvents(System.Text.Json.Utf8JsonWriter w, IEnumerable<WebhookEventType> events)
        {
            w.WritePropertyName("events");
            w.WriteStartArray();
            foreach (WebhookEventType e in events) w.WriteStringValue(e.Value);
            w.WriteEndArray();
        }

        /// <summary>Конечная точка по id (<c>GET /v1/webhook_endpoints/{id}</c>).</summary>
        public async Task<WebhookEndpoint> GetAsync(string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await GetWithResponseAsync(id, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="GetAsync"/>
        public Task<TypizerResponse<WebhookEndpoint>> GetWithResponseAsync(string id, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<WebhookEndpoint>(new ApiRequest("getWebhookEndpoint", Http.Get, PathOf(id)), requestOptions, cancellationToken);

        /// <summary>Меняет адрес, события или статус (<c>PATCH /v1/webhook_endpoints/{id}</c>).</summary>
        public async Task<WebhookEndpoint> UpdateAsync(string id, WebhookEndpointUpdateOptions options, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await UpdateWithResponseAsync(id, options, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="UpdateAsync"/>
        public Task<TypizerResponse<WebhookEndpoint>> UpdateWithResponseAsync(string id, WebhookEndpointUpdateOptions options,
            RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)
        {
            Require.NotNull(options, nameof(options));
            List<WebhookEventType>? events = options.Events?.ToList();
            byte[] body = Json.WriteObject(w =>
            {
                Json.WriteStringIf(w, "url", options.Url);
                if (events != null) WriteEvents(w, events);
                Json.WriteStringIf(w, "status", options.Status?.Value);
            });
            return _pipeline.SendJsonAsync<WebhookEndpoint>(new ApiRequest("updateWebhookEndpoint", Http.Patch, PathOf(id)) { Body = Http.JsonBody(body) },
                requestOptions, cancellationToken);
        }

        /// <summary>Удаляет конечную точку (<c>DELETE /v1/webhook_endpoints/{id}</c>).</summary>
        public async Task<DeletedObject> DeleteAsync(string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await DeleteWithResponseAsync(id, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="DeleteAsync"/>
        public Task<TypizerResponse<DeletedObject>> DeleteWithResponseAsync(string id, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<DeletedObject>(new ApiRequest("deleteWebhookEndpoint", Http.Delete, PathOf(id)), requestOptions, cancellationToken);

        /// <summary>Конечные точки организации (<c>GET /v1/webhook_endpoints</c>).</summary>
        public TypizerPageable<WebhookEndpoint> ListAsync(ListOptions? options = null, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            Lists.Create<WebhookEndpoint>(_pipeline, () => new ApiRequest("listWebhookEndpoints", Http.Get, "/v1/webhook_endpoints"),
                options, "starting_after", requestOptions, cancellationToken);

        /// <summary>Отправляет тестовое событие <c>run.succeeded</c> (<c>POST /v1/webhook_endpoints/{id}/test</c>).</summary>
        public async Task<WebhookDelivery> TestAsync(string id, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default) =>
            (await TestWithResponseAsync(id, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="TestAsync"/>
        public Task<TypizerResponse<WebhookDelivery>> TestWithResponseAsync(string id, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<WebhookDelivery>(new ApiRequest("testWebhookEndpoint", Http.Post, PathOf(id) + "/test"), requestOptions, cancellationToken);
    }

    /// <summary>Доставки вебхуков: <c>client.WebhookEndpoints.Deliveries</c>.</summary>
    public sealed class WebhookDeliveriesResource
    {
        private readonly HttpPipeline _pipeline;

        internal WebhookDeliveriesResource(HttpPipeline pipeline) => _pipeline = pipeline;

        /// <summary>Доставки конечной точки, новые первыми (<c>GET /v1/webhook_endpoints/{id}/deliveries</c>).</summary>
        public TypizerPageable<WebhookDelivery> ListAsync(string endpointId, ListOptions? options = null, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default)
        {
            string path = "/v1/webhook_endpoints/" + Url.Segment(endpointId, nameof(endpointId)) + "/deliveries";
            return Lists.Create<WebhookDelivery>(_pipeline, () => new ApiRequest("listWebhookDeliveries", Http.Get, path),
                options, "starting_after", requestOptions, cancellationToken);
        }

        /// <summary>Повторяет доставку сейчас (<c>POST /v1/webhook_deliveries/{id}/retry</c>).</summary>
        public async Task<WebhookDelivery> RetryAsync(string deliveryId, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            (await RetryWithResponseAsync(deliveryId, requestOptions, cancellationToken).ConfigureAwait(false)).Value;

        /// <inheritdoc cref="RetryAsync"/>
        public Task<TypizerResponse<WebhookDelivery>> RetryWithResponseAsync(string deliveryId, RequestOptions? requestOptions = null,
            CancellationToken cancellationToken = default) =>
            _pipeline.SendJsonAsync<WebhookDelivery>(new ApiRequest("retryWebhookDelivery", Http.Post,
                "/v1/webhook_deliveries/" + Url.Segment(deliveryId, nameof(deliveryId)) + "/retry"), requestOptions, cancellationToken);
    }
}
