using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Typizer.Internal;

namespace Typizer
{
    /// <summary>
    /// Документ для распознавания. Способ передачи задаётся явно, без угадывания по строке:
    /// <see cref="FromPath"/>, <see cref="FromBytes"/>, <see cref="FromStream"/> (уходят в том же запросе,
    /// <c>multipart/form-data</c>), <see cref="FromUrl(string)"/> (поле <c>url</c>) и
    /// <see cref="FromId(string)"/> (уже загруженный файл, поле <c>file_id</c>).
    /// </summary>
    public sealed class TypizerFile
    {
        /// <summary>Лимит API на один файл: 50 МиБ.</summary>
        public const long MaxBytes = 52428800;

        internal enum SourceKind { Path, Bytes, Stream, Url, Id }

        private TypizerFile(SourceKind kind, string? fileName)
        {
            Kind = kind;
            FileName = fileName;
        }

        internal SourceKind Kind { get; }
        internal string? LocalPath { get; private set; }
        internal byte[]? Bytes { get; private set; }
        internal Stream? Stream { get; private set; }
        internal string? Url { get; private set; }
        internal string? Id { get; private set; }

        /// <summary>Имя файла для multipart: из пути, иначе переданное, иначе <c>document</c>.</summary>
        public string? FileName { get; }

        internal bool IsLocal => Kind == SourceKind.Path || Kind == SourceKind.Bytes || Kind == SourceKind.Stream;

        /// <summary>Локальный файл. Читается с диска при отправке (и заново при повторе).</summary>
        public static TypizerFile FromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Пустой путь к файлу", nameof(path));
            return new TypizerFile(SourceKind.Path, System.IO.Path.GetFileName(path)) { LocalPath = path };
        }

        /// <summary>Файл из памяти.</summary>
        public static TypizerFile FromBytes(byte[] bytes, string? fileName = null)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            return new TypizerFile(SourceKind.Bytes, NonEmpty(fileName) ?? "document") { Bytes = bytes };
        }

        /// <summary>
        /// Файл из потока. Поток, который умеет <c>Seek</c>, читается с текущей позиции и перематывается
        /// при повторе; другой поток (сеть, архив) SDK сначала читает в память целиком — не больше 50 МиБ.
        /// Поток не закрывается: он ваш. Имя — переданное, иначе имя <see cref="FileStream"/>, иначе <c>document</c>.
        /// </summary>
        public static TypizerFile FromStream(Stream stream, string? fileName = null)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Поток не читается", nameof(stream));
            string? name = NonEmpty(fileName) ?? (stream is FileStream fs ? System.IO.Path.GetFileName(fs.Name) : null);
            return new TypizerFile(SourceKind.Stream, NonEmpty(name) ?? "document") { Stream = stream };
        }

        /// <summary>Публичный URL документа — Typizer скачает его сам.</summary>
        public static TypizerFile FromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("Пустой URL", nameof(url));
            return new TypizerFile(SourceKind.Url, null) { Url = url };
        }

        /// <summary>Публичный URL документа — Typizer скачает его сам.</summary>
        public static TypizerFile FromUrl(Uri url)
        {
            if (url == null) throw new ArgumentNullException(nameof(url));
            if (!url.IsAbsoluteUri) throw new ArgumentException("Нужен абсолютный URL", nameof(url));
            return FromUrl(url.AbsoluteUri);
        }

        /// <summary>Файл, уже загруженный через <c>client.Files.UploadAsync</c>: <c>file_…</c>.</summary>
        public static TypizerFile FromId(string fileId)
        {
            if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentException("Пустой идентификатор файла", nameof(fileId));
            return new TypizerFile(SourceKind.Id, null) { Id = fileId };
        }

        /// <summary>Файл, уже загруженный через <c>client.Files.UploadAsync</c>.</summary>
        public static TypizerFile FromId(UploadedFile file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            return FromId(file.Id);
        }

        /// <summary>URL — то же, что <see cref="FromUrl(Uri)"/>.</summary>
        public static implicit operator TypizerFile(Uri url) => FromUrl(url);

        /// <summary>Байты — то же, что <see cref="FromBytes"/> без имени.</summary>
        public static implicit operator TypizerFile(byte[] bytes) => FromBytes(bytes);

        /// <summary>Поток — то же, что <see cref="FromStream"/>.</summary>
        public static implicit operator TypizerFile(Stream stream) => FromStream(stream);

        /// <summary>Файл на диске — то же, что <see cref="FromPath"/>.</summary>
        public static implicit operator TypizerFile(FileInfo file) =>
            FromPath((file ?? throw new ArgumentNullException(nameof(file))).FullName);

        /// <summary>Без содержимого: вид источника и имя.</summary>
        public override string ToString() => Kind switch
        {
            SourceKind.Url => "TypizerFile(url)",
            SourceKind.Id => "TypizerFile(" + Id + ")",
            _ => "TypizerFile(" + Kind.ToString().ToLowerInvariant() + ")",
        };

        private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        /// <summary>
        /// Checks size before any request and makes the content repeatable: a non-seekable stream is read
        /// into memory (≤ 50 MiB). Returns the part to put in a multipart body.
        /// </summary>
        internal async Task<MultipartBody.FilePart> PrepareAsync(string field, CancellationToken cancellationToken)
        {
            string name = FileName ?? "document";
            string type = MimeTypes.ForFileName(name);
            switch (Kind)
            {
                case SourceKind.Path:
                    var info = new FileInfo(LocalPath!);
                    if (!info.Exists) throw new FileNotFoundException("Файл не найден: " + LocalPath, LocalPath);
                    CheckSize(info.Length);
                    return new MultipartBody.FilePart(field, name, type, info.Length, path: LocalPath);
                case SourceKind.Bytes:
                    CheckSize(Bytes!.LongLength);
                    return new MultipartBody.FilePart(field, name, type, Bytes.LongLength, bytes: Bytes);
                case SourceKind.Stream:
                    Stream stream = Stream!;
                    if (stream.CanSeek)
                    {
                        long position = stream.Position;
                        long length = stream.Length - position;
                        CheckSize(length);
                        return new MultipartBody.FilePart(field, name, type, length, stream: stream, position: position);
                    }
                    byte[] buffered = await ReadUpTo(stream, MaxBytes, cancellationToken).ConfigureAwait(false);
                    return new MultipartBody.FilePart(field, name, type, buffered.LongLength, bytes: buffered);
                default:
                    throw new InvalidOperationException("Not a local file");
            }
        }

        private static async Task<byte[]> ReadUpTo(Stream stream, long limit, CancellationToken cancellationToken)
        {
            using (var buffer = new MemoryStream())
            {
                byte[] chunk = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    CheckSize(buffer.Length);
                }
                return buffer.ToArray();
            }
        }

        private static void CheckSize(long length)
        {
            if (length > MaxBytes) throw Errors.FileTooLarge(length);
        }
    }
}
