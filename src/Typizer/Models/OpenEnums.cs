using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Typizer
{
    // API enumerations are open (SPEC §5.4): the API adds values without a new version, so each one is
    // a small struct around its string. An unknown value parses fine and keeps its text; compare with
    // the static members (`run.Status == RunStatus.Succeeded`) or with a string.

    /// <summary>Статус прогона: <c>queued</c>, <c>processing</c>, <c>succeeded</c>, <c>failed</c>.</summary>
    [JsonConverter(typeof(OpenEnumConverter<RunStatus>))]
    public readonly struct RunStatus : IEquatable<RunStatus>
    {
        private readonly string? _value;
        /// <summary>Значение как есть, в том числе неизвестное SDK.</summary>
        public RunStatus(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));
        public static RunStatus Queued { get; } = new RunStatus("queued");
        public static RunStatus Processing { get; } = new RunStatus("processing");
        public static RunStatus Succeeded { get; } = new RunStatus("succeeded");
        public static RunStatus Failed { get; } = new RunStatus("failed");
        /// <summary>Строковое значение из API.</summary>
        public string Value => _value ?? string.Empty;
        public static implicit operator RunStatus(string value) => new RunStatus(value);
        public static bool operator ==(RunStatus left, RunStatus right) => left.Equals(right);
        public static bool operator !=(RunStatus left, RunStatus right) => !left.Equals(right);
        public bool Equals(RunStatus other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is RunStatus other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
    }

    /// <summary>Режим распознавания: <c>standard</c> или <c>precise</c>.</summary>
    [JsonConverter(typeof(OpenEnumConverter<RunMode>))]
    public readonly struct RunMode : IEquatable<RunMode>
    {
        private readonly string? _value;
        public RunMode(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));
        public static RunMode Standard { get; } = new RunMode("standard");
        public static RunMode Precise { get; } = new RunMode("precise");
        public string Value => _value ?? string.Empty;
        public static implicit operator RunMode(string value) => new RunMode(value);
        public static bool operator ==(RunMode left, RunMode right) => left.Equals(right);
        public static bool operator !=(RunMode left, RunMode right) => !left.Equals(right);
        public bool Equals(RunMode other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is RunMode other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
    }

    /// <summary>Формат результата: <c>json</c>, <c>xml</c>, <c>csv</c>, <c>xlsx</c>.</summary>
    [JsonConverter(typeof(OpenEnumConverter<OutputFormat>))]
    public readonly struct OutputFormat : IEquatable<OutputFormat>
    {
        private readonly string? _value;
        public OutputFormat(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));
        public static OutputFormat Json { get; } = new OutputFormat("json");
        public static OutputFormat Xml { get; } = new OutputFormat("xml");
        public static OutputFormat Csv { get; } = new OutputFormat("csv");
        public static OutputFormat Xlsx { get; } = new OutputFormat("xlsx");
        public string Value => _value ?? string.Empty;
        public static implicit operator OutputFormat(string value) => new OutputFormat(value);
        public static bool operator ==(OutputFormat left, OutputFormat right) => left.Equals(right);
        public static bool operator !=(OutputFormat left, OutputFormat right) => !left.Equals(right);
        public bool Equals(OutputFormat other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is OutputFormat other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
    }

    /// <summary>Статус поля результата: <c>ok</c>, <c>warn</c>, <c>missing</c>.</summary>
    [JsonConverter(typeof(OpenEnumConverter<FieldStatus>))]
    public readonly struct FieldStatus : IEquatable<FieldStatus>
    {
        private readonly string? _value;
        public FieldStatus(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));
        /// <summary>Значение есть, проверки пройдены.</summary>
        public static FieldStatus Ok { get; } = new FieldStatus("ok");
        /// <summary>Значение есть, но проверка не пройдена (причина — в <see cref="FieldResult.Reason"/>).</summary>
        public static FieldStatus Warn { get; } = new FieldStatus("warn");
        /// <summary>Значения в документе нет.</summary>
        public static FieldStatus Missing { get; } = new FieldStatus("missing");
        public string Value => _value ?? string.Empty;
        public static implicit operator FieldStatus(string value) => new FieldStatus(value);
        public static bool operator ==(FieldStatus left, FieldStatus right) => left.Equals(right);
        public static bool operator !=(FieldStatus left, FieldStatus right) => !left.Equals(right);
        public bool Equals(FieldStatus other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is FieldStatus other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
    }

    /// <summary>Тип события вебхука: <c>run.succeeded</c>, <c>run.failed</c>, <c>batch.completed</c>, <c>balance.low</c>.</summary>
    [JsonConverter(typeof(OpenEnumConverter<WebhookEventType>))]
    public readonly struct WebhookEventType : IEquatable<WebhookEventType>
    {
        private readonly string? _value;
        public WebhookEventType(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));
        public static WebhookEventType RunSucceeded { get; } = new WebhookEventType("run.succeeded");
        public static WebhookEventType RunFailed { get; } = new WebhookEventType("run.failed");
        public static WebhookEventType BatchCompleted { get; } = new WebhookEventType("batch.completed");
        public static WebhookEventType BalanceLow { get; } = new WebhookEventType("balance.low");
        public string Value => _value ?? string.Empty;
        public static implicit operator WebhookEventType(string value) => new WebhookEventType(value);
        public static bool operator ==(WebhookEventType left, WebhookEventType right) => left.Equals(right);
        public static bool operator !=(WebhookEventType left, WebhookEventType right) => !left.Equals(right);
        public bool Equals(WebhookEventType other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is WebhookEventType other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
    }

    /// <summary>Статус пакета: <c>processing</c> или <c>completed</c>.</summary>
    [JsonConverter(typeof(OpenEnumConverter<BatchStatus>))]
    public readonly struct BatchStatus : IEquatable<BatchStatus>
    {
        private readonly string? _value;
        public BatchStatus(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));
        public static BatchStatus Processing { get; } = new BatchStatus("processing");
        public static BatchStatus Completed { get; } = new BatchStatus("completed");
        public string Value => _value ?? string.Empty;
        public static implicit operator BatchStatus(string value) => new BatchStatus(value);
        public static bool operator ==(BatchStatus left, BatchStatus right) => left.Equals(right);
        public static bool operator !=(BatchStatus left, BatchStatus right) => !left.Equals(right);
        public bool Equals(BatchStatus other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is BatchStatus other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
    }

    /// <summary>Статус конечной точки вебхуков: <c>active</c> или <c>disabled</c>.</summary>
    [JsonConverter(typeof(OpenEnumConverter<WebhookEndpointStatus>))]
    public readonly struct WebhookEndpointStatus : IEquatable<WebhookEndpointStatus>
    {
        private readonly string? _value;
        public WebhookEndpointStatus(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));
        public static WebhookEndpointStatus Active { get; } = new WebhookEndpointStatus("active");
        public static WebhookEndpointStatus Disabled { get; } = new WebhookEndpointStatus("disabled");
        public string Value => _value ?? string.Empty;
        public static implicit operator WebhookEndpointStatus(string value) => new WebhookEndpointStatus(value);
        public static bool operator ==(WebhookEndpointStatus left, WebhookEndpointStatus right) => left.Equals(right);
        public static bool operator !=(WebhookEndpointStatus left, WebhookEndpointStatus right) => !left.Equals(right);
        public bool Equals(WebhookEndpointStatus other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is WebhookEndpointStatus other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
    }

    /// <summary>Статус доставки вебхука: <c>pending</c>, <c>succeeded</c>, <c>failed</c>.</summary>
    [JsonConverter(typeof(OpenEnumConverter<WebhookDeliveryStatus>))]
    public readonly struct WebhookDeliveryStatus : IEquatable<WebhookDeliveryStatus>
    {
        private readonly string? _value;
        public WebhookDeliveryStatus(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));
        public static WebhookDeliveryStatus Pending { get; } = new WebhookDeliveryStatus("pending");
        public static WebhookDeliveryStatus Succeeded { get; } = new WebhookDeliveryStatus("succeeded");
        public static WebhookDeliveryStatus Failed { get; } = new WebhookDeliveryStatus("failed");
        public string Value => _value ?? string.Empty;
        public static implicit operator WebhookDeliveryStatus(string value) => new WebhookDeliveryStatus(value);
        public static bool operator ==(WebhookDeliveryStatus left, WebhookDeliveryStatus right) => left.Equals(right);
        public static bool operator !=(WebhookDeliveryStatus left, WebhookDeliveryStatus right) => !left.Equals(right);
        public bool Equals(WebhookDeliveryStatus other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is WebhookDeliveryStatus other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
    }

    /// <summary>Вид схемы каталога: <c>official</c> или <c>community</c>.</summary>
    [JsonConverter(typeof(OpenEnumConverter<CatalogKind>))]
    public readonly struct CatalogKind : IEquatable<CatalogKind>
    {
        private readonly string? _value;
        public CatalogKind(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));
        public static CatalogKind Official { get; } = new CatalogKind("official");
        public static CatalogKind Community { get; } = new CatalogKind("community");
        public string Value => _value ?? string.Empty;
        public static implicit operator CatalogKind(string value) => new CatalogKind(value);
        public static bool operator ==(CatalogKind left, CatalogKind right) => left.Equals(right);
        public static bool operator !=(CatalogKind left, CatalogKind right) => !left.Equals(right);
        public bool Equals(CatalogKind other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is CatalogKind other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
    }

    /// <summary>Раздел каталога: <c>invoices</c>, <c>bank</c>, <c>shipping</c>, <c>contracts</c>, <c>registries</c>, <c>hr</c>, <c>identity</c>.</summary>
    [JsonConverter(typeof(OpenEnumConverter<CatalogCategory>))]
    public readonly struct CatalogCategory : IEquatable<CatalogCategory>
    {
        private readonly string? _value;
        public CatalogCategory(string value) => _value = value ?? throw new ArgumentNullException(nameof(value));
        public static CatalogCategory Invoices { get; } = new CatalogCategory("invoices");
        public static CatalogCategory Bank { get; } = new CatalogCategory("bank");
        public static CatalogCategory Shipping { get; } = new CatalogCategory("shipping");
        public static CatalogCategory Contracts { get; } = new CatalogCategory("contracts");
        public static CatalogCategory Registries { get; } = new CatalogCategory("registries");
        public static CatalogCategory Hr { get; } = new CatalogCategory("hr");
        public static CatalogCategory Identity { get; } = new CatalogCategory("identity");
        public string Value => _value ?? string.Empty;
        public static implicit operator CatalogCategory(string value) => new CatalogCategory(value);
        public static bool operator ==(CatalogCategory left, CatalogCategory right) => left.Equals(right);
        public static bool operator !=(CatalogCategory left, CatalogCategory right) => !left.Equals(right);
        public bool Equals(CatalogCategory other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is CatalogCategory other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
    }

    /// <summary>JSON ⇄ open enum struct: any string in, the same string out; never throws on new values.</summary>
    internal sealed class OpenEnumConverter<T> : JsonConverter<T> where T : struct
    {
        private static readonly Func<string, T> Create = BuildFactory();

        private static Func<string, T> BuildFactory()
        {
            ConstructorInfo ctor = typeof(T).GetConstructor(new[] { typeof(string) })
                ?? throw new InvalidOperationException(typeof(T).Name + " needs a (string) constructor");
            return value => (T)ctor.Invoke(new object[] { value });
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return Create(reader.GetString() ?? string.Empty);
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                    using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
                    {
                        return Create(doc.RootElement.GetRawText());
                    }
                default:
                    throw new JsonException("Expected a string for " + typeof(T).Name);
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
