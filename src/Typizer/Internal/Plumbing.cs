using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Typizer.Internal
{
    /// <summary>The <c>{object: "list", data, has_more, next_cursor}</c> envelope of every API list.</summary>
    internal sealed class ListEnvelope<T>
    {
        public string Object { get; set; } = "list";
        public List<T> Data { get; set; } = new List<T>();
        public bool HasMore { get; set; }
        public string? NextCursor { get; set; }
    }

    internal static class Http
    {
        public static readonly HttpMethod Get = HttpMethod.Get;
        public static readonly HttpMethod Post = HttpMethod.Post;
        public static readonly HttpMethod Put = HttpMethod.Put;
        public static readonly HttpMethod Delete = HttpMethod.Delete;
        public static readonly HttpMethod Patch = new HttpMethod("PATCH");

        public static Func<HttpContent> JsonBody(byte[] json) => () =>
        {
            var content = new ByteArrayContent(json);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            return content;
        };
    }

    internal static class Lists
    {
        /// <summary>
        /// A lazily paged list: each page is the same request with the next cursor (<c>starting_after</c>
        /// or, for the catalog, <c>cursor</c> — the caller never sees which).
        /// </summary>
        public static TypizerPageable<T> Create<T>(HttpPipeline pipeline, Func<ApiRequest> build, ListOptions? options,
            string cursorParam, RequestOptions? requestOptions, CancellationToken cancellationToken)
        {
            async Task<Page<T>> Fetch(string? cursor, CancellationToken token)
            {
                ApiRequest request = build();
                if (options?.Limit != null) request.With("limit", Url.Int(options.Limit.Value));
                request.With(cursorParam, cursor ?? options?.Cursor);
                TypizerResponse<ListEnvelope<T>> response =
                    await pipeline.SendJsonAsync<ListEnvelope<T>>(request, requestOptions, token).ConfigureAwait(false);
                ListEnvelope<T> list = response.Value;
                return new Page<T>(list.Data, list.HasMore, list.NextCursor, response, (next, t) => Fetch(next, t));
            }

            return new TypizerPageable<T>(Fetch, cancellationToken);
        }
    }

    internal static class Require
    {
        public static T NotNull<T>(T? value, string paramName) where T : class =>
            value ?? throw new ArgumentNullException(paramName);

        public static string NotEmpty(string? value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Значение не может быть пустым", paramName);
            return value!;
        }
    }
}
