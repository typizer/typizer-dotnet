using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Typizer.Internal
{
    internal static class Json
    {
        /// <summary>SDK models: PascalCase properties ⇄ snake_case JSON, unknown fields into ExtensionData.</summary>
        public static readonly JsonSerializerOptions ModelOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary><see cref="Run.GetOutput{T}"/> default: snake_case keys of the user's schema ⇄ PascalCase properties.</summary>
        public static readonly JsonSerializerOptions OutputOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        /// <summary>A user's schema object as written: names untouched, order kept.</summary>
        private static readonly JsonSerializerOptions SchemaOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static T Deserialize<T>(byte[] body)
        {
            T? value = JsonSerializer.Deserialize<T>(body, ModelOptions);
            if (value == null) throw new JsonException("The response body is JSON null");
            return value;
        }

        /// <summary>
        /// A schema as the API wants it — a JSON object, with its property order kept (columns and XML
        /// elements follow it). Accepts JSON text, JsonElement/JsonDocument/JsonNode, dictionaries,
        /// anonymous objects and POCOs.
        /// </summary>
        public static JsonElement ToSchema(object schema, string paramName) =>
            ToObject(schema, paramName, "Схема должна быть объектом JSON Schema ({\"type\": \"object\", \"properties\": {…}}), а не ",
                "Схема — это JSON Schema (объект или JSON-текст). Для схемы из каталога задайте ExtractOptions.Catalog, " +
                "для сохранённого типизатора — ExtractOptions.Typizer. Текст не разобрался как JSON: ");

        /// <summary>
        /// A validator spec (<c>{"kind": "rules", …}</c>) as is: a free JSON object, never closed into
        /// types (SPEC §3). The same inputs as <see cref="ToSchema"/>.
        /// </summary>
        public static JsonElement ToSpec(object spec, string paramName) =>
            ToObject(spec, paramName, "Спецификация проверки должна быть объектом JSON ({\"kind\": \"rules\", …}), а не ",
                "Спецификация проверки — объект JSON или JSON-текст. Текст не разобрался как JSON: ");

        private static JsonElement ToObject(object value, string paramName, string notObject, string badText)
        {
            if (value == null) throw new ArgumentNullException(paramName);
            JsonElement element;
            switch (value)
            {
                case JsonElement e:
                    element = e;
                    break;
                case JsonDocument d:
                    element = d.RootElement;
                    break;
                case JsonNode n:
                    element = JsonSerializer.SerializeToElement(n, SchemaOptions);
                    break;
                case string text:
                    try
                    {
                        using (JsonDocument doc = JsonDocument.Parse(text))
                        {
                            element = doc.RootElement.Clone();
                        }
                    }
                    catch (JsonException ex)
                    {
                        throw new ArgumentException(badText + ex.Message, paramName, ex);
                    }
                    break;
                default:
                    element = JsonSerializer.SerializeToElement(value, value.GetType(), SchemaOptions);
                    break;
            }
            if (element.ValueKind != JsonValueKind.Object)
                throw new ArgumentException(notObject + element.ValueKind.ToString().ToLowerInvariant() + ".", paramName);
            return element.Clone();
        }

        /// <summary>
        /// A value a validator checks — a string or a number (<c>int</c>, <c>long</c>, <c>decimal</c>,
        /// <c>double</c>, …, or a <see cref="JsonElement"/> holding one); anything else is an argument error.
        /// </summary>
        public static void WriteCheckValue(Utf8JsonWriter writer, object? value, string paramName)
        {
            switch (value)
            {
                case string s: writer.WriteStringValue(s); return;
                case int i: writer.WriteNumberValue(i); return;
                case long l: writer.WriteNumberValue(l); return;
                case short sh: writer.WriteNumberValue(sh); return;
                case byte b: writer.WriteNumberValue(b); return;
                case sbyte sb: writer.WriteNumberValue(sb); return;
                case ushort us: writer.WriteNumberValue(us); return;
                case uint ui: writer.WriteNumberValue(ui); return;
                case ulong ul: writer.WriteNumberValue(ul); return;
                case decimal m: writer.WriteNumberValue(m); return;
                case double d when !double.IsNaN(d) && !double.IsInfinity(d): writer.WriteNumberValue(d); return;
                case float f when !float.IsNaN(f) && !float.IsInfinity(f): writer.WriteNumberValue(f); return;
                case JsonElement e when e.ValueKind == JsonValueKind.String || e.ValueKind == JsonValueKind.Number: e.WriteTo(writer); return;
                default:
                    throw new ArgumentException("Значение для проверки — строка или конечное число, а не " +
                        (value == null ? "null" : value.GetType().Name) + ".", paramName);
            }
        }

        /// <summary>Builds a JSON request body with only the properties that were set.</summary>
        public static byte[] WriteObject(Action<Utf8JsonWriter> write)
        {
            using (var buffer = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
                {
                    writer.WriteStartObject();
                    write(writer);
                    writer.WriteEndObject();
                }
                return buffer.ToArray();
            }
        }

        public static string ToText(JsonElement element)
        {
            using (var buffer = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
                {
                    element.WriteTo(writer);
                }
                return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }

        public static string MetadataText(IEnumerable<KeyValuePair<string, string>> metadata) =>
            Encoding.UTF8.GetString(WriteObject(w => WriteMetadataProperties(w, metadata)));

        public static void WriteMetadata(Utf8JsonWriter writer, IEnumerable<KeyValuePair<string, string>>? metadata)
        {
            if (metadata == null) return;
            writer.WritePropertyName("metadata");
            writer.WriteStartObject();
            WriteMetadataProperties(writer, metadata);
            writer.WriteEndObject();
        }

        private static void WriteMetadataProperties(Utf8JsonWriter writer, IEnumerable<KeyValuePair<string, string>> metadata)
        {
            foreach (KeyValuePair<string, string> pair in metadata)
            {
                if (pair.Key == null) throw new ArgumentException("metadata: ключ не может быть null");
                writer.WriteString(pair.Key, pair.Value ?? throw new ArgumentException("metadata: значение «" + pair.Key + "» не может быть null"));
            }
        }

        public static void WriteStringIf(Utf8JsonWriter writer, string name, string? value)
        {
            if (value != null) writer.WriteString(name, value);
        }
    }

    /// <summary><c>"2026-11-01"</c> ⇄ <see cref="DateTime"/> (the same type on every target framework).</summary>
    internal sealed class DateOnlyJsonConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string text = reader.GetString() ?? throw new JsonException("Expected a date");
            if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                return date;
            return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal).Date;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
