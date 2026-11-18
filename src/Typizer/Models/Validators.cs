using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Typizer
{
    // Field checks («проверки», /v1/validators). A validator's `spec` stays free JSON (JsonElement): rule
    // kinds are added without a new API version, so the SDK never closes it into types (SPEC §3).

    /// <summary>
    /// Своя проверка организации (<c>my/contract-no</c>) — схема <c>Validator</c> справочника API. На неё
    /// ссылаются из схемы: <c>"x-typizer-check": "my/contract-no@1"</c>.
    /// </summary>
    public class Validator
    {
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = "validator";
        /// <summary>Ссылка для <c>x-typizer-check</c> без версии: <c>my/contract-no</c>.</summary>
        public string Ref { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        /// <summary>Текущая (последняя) версия.</summary>
        public ValidatorVersion Version { get; set; } = new ValidatorVersion();
        public int VersionsCount { get; set; }
        /// <summary>Публикация в каталоге проверок, или <c>null</c>.</summary>
        public ValidatorCatalog? Catalog { get; set; }
        /// <summary>Откуда скопирована, или <c>null</c>.</summary>
        public ValidatorForkedFrom? ForkedFrom { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
        public override string ToString() => "Validator(" + Id + ", " + Ref + ")";
    }

    /// <summary>
    /// Проверка со всеми версиями (новые первыми) и типизаторами, которые на неё ссылаются —
    /// <c>client.Validators.GetAsync</c>.
    /// </summary>
    public sealed class ValidatorDetail : Validator
    {
        /// <summary>Все версии, новые первыми.</summary>
        public IReadOnlyList<ValidatorVersion> Versions { get; set; } = Array.Empty<ValidatorVersion>();
        /// <summary>Типизаторы, которые ссылаются на проверку; пока список не пуст, в архив её не убрать.</summary>
        public IReadOnlyList<ValidatorUsedBy> UsedBy { get; set; } = Array.Empty<ValidatorUsedBy>();
    }

    /// <summary>Публикация проверки в каталоге.</summary>
    public sealed class ValidatorCatalog
    {
        /// <summary><c>cat_…</c></summary>
        public string EntryId { get; set; } = string.Empty;
        /// <summary>Ссылка в каталоге: другие организации пишут её в <c>x-typizer-check</c> с <c>@1</c>.</summary>
        public string Ref { get; set; } = string.Empty;
        /// <summary><c>private</c>, <c>unlisted</c>, <c>public</c>.</summary>
        public string Visibility { get; set; } = string.Empty;
        /// <summary><c>none</c>, <c>pending</c>, <c>approved</c>, <c>rejected</c>.</summary>
        public string ReviewState { get; set; } = string.Empty;
        public bool Hidden { get; set; }
        /// <summary>Последняя опубликованная версия.</summary>
        public string? Version { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Проверка, с которой снята копия.</summary>
    public sealed class ValidatorForkedFrom
    {
        public string Ref { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Версия проверки (SemVer): спецификация, тесты и её описание словами.</summary>
    public sealed class ValidatorVersion
    {
        public string Id { get; set; } = string.Empty;
        public string Object { get; set; } = "validator_version";
        /// <summary><c>1.0.0</c>, <c>1.1.0</c>, …</summary>
        public string Version { get; set; } = string.Empty;
        /// <summary>Спецификация проверки — объект JSON как есть (<c>{"kind": "rules", "rules": […], "message": "…"}</c>).</summary>
        public JsonElement Spec { get; set; }
        /// <summary>Тесты версии — её контракт: каждый обязан выполняться.</summary>
        public IReadOnlyList<ValidatorTest> Tests { get; set; } = Array.Empty<ValidatorTest>();
        /// <summary>Что проверяет — словами, по-русски.</summary>
        public string Explain { get; set; } = string.Empty;
        /// <summary>Короткие ярлыки правил: <c>шаблон</c>, <c>длина</c>, …</summary>
        public IReadOnlyList<string> RulePreview { get; set; } = Array.Empty<string>();
        public string Changelog { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
        public override string ToString() => "ValidatorVersion(" + Version + ")";
    }

    /// <summary>
    /// Тест проверки: значение (строка или число) и ожидаемый итог. Удобнее всего —
    /// <see cref="Pass(string, string?)"/> и <see cref="Fail(string, string?)"/>.
    /// </summary>
    public sealed class ValidatorTest
    {
        public ValidatorTest() { }

        /// <summary>Тест со строковым значением.</summary>
        public ValidatorTest(string value, ValidatorOutcome expect, string? note = null)
        {
            Value = JsonSerializer.SerializeToElement(value ?? throw new ArgumentNullException(nameof(value)));
            Expect = expect;
            Note = note;
        }

        /// <summary>Тест с числовым значением.</summary>
        public ValidatorTest(decimal value, ValidatorOutcome expect, string? note = null)
        {
            Value = JsonSerializer.SerializeToElement(value);
            Expect = expect;
            Note = note;
        }

        /// <summary>Значение должно пройти проверку.</summary>
        public static ValidatorTest Pass(string value, string? note = null) => new ValidatorTest(value, ValidatorOutcome.Pass, note);

        /// <summary>Значение не должно пройти проверку.</summary>
        public static ValidatorTest Fail(string value, string? note = null) => new ValidatorTest(value, ValidatorOutcome.Fail, note);

        /// <summary>Значение — строка или число JSON.</summary>
        public JsonElement Value { get; set; }
        /// <summary><c>pass</c> или <c>fail</c>.</summary>
        public ValidatorOutcome Expect { get; set; }
        public string? Note { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Типизатор, который ссылается на проверку.</summary>
    public sealed class ValidatorUsedBy
    {
        public string TypizerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>Версия типизатора.</summary>
        public int Version { get; set; }
        /// <summary>Ссылка, как она написана в схеме: <c>my/contract-no@1</c>.</summary>
        public string Ref { get; set; } = string.Empty;
        /// <summary>Версия проверки, на которой зафиксирован типизатор.</summary>
        public string LockedVersion { get; set; } = string.Empty;
        /// <summary>Более новая совместимая версия проверки, или <c>null</c>.</summary>
        public string? UpdateAvailable { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>
    /// Итог <c>client.Validators.UpdateAsync</c>: проверка, какой стала версия (<see cref="Bump"/>) и
    /// почему, и куда изменение разошлось.
    /// </summary>
    public sealed class ValidatorUpdateResult
    {
        public string Object { get; set; } = "validator_update";
        public Validator Validator { get; set; } = new Validator();
        /// <summary><c>major</c>, <c>minor</c>, <c>patch</c> или <c>none</c>.</summary>
        public ValidatorBump Bump { get; set; }
        /// <summary>Почему версия стала такой — по-русски.</summary>
        public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
        /// <summary>Типизаторы, автоматически переведённые на новую совместимую версию.</summary>
        public IReadOnlyList<ValidatorPropagation> PropagatedTo { get; set; } = Array.Empty<ValidatorPropagation>();
        /// <summary>Типизаторы (id), которым новую версию предложено принять вручную.</summary>
        public IReadOnlyList<string> SuggestedTo { get; set; } = Array.Empty<string>();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Типизатор и его новая версия после обновления проверки.</summary>
    public sealed class ValidatorPropagation
    {
        public string TypizerId { get; set; } = string.Empty;
        public int Version { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Итог <c>client.Validators.TestAsync</c>: описание проверки и итог по каждому значению.</summary>
    public sealed class ValidatorTestResult
    {
        public string Object { get; set; } = "validator_test";
        /// <summary>Что проверяет — словами, по-русски.</summary>
        public string Explain { get; set; } = string.Empty;
        /// <summary>По значению на каждое переданное, в том же порядке.</summary>
        public IReadOnlyList<ValidatorTestValueResult> Results { get; set; } = Array.Empty<ValidatorTestValueResult>();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Итог проверки одного значения.</summary>
    public sealed class ValidatorTestValueResult
    {
        /// <summary>Значение, как его передали (строка или число JSON).</summary>
        public JsonElement Value { get; set; }
        public bool Pass { get; set; }
        /// <summary>При провале: <c>pattern_mismatch</c>, <c>invalid_format</c>, <c>checksum_mismatch</c>, …; иначе <c>null</c>.</summary>
        public string? Reason { get; set; }
        /// <summary>При провале — фраза по-русски, которую увидит человек в предупреждении поля.</summary>
        public string? Message { get; set; }
        /// <summary>Номер правила (с 0), на котором значение не прошло, или <c>null</c>.</summary>
        public int? RuleIndex { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>
    /// Предложение проверки от <c>client.Validators.SuggestAsync</c> (не сохраняется): спецификация,
    /// прогон примеров и похожие готовые проверки.
    /// </summary>
    public sealed class ValidatorSuggestion
    {
        public string Object { get; set; } = "validator_suggestion";
        public string Name { get; set; } = string.Empty;
        /// <summary>Спецификация — объект JSON как есть; её можно сразу передать в <c>Validators.CreateAsync</c>.</summary>
        public JsonElement Spec { get; set; }
        /// <summary>Примеры и что с ними сделала предложенная спецификация.</summary>
        public IReadOnlyList<ValidatorSuggestionTest> Tests { get; set; } = Array.Empty<ValidatorSuggestionTest>();
        /// <summary>Все примеры дают ожидаемый итог.</summary>
        public bool Converged { get; set; }
        public string Explain { get; set; } = string.Empty;
        public IReadOnlyList<string> RulePreview { get; set; } = Array.Empty<string>();
        public int Iterations { get; set; }
        /// <summary>Откуда предложение: <c>catalog</c>, <c>shape</c> (по форме примеров) или <c>model</c>.</summary>
        public string Source { get; set; } = string.Empty;
        /// <summary>Готовые проверки, которые подходят к примерам: официальные и свои.</summary>
        public IReadOnlyList<ValidatorCandidate> CandidatesFromCatalog { get; set; } = Array.Empty<ValidatorCandidate>();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Пример из запроса <c>SuggestAsync</c>: ожидаемый и полученный итог.</summary>
    public sealed class ValidatorSuggestionTest
    {
        public JsonElement Value { get; set; }
        public ValidatorOutcome Expect { get; set; }
        public ValidatorOutcome Got { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Готовая проверка, подходящая к примерам: ссылка для <c>x-typizer-check</c> и описание.</summary>
    public sealed class ValidatorCandidate
    {
        /// <summary><c>inn@1</c>, <c>my/contract-no@1</c>, …</summary>
        public string Ref { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary><c>official</c> или <c>own</c>.</summary>
        public string Kind { get; set; } = string.Empty;
        public string Explain { get; set; } = string.Empty;
        /// <summary>Сколько примеров проверка решает верно, или <c>null</c>.</summary>
        public ValidatorCandidateExamples? Examples { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Сколько примеров из запроса готовая проверка решает верно.</summary>
    public sealed class ValidatorCandidateExamples
    {
        public int Correct { get; set; }
        public int Total { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}
