using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Typizer
{
    // Models of apps/docs/openapi.public.json `components.schemas`, fields in PascalCase (the JSON
    // names are their snake_case). Two names differ from the schemas, because in C# they would clash:
    // `File` → UploadedFile (System.IO.File is in scope in almost every file that uploads one) and
    // `Typizer` → SavedTypizer (a type cannot share the name of its namespace `Typizer`).

    /// <summary>Загруженный файл (<c>POST /v1/files</c>) — схема <c>File</c> справочника API.</summary>
    public sealed class UploadedFile
    {
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = "file";
        public string Filename { get; set; } = string.Empty;
        public string Mime { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public int? PagesEstimate { get; set; }
        public string Source { get; set; } = string.Empty;
        public DateTimeOffset? ExpiresAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
        public override string ToString() => "UploadedFile(" + Id + ")";
    }

    /// <summary>Ответ на удаление: <c>DeletedFile</c>, <c>DeletedTypizer</c>, <c>DeletedWebhookEndpoint</c>.</summary>
    public sealed class DeletedObject
    {
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = string.Empty;
        public bool Deleted { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>
    /// Типизатор — сохранённая схема с настройками (<c>/v1/typizers</c>) — схема <c>Typizer</c>
    /// справочника API.
    /// </summary>
    public sealed class SavedTypizer
    {
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = "typizer";
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int Version { get; set; }
        public JsonElement Schema { get; set; }
        public string Instructions { get; set; } = string.Empty;
        public OutputFormat DefaultFormat { get; set; }
        public RunMode Mode { get; set; }
        public TypizerCatalogSource? CatalogSource { get; set; }
        /// <summary>
        /// Проверки, на версиях которых зафиксирована текущая версия (ссылки из <c>x-typizer-check</c>:
        /// <c>my/contract-no</c> + <c>1.0.0</c>). Пустой список — проверок по ссылке нет.
        /// </summary>
        public IReadOnlyList<TypizerCheck> Checks { get; set; } = Array.Empty<TypizerCheck>();
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
        public override string ToString() => "SavedTypizer(" + Id + ", " + Name + ")";
    }

    /// <summary>Откуда типизатор: схема каталога, установленная или скопированная.</summary>
    public sealed class TypizerCatalogSource
    {
        public string? EntryId { get; set; }
        public string? Ref { get; set; }
        public string Title { get; set; } = string.Empty;
        public CatalogPublisherBrief? Publisher { get; set; }
        /// <summary><c>installed</c> или <c>forked</c>.</summary>
        public string Relation { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        /// <summary><c>active</c> или <c>removed</c>.</summary>
        public string Status { get; set; } = string.Empty;
        public bool AutoUpdate { get; set; }
        public TypizerCatalogSourceUpdate? Update { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Доступное обновление схемы каталога.</summary>
    public sealed class TypizerCatalogSourceUpdate
    {
        public string Version { get; set; } = string.Empty;
        public bool Breaking { get; set; }
        public string Changelog { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Черновик типизатора от <c>POST /v1/typizers/generate</c> (ещё не сохранён).</summary>
    public sealed class TypizerDraft
    {
        public string Object { get; set; } = "typizer_draft";
        public string Name { get; set; } = string.Empty;
        public JsonElement Schema { get; set; }
        public string Instructions { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Версия типизатора.</summary>
    public sealed class TypizerVersion
    {
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = "typizer_version";
        public string TypizerId { get; set; } = string.Empty;
        public int Version { get; set; }
        public JsonElement Schema { get; set; }
        public string Instructions { get; set; } = string.Empty;
        /// <summary>Проверки, на версиях которых зафиксирована эта версия.</summary>
        public IReadOnlyList<TypizerCheck> Checks { get; set; } = Array.Empty<TypizerCheck>();
        public DateTimeOffset CreatedAt { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Проверка, на версии которой зафиксирован типизатор (<see cref="SavedTypizer.Checks"/>).</summary>
    public sealed class TypizerCheck
    {
        /// <summary><c>my/contract-no</c>, <c>inn</c>, <c>vin</c>, …</summary>
        public string Ref { get; set; } = string.Empty;
        /// <summary>Точная версия: <c>1.0.0</c>.</summary>
        public string Version { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
        public override string ToString() => "TypizerCheck(" + Ref + "@" + Version + ")";
    }

    /// <summary>Пакет — много документов одним типизатором.</summary>
    public sealed class Batch
    {
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = "batch";
        public string TypizerId { get; set; } = string.Empty;
        public BatchStatus Status { get; set; }
        public int Total { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
        public override string ToString() => "Batch(" + Id + ", " + Status + ")";
    }

    /// <summary>Конечная точка вебхуков.</summary>
    public sealed class WebhookEndpoint
    {
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = "webhook_endpoint";
        public string Url { get; set; } = string.Empty;
        public IReadOnlyList<WebhookEventType> Events { get; set; } = Array.Empty<WebhookEventType>();
        public WebhookEndpointStatus Status { get; set; }
        /// <summary>
        /// <c>whsec_…</c> — ключ проверки подписи; приходит только в ответе на создание (повтор
        /// создания с тем же <c>Idempotency-Key</c> отдаёт <c>null</c>).
        /// </summary>
        public string? Secret { get; set; }
        public DateTimeOffset? FailingSince { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
        public override string ToString() => "WebhookEndpoint(" + Id + ", " + Url + ")";
    }

    /// <summary>Доставка вебхука.</summary>
    public sealed class WebhookDelivery
    {
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = "webhook_delivery";
        public string EventId { get; set; } = string.Empty;
        public WebhookEventType EventType { get; set; }
        public WebhookDeliveryStatus Status { get; set; }
        public int Attempt { get; set; }
        public int? ResponseCode { get; set; }
        public DateTimeOffset? NextAttemptAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Баланс страниц.</summary>
    public sealed class Balance
    {
        public string Object { get; set; } = "balance";
        public int PagesAvailable { get; set; }
        public int PagesReserved { get; set; }
        public IReadOnlyList<BalanceLot> Lots { get; set; } = Array.Empty<BalanceLot>();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Пакет страниц на балансе.</summary>
    public sealed class BalanceLot
    {
        /// <summary><c>free</c>, <c>inn_bonus</c>, <c>purchase</c>, <c>adjustment</c>.</summary>
        public string Source { get; set; } = string.Empty;
        public int PagesLeft { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Расход за период (<c>GET /v1/usage</c>).</summary>
    public sealed class Usage
    {
        public string Object { get; set; } = "usage";
        /// <summary>Первый день периода (дата без времени).</summary>
        [JsonConverter(typeof(Internal.DateOnlyJsonConverter))]
        public DateTime From { get; set; }
        /// <summary>Последний день периода (дата без времени).</summary>
        [JsonConverter(typeof(Internal.DateOnlyJsonConverter))]
        public DateTime To { get; set; }
        /// <summary><c>day</c>, <c>typizer</c> или <c>key</c>.</summary>
        public string GroupBy { get; set; } = string.Empty;
        public IReadOnlyList<UsageRow> Data { get; set; } = Array.Empty<UsageRow>();
        public bool HasMore { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Строка расхода: день, типизатор или ключ API.</summary>
    public sealed class UsageRow
    {
        public string? Key { get; set; }
        public int Runs { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public int PagesBilled { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Кто вызывает API (<c>GET /v1/me</c>).</summary>
    public sealed class Me
    {
        public string Object { get; set; } = "me";
        /// <summary><c>api_key</c> или <c>session</c>.</summary>
        public string Auth { get; set; } = string.Empty;
        public MeOrganization Organization { get; set; } = new MeOrganization();
        public MeUser? User { get; set; }
        /// <summary><c>owner</c>, <c>admin</c>, <c>member</c> или пусто.</summary>
        public string Role { get; set; } = string.Empty;
        public MeApiKey? ApiKey { get; set; }
        public IReadOnlyList<MeMembership>? Organizations { get; set; }
        public MeFeatures Features { get; set; } = new MeFeatures();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed class MeOrganization
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed class MeUser
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool TwoFactorEnabled { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed class MeApiKey
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary><c>typizers:read</c>, <c>typizers:write</c>, <c>runs:read</c>, <c>runs:write</c>, <c>billing:read</c>.</summary>
        public IReadOnlyList<string> Scopes { get; set; } = Array.Empty<string>();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed class MeMembership
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed class MeFeatures
    {
        public bool BillingEnabled { get; set; }
        public bool EdoDiadoc { get; set; }
        public bool CatalogCommunity { get; set; }
        /// <summary>Приём документов по почте («Почта для документов»).</summary>
        public bool InboundMail { get; set; }
        /// <summary>Доступен <c>client.Validators.SuggestAsync</c> — проверку можно составить по описанию словами.</summary>
        public bool ValidatorsSuggest { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Организация ключа API.</summary>
    public sealed class Organization
    {
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = "organization";
        public string Name { get; set; } = string.Empty;
        public OrganizationSettings Settings { get; set; } = new OrganizationSettings();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed class OrganizationSettings
    {
        /// <summary><c>immediate</c>, <c>h24</c>, <c>d7</c>, <c>d30</c>.</summary>
        public string FileRetention { get; set; } = string.Empty;
        /// <summary><c>none</c>, <c>d30</c>, <c>d90</c>.</summary>
        public string OutputRetention { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Плановые работы и инциденты (<c>GET /v1/status</c>).</summary>
    public sealed class Status
    {
        public string Object { get; set; } = "status";
        public IReadOnlyList<MaintenanceEvent> Data { get; set; } = Array.Empty<MaintenanceEvent>();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    public sealed class MaintenanceEvent
    {
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = "maintenance_event";
        /// <summary><c>planned</c> или <c>incident</c>.</summary>
        public string Kind { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string MessageRu { get; set; } = string.Empty;
        public IReadOnlyList<string> Components { get; set; } = Array.Empty<string>();
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset EndsAt { get; set; }
        /// <summary><c>scheduled</c> или <c>in_progress</c>.</summary>
        public string Status { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Тело ошибки API (<c>application/problem+json</c>, RFC 9457).</summary>
    public sealed class Problem
    {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Status { get; set; }
        public string Detail { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? Param { get; set; }
        public string RequestId { get; set; } = string.Empty;
        public string DocUrl { get; set; } = string.Empty;
        /// <summary>Прочие поля: <c>batch_id</c>, <c>created</c>, <c>expected</c>, <c>findings</c>, <c>retry_after</c>…</summary>
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}
