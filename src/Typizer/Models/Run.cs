using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Typizer.Internal;

namespace Typizer
{
    /// <summary>
    /// Прогон — один документ, распознанный по схеме (<c>GET /v1/runs/{id}</c>). Один и тот же тип
    /// возвращают <see cref="TypizerClient.ExtractAsync(TypizerFile, object, System.Threading.CancellationToken)"/>,
    /// <c>client.Runs.GetAsync</c> и вебхук (<see cref="WebhookEvent.Run"/>).
    /// </summary>
    public sealed class Run
    {
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = "run";
        public RunStatus Status { get; set; }
        public string? TypizerId { get; set; }
        public int? TypizerVersion { get; set; }
        public RunMode Mode { get; set; }
        public OutputFormat Format { get; set; }
        /// <summary>Откуда создан прогон: <c>api</c>, <c>my</c>, <c>mcp</c>, <c>demo</c>, <c>agent</c>.</summary>
        public string Source { get; set; } = string.Empty;
        public RunPages Pages { get; set; } = new RunPages();

        /// <summary>
        /// Результат по вашей схеме — значение JSON как есть (ключи и порядок — как в схеме).
        /// <c>null</c>, пока прогон не завершился успешно, у элементов списка
        /// (<c>Runs.ListAsync</c> его не отдаёт) и после удаления по сроку хранения
        /// (<see cref="OutputExpired"/>). Разобрать в свой тип — <see cref="GetOutput{T}"/>.
        /// </summary>
        public JsonElement? Output { get; set; }

        /// <summary>
        /// Проверка каждого конечного поля результата (<c>seller.inn</c>, <c>items[0].amount</c>) в
        /// порядке результата. <c>null</c> — в тех же случаях, что и <see cref="Output"/>.
        /// </summary>
        public IReadOnlyDictionary<string, FieldResult>? Fields { get; set; }

        /// <summary>Результат удалён по сроку хранения организации.</summary>
        public bool OutputExpired { get; set; }
        public RunError? Error { get; set; }
        public string FileId { get; set; } = string.Empty;
        public RunFile? File { get; set; }
        public string? BatchId { get; set; }
        public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        public RunCatalog? Catalog { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }

        /// <summary>Поля ответа, которых нет в этой версии SDK.</summary>
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }

        /// <summary>Прогон завершён: <c>succeeded</c> или <c>failed</c>. Неизвестный SDK статус — не конечный.</summary>
        [JsonIgnore]
        public bool IsTerminal => Status == RunStatus.Succeeded || Status == RunStatus.Failed;

        /// <summary>Прогон успешен, но хотя бы одно поле не прошло проверку или не найдено — его стоит показать человеку.</summary>
        [JsonIgnore]
        public bool NeedsReview
        {
            get
            {
                if (Status != RunStatus.Succeeded || Fields == null) return false;
                foreach (KeyValuePair<string, FieldResult> field in Fields)
                {
                    if (field.Value != null && field.Value.Status != FieldStatus.Ok) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Поля со статусом <c>warn</c> или <c>missing</c> — путь и проверка, в порядке <see cref="Fields"/>.
        /// </summary>
        public IReadOnlyList<(string Path, FieldResult Field)> FieldsToReview()
        {
            var result = new List<(string, FieldResult)>();
            if (Fields == null) return result;
            foreach (KeyValuePair<string, FieldResult> field in Fields)
            {
                if (field.Value == null) continue;
                if (field.Value.Status == FieldStatus.Warn || field.Value.Status == FieldStatus.Missing)
                    result.Add((field.Key, field.Value));
            }
            return result;
        }

        /// <summary>
        /// Разбирает <see cref="Output"/> в ваш тип. По умолчанию имена свойств сопоставляются в
        /// snake_case (<c>InvoiceNumber</c> ⇄ <c>invoice_number</c>) без учёта регистра; свои правила —
        /// через <paramref name="options"/> или <c>[JsonPropertyName]</c>. Возвращает <c>default</c>, если
        /// результата нет.
        /// </summary>
        public T? GetOutput<T>(JsonSerializerOptions? options = null)
        {
            if (Output == null || Output.Value.ValueKind == JsonValueKind.Null || Output.Value.ValueKind == JsonValueKind.Undefined)
                return default;
            return Output.Value.Deserialize<T>(options ?? Json.OutputOptions);
        }

        public override string ToString() => "Run(" + Id + ", " + Status + ")";
    }

    /// <summary>Страницы прогона: в документе и к оплате.</summary>
    public sealed class RunPages
    {
        public int? Physical { get; set; }
        public int? Billed { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Ошибка прогона, завершившегося <c>failed</c>.</summary>
    public sealed class RunError
    {
        /// <summary><c>document_unreadable</c>, <c>too_many_pages</c>, <c>extraction_failed</c>, <c>upstream_unavailable</c>, <c>internal_error</c>, <c>timeout</c>, <c>insufficient_balance</c> и другие.</summary>
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Документ прогона.</summary>
    public sealed class RunFile
    {
        public string Filename { get; set; } = string.Empty;
        public string Mime { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Схема каталога, по которой шёл прогон.</summary>
    public sealed class RunCatalog
    {
        public string EntryId { get; set; } = string.Empty;
        public string Ref { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>
    /// Проверка одного поля результата. Числовой «уверенности» API не отдаёт: вместо неё — статус,
    /// причина и места в документе, где найдено значение.
    /// </summary>
    public sealed class FieldResult
    {
        public FieldStatus Status { get; set; }
        /// <summary>Только при <c>warn</c>: <c>checksum_mismatch</c>, <c>invalid_format</c>, <c>sum_mismatch</c>, <c>implausible_date</c>, <c>no_source</c>, <c>code_mismatch</c> (значение расходится со штрихкодом или QR-кодом на документе) и другие.</summary>
        public string? Reason { get; set; }
        public IReadOnlyList<FieldSource> Sources { get; set; } = Array.Empty<FieldSource>();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Где в документе найдено значение.</summary>
    public sealed class FieldSource
    {
        /// <summary>Страница, с 1.</summary>
        public int Page { get; set; }
        /// <summary><c>[x0, y0, x1, y1]</c> в долях страницы от левого верхнего угла, или <c>null</c>.</summary>
        public IReadOnlyList<double>? Bbox { get; set; }
        /// <summary>Исходный текст документа.</summary>
        public string? Text { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}
