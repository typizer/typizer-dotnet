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
        public static JsonElement ToSchema(object schema, string paramName)
        {
            if (schema == null) throw new ArgumentNullException(paramName);
            JsonElement element;
            switch (schema)
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
                        throw new ArgumentException(
                            "Схема — это JSON Schema (объект или JSON-текст). Для схемы из каталога задайте ExtractOptions.Catalog, " +
                            "для сохранённого типизатора — ExtractOptions.Typizer. Текст не разобрался как JSON: " + ex.Message,
                            paramName, ex);
                    }
                    break;
                default:
                    element = JsonSerializer.SerializeToElement(schema, schema.GetType(), SchemaOptions);
                    break;
            }
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    "Схема должна быть объектом JSON Schema ({\"type\": \"object\", \"properties\": {…}}), а не " +
                    element.ValueKind.ToString().ToLowerInvariant() + ".", paramName);
            }
            return element.Clone();
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
