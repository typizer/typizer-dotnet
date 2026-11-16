using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Typizer.Internal
{
    internal static class KeyMask
    {
        private static readonly Regex Prefix = new Regex("^tz_[a-z]+_", RegexOptions.CultureInvariant);

        /// <summary><c>tz_live_…a1b2</c>: the prefix and the last 4 characters, never the whole key.</summary>
        public static string Mask(string? key)
        {
            if (string.IsNullOrEmpty(key)) return "(none)";
            string prefix = Prefix.Match(key).Value;
            string rest = key!.Substring(prefix.Length);
            return rest.Length >= 12 ? prefix + "…" + rest.Substring(rest.Length - 4) : prefix + "…";
        }
    }

    internal static class IdempotencyKeys
    {
        public static string New() => Guid.NewGuid().ToString("D");

        /// <summary>1–255 printable ASCII characters (the API's pattern), checked before any request.</summary>
        public static void Validate(string? key, string paramName)
        {
            if (key == null) return;
            if (key.Length < 1 || key.Length > 255)
                throw new ArgumentException("Idempotency-Key: от 1 до 255 символов", paramName);
            foreach (char c in key)
            {
                if (c < 0x20 || c > 0x7E)
                    throw new ArgumentException("Idempotency-Key: только печатные символы ASCII", paramName);
            }
        }
    }

    /// <summary>Monotonic time for deadlines and pauses (Stopwatch, not the wall clock).</summary>
    internal static class Clock
    {
        public static long Now() => Stopwatch.GetTimestamp();

        public static long After(TimeSpan span) =>
            Now() + (long)(span.TotalSeconds * Stopwatch.Frequency);

        public static TimeSpan Remaining(long deadline) =>
            TimeSpan.FromSeconds((double)(deadline - Now()) / Stopwatch.Frequency);

        public static TimeSpan Since(long start) =>
            TimeSpan.FromSeconds((double)(Now() - start) / Stopwatch.Frequency);
    }

    internal static class UserAgent
    {
        private static string? _platform;

        /// <summary><c>typizer-dotnet/0.2.0 (dotnet/8.0.6; linux/arm64)[ app/1.0]</c> — SPEC §2.3.</summary>
        public static string Build(AppInfo? app)
        {
            var sb = new StringBuilder("typizer-dotnet/").Append(TypizerClient.Version);
            string platform = _platform ??= Platform();
            if (platform.Length > 0) sb.Append(" (").Append(platform).Append(')');
            if (app != null)
            {
                sb.Append(' ').Append(Token(app.Name));
                if (!string.IsNullOrEmpty(app.Version)) sb.Append('/').Append(Token(app.Version!));
                if (!string.IsNullOrEmpty(app.Url)) sb.Append(" (+").Append(app.Url).Append(')');
            }
            return sb.ToString();
        }

        private static string Token(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (char c in value) sb.Append(c > 0x20 && c < 0x7F && c != '/' && c != '(' && c != ')' ? c : '-');
            return sb.ToString();
        }

        private static string Platform()
        {
            try
            {
                return Runtime() + "; " + Os() + "/" + Arch();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string Runtime()
        {
            // ".NET 8.0.6", ".NET Core 3.1.32", ".NET Framework 4.8.9232.0", "Mono 6.12.0.200 (…)"
            string description = RuntimeInformation.FrameworkDescription.Trim();
            Match m = Regex.Match(description, @"^(\.NET Framework|\.NET Core|\.NET|Mono)\s+([0-9][0-9A-Za-z.\-+]*)");
            if (!m.Success) return "dotnet/" + Environment.Version.ToString();
            string version = m.Groups[2].Value;
            int plus = version.IndexOf('+');
            if (plus >= 0) version = version.Substring(0, plus);
            switch (m.Groups[1].Value)
            {
                case ".NET Framework": return "dotnet-framework/" + version;
                case "Mono": return "mono/" + version;
                default: return "dotnet/" + version;
            }
        }

        private static string Os()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "darwin";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("FREEBSD"))) return "freebsd";
            return "unknown";
        }

        private static string Arch() => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
    }

    /// <summary>Path and query building with every value escaped.</summary>
    internal static class Url
    {
        public static string Segment(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Идентификатор не может быть пустым", paramName);
            return Uri.EscapeDataString(value);
        }

        public static string Query(IEnumerable<KeyValuePair<string, string>>? query)
        {
            if (query == null) return string.Empty;
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in query)
            {
                sb.Append(sb.Length == 0 ? '?' : '&')
                  .Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(pair.Value));
            }
            return sb.ToString();
        }

        public static string Timestamp(DateTimeOffset value) =>
            value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        public static string Date(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Content-Type of a multipart file part, by extension (the server sniffs the content anyway).</summary>
    internal static class MimeTypes
    {
        private static readonly Dictionary<string, string> ByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".tif"] = "image/tiff",
            [".tiff"] = "image/tiff",
            [".webp"] = "image/webp",
            [".heic"] = "image/heic",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".odt"] = "application/vnd.oasis.opendocument.text",
            [".ods"] = "application/vnd.oasis.opendocument.spreadsheet",
            [".rtf"] = "application/rtf",
            [".txt"] = "text/plain",
            [".zip"] = "application/zip",
        };

        public static string ForFileName(string fileName)
        {
            string ext = System.IO.Path.GetExtension(fileName);
            return ext != null && ByExtension.TryGetValue(ext, out string? type) ? type : "application/octet-stream";
        }
    }
}
