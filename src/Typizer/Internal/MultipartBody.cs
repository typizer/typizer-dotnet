using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Typizer.Internal
{
    /// <summary>
    /// <c>multipart/form-data</c> written by hand: quoted UTF-8 names (as browsers send them —
    /// MultipartFormDataContent would MIME-encode a Cyrillic file name), a known Content-Length, and
    /// the file streamed from disk or the caller's stream. A new instance per attempt; it never
    /// disposes the caller's stream.
    /// </summary>
    internal sealed class MultipartBody : HttpContent
    {
        private static readonly byte[] CrLf = { (byte)'\r', (byte)'\n' };
        private readonly string _boundary;
        private readonly List<(byte[] Head, FilePart? File, byte[]? Text)> _parts = new List<(byte[], FilePart?, byte[]?)>();
        private readonly byte[] _closing;

        public MultipartBody(IEnumerable<KeyValuePair<string, string>> fields, IEnumerable<FilePart> files)
        {
            _boundary = "typizer-" + Guid.NewGuid().ToString("N");
            foreach (KeyValuePair<string, string> field in fields)
            {
                string head = "--" + _boundary + "\r\nContent-Disposition: form-data; name=\"" + Quote(field.Key) + "\"\r\n\r\n";
                _parts.Add((Encoding.UTF8.GetBytes(head), null, Encoding.UTF8.GetBytes(field.Value)));
            }
            foreach (FilePart file in files)
            {
                string head = "--" + _boundary + "\r\nContent-Disposition: form-data; name=\"" + Quote(file.Field) +
                              "\"; filename=\"" + Quote(file.FileName) + "\"\r\nContent-Type: " + file.ContentType + "\r\n\r\n";
                _parts.Add((Encoding.UTF8.GetBytes(head), file, null));
            }
            _closing = Encoding.ASCII.GetBytes("--" + _boundary + "--\r\n");
            Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");
            Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", _boundary));
        }

        /// <summary>The HTML form encoding of a name: <c>"</c> → %22, CR → %0D, LF → %0A.</summary>
        private static string Quote(string value) =>
            value.Replace("\"", "%22").Replace("\r", "%0D").Replace("\n", "%0A");

#if NET8_0_OR_GREATER
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            WriteAsync(stream, cancellationToken);
#endif

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            WriteAsync(stream, CancellationToken.None);

        private async Task WriteAsync(Stream target, CancellationToken cancellationToken)
        {
            foreach ((byte[] head, FilePart? file, byte[]? text) in _parts)
            {
                await target.WriteAsync(head, 0, head.Length, cancellationToken).ConfigureAwait(false);
                if (text != null)
                    await target.WriteAsync(text, 0, text.Length, cancellationToken).ConfigureAwait(false);
                else
                    await file!.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                await target.WriteAsync(CrLf, 0, CrLf.Length, cancellationToken).ConfigureAwait(false);
            }
            await target.WriteAsync(_closing, 0, _closing.Length, cancellationToken).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _closing.Length;
            foreach ((byte[] head, FilePart? file, byte[]? text) in _parts)
                length += head.Length + (text?.Length ?? file!.Length) + CrLf.Length;
            return true;
        }

        /// <summary>One file of a multipart body: bytes, a path opened per attempt, or a stream rewound per attempt.</summary>
        internal sealed class FilePart
        {
            private readonly string? _path;
            private readonly byte[]? _bytes;
            private readonly Stream? _stream;
            private readonly long _position;

            public FilePart(string field, string fileName, string contentType, long length,
                string? path = null, byte[]? bytes = null, Stream? stream = null, long position = 0)
            {
                Field = field;
                FileName = fileName;
                ContentType = contentType;
                Length = length;
                _path = path;
                _bytes = bytes;
                _stream = stream;
                _position = position;
            }

            public string Field { get; }
            public string FileName { get; }
            public string ContentType { get; }
            public long Length { get; }

            public async Task CopyToAsync(Stream target, CancellationToken cancellationToken)
            {
                if (_bytes != null)
                {
                    await target.WriteAsync(_bytes, 0, _bytes.Length, cancellationToken).ConfigureAwait(false);
                    return;
                }
                if (_path != null)
                {
                    using (var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                    {
                        await CopyExactly(file, target, Length, cancellationToken).ConfigureAwait(false);
                    }
                    return;
                }
                _stream!.Position = _position;
                await CopyExactly(_stream, target, Length, cancellationToken).ConfigureAwait(false);
            }

            /// <summary>Copies exactly <paramref name="length"/> bytes, so a file growing meanwhile cannot break Content-Length.</summary>
            private static async Task CopyExactly(Stream source, Stream target, long length, CancellationToken cancellationToken)
            {
                byte[] buffer = new byte[81920];
                long left = length;
                while (left > 0)
                {
                    int read = await source.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, left), cancellationToken).ConfigureAwait(false);
                    if (read == 0) throw new IOException("The file became shorter while it was being sent");
                    await target.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    left -= read;
                }
            }
        }
    }
}
