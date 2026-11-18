using System;
using System.Collections.Generic;

namespace Typizer
{
    /// <summary>
    /// Что распознавать и как: ровно одно из <see cref="Schema"/>, <see cref="Catalog"/>,
    /// <see cref="Typizer"/>, плюс необязательные настройки прогона. Незаданное не отправляется.
    /// </summary>
    public abstract class RunOptionsBase
    {
        /// <summary>
        /// JSON Schema результата: JSON-текст, <c>JsonElement</c>/<c>JsonDocument</c>/<c>JsonNode</c>,
        /// словарь или объект. Порядок свойств сохраняется (по нему идут столбцы и элементы XML).
        /// </summary>
        public object? Schema { get; set; }

        /// <summary>Схема каталога: <c>schet-faktura</c>, <c>schet-faktura@1</c>, <c>acme/akt-sverki@1.2.0</c>.</summary>
        public string? Catalog { get; set; }

        /// <summary>Сохранённый типизатор: <c>tz_…</c> (или <see cref="SavedTypizer.Id"/>).</summary>
        public string? Typizer { get; set; }

        /// <summary>Подсказки модели — только вместе со <see cref="Schema"/>.</summary>
        public string? Instructions { get; set; }

        public RunMode? Mode { get; set; }

        public OutputFormat? Format { get; set; }

        /// <summary>Ваши метки (строки) — вернутся в прогоне и вебхуке.</summary>
        public IDictionary<string, string>? Metadata { get; set; }

        /// <summary>HTTPS-адрес для вебхука о завершении этого прогона.</summary>
        public string? WebhookUrl { get; set; }
    }

    /// <summary>Настройки <see cref="TypizerClient.ExtractAsync(TypizerFile, ExtractOptions, System.Threading.CancellationToken)"/>.</summary>
    public sealed class ExtractOptions : RunOptionsBase
    {
        /// <summary>
        /// Общий срок: создание и ожидание прогона. По умолчанию 300 с. Истёк — <see cref="TypizerWaitTimeoutException"/>
        /// (прогон продолжается и будет оплачен).
        /// </summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>Первая пауза между опросами (дальше ×1,5, не больше 5 с). По умолчанию 1 с.</summary>
        public TimeSpan? PollInterval { get; set; }

        /// <summary>Свой ключ идемпотентности создания прогона (иначе — UUID v4 на вызов).</summary>
        public string? IdempotencyKey { get; set; }

        /// <summary>Таймаут одной HTTP-попытки (по умолчанию — из настроек клиента).</summary>
        public TimeSpan? RequestTimeout { get; set; }

        /// <summary>Число повторов каждого запроса (по умолчанию — из настроек клиента).</summary>
        public int? MaxRetries { get; set; }

        /// <summary>Дополнительные заголовки запросов.</summary>
        public IDictionary<string, string>? ExtraHeaders { get; set; }
    }

    /// <summary>Настройки <c>client.Runs.CreateAsync</c>.</summary>
    public sealed class RunCreateOptions : RunOptionsBase
    {
        /// <summary>
        /// Серверное ожидание, 0–60 с: прогон, завершившийся за это время, вернётся готовым (200), иначе —
        /// <c>queued</c> (202). Не задано — сервер не ждёт.
        /// </summary>
        public TimeSpan? Wait { get; set; }
    }

    /// <summary>Лимит и курсор списка.</summary>
    public class ListOptions
    {
        /// <summary>Элементов на странице, 1–100 (по умолчанию у API — 20).</summary>
        public int? Limit { get; set; }

        /// <summary>Продолжить с курсора <see cref="Page{T}.NextCursor"/> прошлой страницы.</summary>
        public string? Cursor { get; set; }
    }

    /// <summary>Фильтр <c>client.Runs.ListAsync</c>.</summary>
    public sealed class RunListOptions : ListOptions
    {
        public RunStatus? Status { get; set; }
        public string? TypizerId { get; set; }
        public string? BatchId { get; set; }
        /// <summary><c>api</c>, <c>my</c>, <c>mcp</c>, <c>demo</c>, <c>agent</c>.</summary>
        public string? Source { get; set; }
        public DateTimeOffset? CreatedAfter { get; set; }
        public DateTimeOffset? CreatedBefore { get; set; }
    }

    /// <summary>Фильтр <c>client.Catalog.ListAsync</c>.</summary>
    public sealed class CatalogListOptions : ListOptions
    {
        /// <summary>Поиск по названию, описанию и полям.</summary>
        public string? Query { get; set; }
        public CatalogCategory? Category { get; set; }
        public CatalogKind? Kind { get; set; }
        /// <summary>Slug издателя.</summary>
        public string? Publisher { get; set; }
        /// <summary>Только избранные.</summary>
        public bool? Favorites { get; set; }
        /// <summary><c>recommended</c>, <c>popular</c>, <c>new</c>, <c>updated</c>.</summary>
        public string? Sort { get; set; }
    }

    /// <summary>Своя копия проверки каталога; пустые поля — slug и название проверки каталога.</summary>
    public sealed class CatalogForkValidatorOptions
    {
        /// <summary>Мажор (<c>1</c>) или точная версия (<c>1.2.0</c>); по умолчанию — из <c>@</c> в ссылке.</summary>
        public string? Version { get; set; }
        /// <summary>Ссылка <c>my/&lt;slug&gt;</c>.</summary>
        public string? Slug { get; set; }
        public string? Name { get; set; }
    }

    /// <summary>Новый типизатор.</summary>
    public sealed class TypizerCreateOptions
    {
        public string? Name { get; set; }
        /// <summary>JSON Schema — как <see cref="RunOptionsBase.Schema"/>.</summary>
        public object? Schema { get; set; }
        public string? Description { get; set; }
        public string? Instructions { get; set; }
        public OutputFormat? DefaultFormat { get; set; }
        public RunMode? Mode { get; set; }
    }

    /// <summary>
    /// Изменения типизатора (<c>PATCH</c>): отправляются только заданные свойства. Явное
    /// <c>Description = null</c> очищает описание.
    /// </summary>
    public sealed class TypizerUpdateOptions
    {
        private string? _description;

        public string? Name { get; set; }
        public object? Schema { get; set; }
        public string? Description
        {
            get => _description;
            set
            {
                _description = value;
                DescriptionSet = true;
            }
        }
        internal bool DescriptionSet { get; private set; }
        public string? Instructions { get; set; }
        public OutputFormat? DefaultFormat { get; set; }
        public RunMode? Mode { get; set; }
    }

    /// <summary>Черновик типизатора по описанию и/или образцу документа.</summary>
    public sealed class TypizerGenerateOptions
    {
        /// <summary>Что извлекать, своими словами.</summary>
        public string? Description { get; set; }
        /// <summary>Образец документа, загруженный через <c>client.Files.UploadAsync</c>.</summary>
        public string? FileId { get; set; }
    }

    /// <summary>Установка или копия схемы каталога.</summary>
    public sealed class CatalogInstallOptions
    {
        /// <summary>Мажор (<c>1</c>) или точная версия (<c>1.2.0</c>); по умолчанию — последняя.</summary>
        public string? Version { get; set; }
        /// <summary>Имя нового типизатора.</summary>
        public string? Name { get; set; }
    }

    /// <summary>Пакет документов.</summary>
    public sealed class BatchCreateOptions
    {
        public OutputFormat? Format { get; set; }
        public RunMode? Mode { get; set; }
        public IDictionary<string, string>? Metadata { get; set; }
    }

    /// <summary>Изменения конечной точки вебхуков (<c>PATCH</c>): отправляются только заданные свойства.</summary>
    public sealed class WebhookEndpointUpdateOptions
    {
        public string? Url { get; set; }
        public IEnumerable<WebhookEventType>? Events { get; set; }
        public WebhookEndpointStatus? Status { get; set; }
    }

    /// <summary>Новая своя проверка (<c>client.Validators.CreateAsync</c>).</summary>
    public sealed class ValidatorCreateOptions
    {
        /// <summary>Название: <c>Номер договора</c> (до 80 символов).</summary>
        public string? Name { get; set; }
        /// <summary>
        /// Спецификация — объект JSON как есть: JSON-текст, <c>JsonElement</c>, <c>JsonNode</c>, словарь
        /// или объект (<c>{"kind": "rules", "applies_to": "string", "rules": […], "message": "…"}</c>).
        /// </summary>
        public object? Spec { get; set; }
        /// <summary>Тесты — контракт версии: «должно пройти» и «не должно пройти» (до 100).</summary>
        public IEnumerable<ValidatorTest>? Tests { get; set; }
        /// <summary>Ссылка <c>my/&lt;slug&gt;</c>; по умолчанию — из названия.</summary>
        public string? Slug { get; set; }
        public string? Description { get; set; }
        /// <summary>Что изменилось — для истории версий.</summary>
        public string? Changelog { get; set; }
    }

    /// <summary>
    /// Изменения проверки (<c>PATCH</c>): отправляются только заданные свойства. Новые правила или тесты —
    /// новая версия (<see cref="ValidatorUpdateResult.Bump"/>). Явное <c>Description = null</c> очищает описание.
    /// </summary>
    public sealed class ValidatorUpdateOptions
    {
        private string? _description;

        public string? Name { get; set; }
        /// <summary>Новая спецификация — как <see cref="ValidatorCreateOptions.Spec"/>.</summary>
        public object? Spec { get; set; }
        /// <summary>Полный новый список тестов (удалённый тест делает версию major).</summary>
        public IEnumerable<ValidatorTest>? Tests { get; set; }
        public string? Description
        {
            get => _description;
            set
            {
                _description = value;
                DescriptionSet = true;
            }
        }
        internal bool DescriptionSet { get; private set; }
        public string? Changelog { get; set; }
    }

    /// <summary>Что известно о будущей проверке (<c>client.Validators.SuggestAsync</c>): описание и/или примеры.</summary>
    public sealed class ValidatorSuggestOptions
    {
        /// <summary>Какие значения верные — словами.</summary>
        public string? Description { get; set; }
        /// <summary>Примеры, которые должны пройти и не должны.</summary>
        public ValidatorExamples? Examples { get; set; }
        /// <summary>Поле схемы, для которого нужна проверка.</summary>
        public ValidatorSuggestField? Field { get; set; }
    }

    /// <summary>Примеры для <see cref="ValidatorSuggestOptions"/>: строки или числа, до 50 каждого вида.</summary>
    public sealed class ValidatorExamples
    {
        /// <summary>Должны пройти.</summary>
        public IEnumerable<object>? Pass { get; set; }
        /// <summary>Не должны пройти.</summary>
        public IEnumerable<object>? Fail { get; set; }
    }

    /// <summary>Поле схемы для <see cref="ValidatorSuggestOptions"/>.</summary>
    public sealed class ValidatorSuggestField
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        /// <summary>Тип JSON Schema: <c>string</c>, <c>number</c>, …</summary>
        public string? Type { get; set; }
    }

    /// <summary>Период и группировка расхода.</summary>
    public sealed class UsageOptions
    {
        /// <summary>Первый день (дата без времени).</summary>
        public DateTime? From { get; set; }
        /// <summary>Последний день (дата без времени).</summary>
        public DateTime? To { get; set; }
        /// <summary><c>day</c> (по умолчанию), <c>typizer</c> или <c>key</c>.</summary>
        public string? GroupBy { get; set; }
    }
}
