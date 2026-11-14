using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Typizer
{
    /// <summary>
    /// Статус и заголовки успешного ответа: <c>X-Request-Id</c>, <c>RateLimit-*</c>,
    /// <c>Idempotent-Replayed</c>, <c>Typizer-Maintenance</c>, <c>Location</c>. Возвращают методы
    /// <c>…WithResponseAsync</c>; у страниц списка — <see cref="Page{T}.Response"/>.
    /// </summary>
    public class TypizerResponse
    {
        internal TypizerResponse(int statusCode, IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
        {
            StatusCode = statusCode;
            Headers = headers;
        }

        public int StatusCode { get; }
        /// <summary>Заголовки ответа, имена без учёта регистра.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }

        /// <summary>Первое значение заголовка или <c>null</c>.</summary>
        public string? GetHeader(string name) =>
            Headers.TryGetValue(name, out IReadOnlyList<string>? values) && values.Count > 0 ? values[0] : null;

        public string? RequestId => GetHeader("X-Request-Id");

        /// <summary>Ответ — повтор первого ответа на тот же <c>Idempotency-Key</c>.</summary>
        public bool IdempotentReplayed => string.Equals(GetHeader("Idempotent-Replayed"), "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>Объявленные плановые работы: <c>mnt_…; starts=…; ends=…</c>.</summary>
        public string? Maintenance => GetHeader("Typizer-Maintenance");

        public string? Location => GetHeader("Location");

        public RateLimitInfo? RateLimit => Internal.HttpPipeline.ParseRateLimit(Headers);

        internal static IReadOnlyDictionary<string, IReadOnlyList<string>> CollectHeaders(HttpResponseMessage response)
        {
            var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            void Add(IEnumerable<KeyValuePair<string, IEnumerable<string>>> source)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in source)
                {
                    List<string> values = header.Value.ToList();
                    if (headers.TryGetValue(header.Key, out IReadOnlyList<string>? existing))
                        values.InsertRange(0, existing);
                    headers[header.Key] = values;
                }
            }
            Add(response.Headers);
            if (response.Content != null) Add(response.Content.Headers);
            return headers;
        }
    }

    /// <summary>Значение ответа вместе со статусом и заголовками.</summary>
    public sealed class TypizerResponse<T> : TypizerResponse
    {
        internal TypizerResponse(T value, int statusCode, IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
            : base(statusCode, headers)
        {
            Value = value;
        }

        public T Value { get; }
    }

    /// <summary>
    /// Файл из ответа: результат прогона (<c>Runs.GetOutputAsync</c>), исходный документ
    /// (<c>Files.GetContentAsync</c>), выгрузка пакета, XSD. Тело читается потоком — закройте объект
    /// (<c>using</c>) или вызовите один из методов чтения.
    /// </summary>
    public sealed class TypizerDownload : TypizerResponse, IDisposable
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _content;

        internal TypizerDownload(HttpResponseMessage response, Stream content, IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
            : base((int)response.StatusCode, headers)
        {
            _response = response;
            _content = content;
            ContentType = response.Content?.Headers.ContentType?.MediaType;
            ContentLength = response.Content?.Headers.ContentLength;
            FileName = SafeFileName(response.Content?.Headers.ContentDisposition?.FileNameStar
                                    ?? response.Content?.Headers.ContentDisposition?.FileName);
        }

        /// <summary>Тело ответа. Читается один раз.</summary>
        public Stream Content => _content;
        /// <summary><c>Content-Type</c> без параметров: <c>application/xml</c>, <c>text/csv</c>, …</summary>
        public string? ContentType { get; }
        /// <summary>Длина, если сервер её сообщил (большие выгрузки идут без неё).</summary>
        public long? ContentLength { get; }
        /// <summary>
        /// Имя файла из <c>Content-Disposition</c> — только имя, без каталогов, <c>..</c> и управляющих
        /// символов, так что его можно склеить со своей папкой.
        /// </summary>
        public string? FileName { get; }

        public async Task<byte[]> ReadAsBytesAsync(CancellationToken cancellationToken = default)
        {
            using (this)
            using (var buffer = new MemoryStream())
            {
                await _content.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                return buffer.ToArray();
            }
        }

        public async Task<string> ReadAsStringAsync(CancellationToken cancellationToken = default) =>
            Encoding.UTF8.GetString(await ReadAsBytesAsync(cancellationToken).ConfigureAwait(false));

        /// <summary>Сохраняет тело в файл (перезаписывает).</summary>
        public async Task SaveToAsync(string path, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Пустой путь", nameof(path));
            using (this)
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await _content.CopyToAsync(file, 81920, cancellationToken).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _content.Dispose();
            _response.Dispose();
        }

        internal static string? SafeFileName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string value = name!.Trim().Trim('"');
            int slash = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
            if (slash >= 0) value = value.Substring(slash + 1);
            // The same rules on every OS (Path.GetInvalidFileNameChars differs): drop control
            // characters and the Windows-invalid set, incl. ':' (drive letters, NTFS streams).
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (!char.IsControl(c) && InvalidNameChars.IndexOf(c) < 0) sb.Append(c);
            }
            // Windows ignores trailing dots and spaces ("..." is "..", "a. " is "a").
            value = sb.ToString().Trim().TrimEnd('.', ' ');
            if (value.Length == 0 || IsReservedDeviceName(value) || Path.IsPathRooted(value)) return null;
            return value;
        }

        private const string InvalidNameChars = "<>:\"/\\|?*";

        private static readonly string[] ReservedDeviceNames =
        {
            "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        private static bool IsReservedDeviceName(string name)
        {
            int dot = name.IndexOf('.');
            string stem = (dot >= 0 ? name.Substring(0, dot) : name).TrimEnd(' ');
            foreach (string reserved in ReservedDeviceNames)
            {
                if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
