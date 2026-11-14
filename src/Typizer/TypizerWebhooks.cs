using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Typizer.Internal;

namespace Typizer
{
    /// <summary>
    /// Проверка подписи вебхуков Typizer (<see href="https://www.standardwebhooks.com">Standard Webhooks</see>):
    /// заголовки <c>webhook-id</c>, <c>webhook-timestamp</c>, <c>webhook-signature</c>, HMAC-SHA256 на
    /// секрете <c>whsec_…</c>, допуск по времени — 5 минут. Клиент и ключ API не нужны.
    /// </summary>
    /// <remarks>
    /// <c>payload</c> — тело ровно в тех байтах, в которых оно пришло: не пересобирайте его из
    /// разобранного JSON. Заголовки — словарь, <c>IHeaderDictionary</c> ASP.NET Core,
    /// <c>HttpHeaders</c> или <see cref="NameValueCollection"/> (ASP.NET на .NET Framework); имена без
    /// учёта регистра.
    /// </remarks>
    public static class TypizerWebhooks
    {
        /// <summary>Допуск по времени по умолчанию: 300 с в обе стороны.</summary>
        public static readonly TimeSpan DefaultTolerance = TimeSpan.FromSeconds(300);

        private static readonly Regex TimestampPattern = new Regex(@"^\d{1,15}$", RegexOptions.CultureInvariant);

        /// <summary>Проверяет подпись и время и возвращает событие; иначе — <see cref="TypizerWebhookVerificationException"/>.</summary>
        public static WebhookEvent Verify(string payload, IEnumerable<KeyValuePair<string, string>> headers, string secret,
            TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
            VerifyCore(Utf8(payload), Lookup(headers), secret, tolerance, now);

        /// <inheritdoc cref="Verify(string, IEnumerable{KeyValuePair{string, string}}, string, TimeSpan?, DateTimeOffset?)"/>
        public static WebhookEvent Verify(byte[] payload, IEnumerable<KeyValuePair<string, string>> headers, string secret,
            TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
            VerifyCore(Require.NotNull(payload, nameof(payload)), Lookup(headers), secret, tolerance, now);

        /// <summary>
        /// Проверяет подпись; заголовки с несколькими значениями: <c>IHeaderDictionary</c> (ASP.NET Core),
        /// <c>HttpHeaders</c>, <c>IDictionary&lt;string, string[]&gt;</c>.
        /// </summary>
        public static WebhookEvent Verify<TValues>(string payload, IEnumerable<KeyValuePair<string, TValues>> headers, string secret,
            TimeSpan? tolerance = null, DateTimeOffset? now = null) where TValues : IEnumerable<string?> =>
            VerifyCore(Utf8(payload), Lookup(headers), secret, tolerance, now);

        /// <inheritdoc cref="Verify{TValues}(string, IEnumerable{KeyValuePair{string, TValues}}, string, TimeSpan?, DateTimeOffset?)"/>
        public static WebhookEvent Verify<TValues>(byte[] payload, IEnumerable<KeyValuePair<string, TValues>> headers, string secret,
            TimeSpan? tolerance = null, DateTimeOffset? now = null) where TValues : IEnumerable<string?> =>
            VerifyCore(Require.NotNull(payload, nameof(payload)), Lookup(headers), secret, tolerance, now);

        /// <summary>Проверяет подпись; заголовки ASP.NET на .NET Framework (<c>Request.Headers</c>).</summary>
        public static WebhookEvent Verify(string payload, NameValueCollection headers, string secret,
            TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
            VerifyCore(Utf8(payload), Lookup(headers), secret, tolerance, now);

        /// <inheritdoc cref="Verify(string, NameValueCollection, string, TimeSpan?, DateTimeOffset?)"/>
        public static WebhookEvent Verify(byte[] payload, NameValueCollection headers, string secret,
            TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
            VerifyCore(Require.NotNull(payload, nameof(payload)), Lookup(headers), secret, tolerance, now);

        /// <summary>То же, что <c>Verify</c>, но без исключения: <c>true</c>, если подпись и время верны.</summary>
        public static bool IsValid(string payload, IEnumerable<KeyValuePair<string, string>> headers, string secret,
            TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
            Check(() => Verify(payload, headers, secret, tolerance, now));

        /// <inheritdoc cref="IsValid(string, IEnumerable{KeyValuePair{string, string}}, string, TimeSpan?, DateTimeOffset?)"/>
        public static bool IsValid(byte[] payload, IEnumerable<KeyValuePair<string, string>> headers, string secret,
            TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
            Check(() => Verify(payload, headers, secret, tolerance, now));

        /// <inheritdoc cref="IsValid(string, IEnumerable{KeyValuePair{string, string}}, string, TimeSpan?, DateTimeOffset?)"/>
        public static bool IsValid<TValues>(string payload, IEnumerable<KeyValuePair<string, TValues>> headers, string secret,
            TimeSpan? tolerance = null, DateTimeOffset? now = null) where TValues : IEnumerable<string?> =>
            Check(() => Verify(payload, headers, secret, tolerance, now));

        /// <inheritdoc cref="IsValid(string, IEnumerable{KeyValuePair{string, string}}, string, TimeSpan?, DateTimeOffset?)"/>
        public static bool IsValid<TValues>(byte[] payload, IEnumerable<KeyValuePair<string, TValues>> headers, string secret,
            TimeSpan? tolerance = null, DateTimeOffset? now = null) where TValues : IEnumerable<string?> =>
            Check(() => Verify(payload, headers, secret, tolerance, now));

        /// <inheritdoc cref="IsValid(string, IEnumerable{KeyValuePair{string, string}}, string, TimeSpan?, DateTimeOffset?)"/>
        public static bool IsValid(string payload, NameValueCollection headers, string secret,
            TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
            Check(() => Verify(payload, headers, secret, tolerance, now));

        /// <inheritdoc cref="IsValid(string, IEnumerable{KeyValuePair{string, string}}, string, TimeSpan?, DateTimeOffset?)"/>
        public static bool IsValid(byte[] payload, NameValueCollection headers, string secret,
            TimeSpan? tolerance = null, DateTimeOffset? now = null) =>
            Check(() => Verify(payload, headers, secret, tolerance, now));

        private static bool Check(Func<WebhookEvent> verify)
        {
            try
            {
                verify();
                return true;
            }
            catch (TypizerWebhookVerificationException)
            {
                return false;
            }
        }

        private static byte[] Utf8(string payload) => Encoding.UTF8.GetBytes(Require.NotNull(payload, nameof(payload)));

        private static Func<string, string?> Lookup(IEnumerable<KeyValuePair<string, string>> headers)
        {
            Require.NotNull(headers, nameof(headers));
            return name =>
            {
                foreach (KeyValuePair<string, string> h in headers)
                {
                    if (string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)) return h.Value;
                }
                return null;
            };
        }

        private static Func<string, string?> Lookup<TValues>(IEnumerable<KeyValuePair<string, TValues>> headers) where TValues : IEnumerable<string?>
        {
            Require.NotNull(headers, nameof(headers));
            return name =>
            {
                foreach (KeyValuePair<string, TValues> h in headers)
                {
                    if (string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase))
                        return h.Value == null ? null : h.Value.FirstOrDefault();
                }
                return null;
            };
        }

        private static Func<string, string?> Lookup(NameValueCollection headers)
        {
            Require.NotNull(headers, nameof(headers));
            return name => headers.GetValues(name)?.FirstOrDefault();
        }

        /// <summary>SPEC §9.2, step by step.</summary>
        private static WebhookEvent VerifyCore(byte[] payload, Func<string, string?> header, string secret, TimeSpan? tolerance, DateTimeOffset? now)
        {
            if (secret == null) throw new ArgumentNullException(nameof(secret));
            string? id = header("webhook-id");
            string? timestamp = header("webhook-timestamp");
            string? signatures = header("webhook-signature");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signatures))
                throw Fail("missing_header", "A webhook-id, webhook-timestamp or webhook-signature header is missing");

            if (!TimestampPattern.IsMatch(timestamp!))
                throw Fail("invalid_timestamp", "webhook-timestamp is not Unix seconds");
            long ts = long.Parse(timestamp!, NumberStyles.None, CultureInfo.InvariantCulture);
            long current = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            double allowed = (tolerance ?? DefaultTolerance).TotalSeconds;
            if (Math.Abs((double)(current - ts)) > allowed)
                throw Fail("timestamp_out_of_tolerance", "webhook-timestamp is too far from now: a replay or a skewed clock");

            byte[] key = DecodeSecret(secret);
            byte[] expected;
            using (var hmac = new HMACSHA256(key))
            {
                byte[] prefix = Encoding.UTF8.GetBytes(id + "." + timestamp + ".");
                byte[] signed = new byte[prefix.Length + payload.Length];
                Buffer.BlockCopy(prefix, 0, signed, 0, prefix.Length);
                Buffer.BlockCopy(payload, 0, signed, prefix.Length, payload.Length);
                expected = hmac.ComputeHash(signed);
            }

            bool matched = false;
            foreach (string entry in signatures!.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int comma = entry.IndexOf(',');
                if (comma < 0 || entry.Substring(0, comma) != "v1") continue;
                byte[] candidate;
                try
                {
                    candidate = Convert.FromBase64String(entry.Substring(comma + 1));
                }
                catch (FormatException)
                {
                    continue;
                }
                // Every entry is compared, so timing does not tell which one matched.
                matched |= FixedTimeEquals(candidate, expected);
            }
            if (!matched)
                throw Fail("signature_mismatch", "No v1 signature matches: wrong secret, or the body or headers were changed");

            try
            {
                return JsonSerializer.Deserialize<WebhookEvent>(payload, Json.ModelOptions)
                       ?? throw Fail("invalid_payload", "The webhook body is JSON null");
            }
            catch (JsonException ex)
            {
                throw Fail("invalid_payload", "The webhook body is not a Typizer event: " + ex.Message);
            }
        }

        private static byte[] DecodeSecret(string secret)
        {
            string value = secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret.Substring(6) : secret;
            byte[] key;
            try
            {
                key = Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                throw Fail("invalid_secret", "The webhook secret is not whsec_<base64>");
            }
            if (key.Length < 24) throw Fail("invalid_secret", "The webhook secret is shorter than 24 bytes");
            return key;
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
#if NET8_0_OR_GREATER
            return CryptographicOperations.FixedTimeEquals(a, b);
#else
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
#endif
        }

        private static TypizerWebhookVerificationException Fail(string reason, string message) =>
            new TypizerWebhookVerificationException(reason, message);
    }

    /// <summary>
    /// Событие вебхука: <c>id</c> (<c>evt_…</c>, он же заголовок <c>webhook-id</c> — ключ дедупликации),
    /// <c>type</c>, <c>created_at</c> и объект события: <see cref="Run"/> у <c>run.*</c>,
    /// <see cref="Batch"/> у <c>batch.completed</c>, иначе <see cref="WebhookEventData.Object"/> как есть.
    /// </summary>
    public sealed class WebhookEvent
    {
        private Run? _run;
        private Batch? _batch;

        public string Id { get; set; } = string.Empty;
        public WebhookEventType Type { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public WebhookEventData Data { get; set; } = new WebhookEventData();
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }

        /// <summary>Прогон события <c>run.succeeded</c>/<c>run.failed</c> (и других <c>run.*</c>), иначе <c>null</c>.</summary>
        [JsonIgnore]
        public Run? Run => _run ??= Type.Value.StartsWith("run.", StringComparison.Ordinal) ? Data.As<Run>() : null;

        /// <summary>Пакет события <c>batch.completed</c> (и других <c>batch.*</c>), иначе <c>null</c>.</summary>
        [JsonIgnore]
        public Batch? Batch => _batch ??= Type.Value.StartsWith("batch.", StringComparison.Ordinal) ? Data.As<Batch>() : null;

        public override string ToString() => "WebhookEvent(" + Id + ", " + Type + ")";
    }

    /// <summary><c>data</c> события.</summary>
    public sealed class WebhookEventData
    {
        /// <summary>Объект события как есть.</summary>
        public JsonElement Object { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JsonElement>? ExtensionData { get; set; }

        internal T? As<T>() where T : class =>
            Object.ValueKind == JsonValueKind.Object ? Object.Deserialize<T>(Json.ModelOptions) : null;
    }
}
