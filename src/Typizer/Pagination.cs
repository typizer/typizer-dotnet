using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Typizer
{
    // Async iteration without IAsyncEnumerable in the public surface of the netstandard2.0 build:
    // `await foreach` is pattern-based (GetAsyncEnumerator / MoveNextAsync / Current), so these types work
    // in C# 8+ on every target. The net8.0 build also implements IAsyncEnumerable<T> (LINQ, ConfigureAwait,
    // WithCancellation). A netstandard2.0 build that exposed IAsyncEnumerable would tie its callers to
    // Microsoft.Bcl.AsyncInterfaces — an assembly .NET 8 does not ship — so a shared netstandard2.0
    // library using the SDK would fail to load on .NET 8.

    /// <summary>
    /// Одна страница списка: элементы, <see cref="HasMore"/>, <see cref="NextCursor"/> и переход к
    /// следующей (<see cref="GetNextPageAsync"/>). Перебирается как список своих элементов.
    /// </summary>
    public sealed class Page<T> : IReadOnlyList<T>
    {
        private readonly Func<string, CancellationToken, Task<Page<T>>>? _fetchNext;

        internal Page(IReadOnlyList<T> data, bool hasMore, string? nextCursor, TypizerResponse response,
            Func<string, CancellationToken, Task<Page<T>>>? fetchNext)
        {
            Data = data;
            HasMore = hasMore;
            NextCursor = nextCursor;
            Response = response;
            _fetchNext = fetchNext;
        }

        public IReadOnlyList<T> Data { get; }
        public bool HasMore { get; }
        /// <summary>Курсор следующей страницы — его можно сохранить и продолжить позже (<c>ListOptions.Cursor</c>).</summary>
        public string? NextCursor { get; }
        /// <summary>Статус и заголовки ответа этой страницы.</summary>
        public TypizerResponse Response { get; }

        /// <summary>Есть ли следующая страница.</summary>
        public bool HasNextPage => HasMore && !string.IsNullOrEmpty(NextCursor) && _fetchNext != null;

        /// <summary>Следующая страница с тем же фильтром и лимитом, или <c>null</c>, если эта последняя.</summary>
        public async Task<Page<T>?> GetNextPageAsync(CancellationToken cancellationToken = default)
        {
            if (!HasNextPage) return null;
            return await _fetchNext!(NextCursor!, cancellationToken).ConfigureAwait(false);
        }

        public int Count => Data.Count;
        public T this[int index] => Data[index];
        public IEnumerator<T> GetEnumerator() => Data.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Список API: <c>await foreach</c> перебирает все элементы всех страниц (следующая страница
    /// запрашивается, когда кончилась текущая); <c>await</c> отдаёт первую страницу; <see cref="AsPages"/>
    /// перебирает страницы. Без <c>await foreach</c> (C# 7.3) — <see cref="GetPageAsync"/> и
    /// <see cref="Page{T}.GetNextPageAsync"/>.
    /// </summary>
    public sealed class TypizerPageable<T>
#if NET8_0_OR_GREATER
        : IAsyncEnumerable<T>
#endif
    {
        private readonly Func<string?, CancellationToken, Task<Page<T>>> _fetch;
        private readonly CancellationToken _cancellationToken;

        internal TypizerPageable(Func<string?, CancellationToken, Task<Page<T>>> fetch, CancellationToken cancellationToken)
        {
            _fetch = fetch;
            _cancellationToken = cancellationToken;
        }

        /// <summary>Первая страница (или страница после курсора).</summary>
        public Task<Page<T>> GetPageAsync(string? cursor = null, CancellationToken cancellationToken = default) =>
            _fetch(cursor, cancellationToken.CanBeCanceled ? cancellationToken : _cancellationToken);

        /// <summary><c>var page = await client.Runs.ListAsync();</c> — первая страница.</summary>
        public TaskAwaiter<Page<T>> GetAwaiter() => GetPageAsync().GetAwaiter();

        /// <summary>Страницы по одной, пока <c>has_more</c>: <c>await foreach (var page in list.AsPages())</c>.</summary>
        public TypizerAsyncSequence<Page<T>> AsPages() =>
            new TypizerAsyncSequence<Page<T>>(token => PageEnumerator(token), _cancellationToken);

        /// <summary>Все элементы всех страниц.</summary>
        public TypizerAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            CancellationTokenSource? linked = Cancellation.Link(_cancellationToken, cancellationToken, out CancellationToken token);
            Func<CancellationToken, Task<(bool, Page<T>)>> nextPage = PageEnumerator(token);
            Page<T>? page = null;
            int index = 0;
            async Task<(bool, T)> Next(CancellationToken ct)
            {
                while (true)
                {
                    if (page != null && index < page.Count) return (true, page[index++]);
                    (bool has, Page<T> next) = await nextPage(ct).ConfigureAwait(false);
                    if (!has) return (false, default!);
                    page = next;
                    index = 0;
                }
            }
            return new TypizerAsyncEnumerator<T>(Next, token, linked);
        }

#if NET8_0_OR_GREATER
        IAsyncEnumerator<T> IAsyncEnumerable<T>.GetAsyncEnumerator(CancellationToken cancellationToken) => GetAsyncEnumerator(cancellationToken);
#endif

        private Func<CancellationToken, Task<(bool, Page<T>)>> PageEnumerator(CancellationToken token)
        {
            Page<T>? current = null;
            bool done = false;
            return async ct =>
            {
                if (done) return (false, null!);
                Page<T>? next = current == null
                    ? await _fetch(null, ct).ConfigureAwait(false)
                    : await current.GetNextPageAsync(ct).ConfigureAwait(false);
                if (next == null)
                {
                    done = true;
                    return (false, null!);
                }
                current = next;
                return (true, next);
            };
        }
    }

    /// <summary>Асинхронная последовательность для <c>await foreach</c> (в .NET 8 — ещё и <c>IAsyncEnumerable&lt;T&gt;</c>).</summary>
    public sealed class TypizerAsyncSequence<T>
#if NET8_0_OR_GREATER
        : IAsyncEnumerable<T>
#endif
    {
        private readonly Func<CancellationToken, Func<CancellationToken, Task<(bool, T)>>> _start;
        private readonly CancellationToken _cancellationToken;

        internal TypizerAsyncSequence(Func<CancellationToken, Func<CancellationToken, Task<(bool, T)>>> start, CancellationToken cancellationToken)
        {
            _start = start;
            _cancellationToken = cancellationToken;
        }

        public TypizerAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            CancellationTokenSource? linked = Cancellation.Link(_cancellationToken, cancellationToken, out CancellationToken token);
            return new TypizerAsyncEnumerator<T>(_start(token), token, linked);
        }

#if NET8_0_OR_GREATER
        IAsyncEnumerator<T> IAsyncEnumerable<T>.GetAsyncEnumerator(CancellationToken cancellationToken) => GetAsyncEnumerator(cancellationToken);
#endif
    }

    /// <summary>Перечислитель для <c>await foreach</c>.</summary>
    public sealed class TypizerAsyncEnumerator<T>
#if NET8_0_OR_GREATER
        : IAsyncEnumerator<T>
#endif
    {
        private readonly Func<CancellationToken, Task<(bool, T)>> _next;
        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenSource? _linked;

        internal TypizerAsyncEnumerator(Func<CancellationToken, Task<(bool, T)>> next, CancellationToken cancellationToken, CancellationTokenSource? linked)
        {
            _next = next;
            _cancellationToken = cancellationToken;
            _linked = linked;
        }

        public T Current { get; private set; } = default!;

        public async ValueTask<bool> MoveNextAsync()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            (bool has, T value) = await _next(_cancellationToken).ConfigureAwait(false);
            if (!has) return false;
            Current = value;
            return true;
        }

        public ValueTask DisposeAsync()
        {
            _linked?.Dispose();
            return default;
        }
    }

    internal static class Cancellation
    {
        /// <summary>One token from the list call's token and the enumeration's token.</summary>
        public static CancellationTokenSource? Link(CancellationToken first, CancellationToken second, out CancellationToken token)
        {
            if (!second.CanBeCanceled || second == first)
            {
                token = first;
                return null;
            }
            if (!first.CanBeCanceled)
            {
                token = second;
                return null;
            }
            CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(first, second);
            token = linked.Token;
            return linked;
        }
    }
}
