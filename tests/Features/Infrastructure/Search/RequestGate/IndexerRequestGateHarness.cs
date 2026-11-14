/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Listenarr.Infrastructure.Search.RequestGate;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestGate;

/// <summary>
/// A real <see cref="IndexerRequestGate"/> over a counting stub handler, a manual clock and a
/// state store, for gate-level tests. The stub counts at the wire, independently of the gate.
/// </summary>
internal sealed class IndexerRequestGateHarness : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly HttpClient _client;

    public IndexerRequestGateHarness(
        IIndexerRequestStateStore store,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        int perIndexerCap = 2,
        int globalCap = 8,
        ManualClock? clock = null,
        TimeSpan? clientTimeout = null,
        IEnumerable<IIndexerRequestReservation>? reservations = null)
    {
        Clock = clock ?? new ManualClock(Start);
        Store = store;
        Handler = new CountingHandler(respond);
        _client = new HttpClient(Handler);
        if (clientTimeout is { } timeout)
        {
            _client.Timeout = timeout;
        }

        Cache = new IndexerRequestStateCache(store, NullLogger<IndexerRequestStateCache>.Instance);
        Gate = new IndexerRequestGate(
            Cache,
            reservations ?? [],
            new IndexerConcurrencyLimiter(perIndexerCap, globalCap),
            new IndexerPacingSchedule(Interval, Clock, Clock.AdvanceDelayAsync),
            Clock,
            NullLogger<IndexerRequestGate>.Instance);
    }

    public ManualClock Clock { get; }

    public IIndexerRequestStateStore Store { get; }

    public CountingHandler Handler { get; }

    public IndexerRequestStateCache Cache { get; }

    public IndexerRequestGate Gate { get; }

    public Task<IndexerRequestResult> SendAsync(
        int indexerId,
        CancellationToken cancellationToken = default,
        SearchOrigin origin = SearchOrigin.Sweep,
        CycleRequestAllowance? cycleAllowance = null)
        => Gate.SendAsync(
            new IndexerRequestContext(indexerId, origin, IndexerRequestKind.Search, cycleAllowance),
            token => _client.GetAsync("https://indexer.example.invalid/api", token),
            cancellationToken);

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new TrackedContent() };

    public static HttpResponseMessage RateLimited(RetryConditionHeaderValue? retryAfter, HttpStatusCode status = HttpStatusCode.TooManyRequests)
    {
        var response = Status(status);
        response.Headers.RetryAfter = retryAfter;
        return response;
    }

    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Sequence(
        params Func<HttpResponseMessage>[] responses)
    {
        var next = -1;
        return (_, _) =>
        {
            var index = Math.Min(Interlocked.Increment(ref next), responses.Length - 1);
            return Task.FromResult(responses[index]());
        };
    }

    public void Dispose()
    {
        _client.Dispose();
        Handler.Dispose();
    }

    internal sealed class ManualClock : TimeProvider
    {
        private readonly object _sync = new();
        private DateTimeOffset _now;

        public ManualClock(DateTimeOffset now)
        {
            _now = now;
        }

        public ConcurrentQueue<TimeSpan> Delays { get; } = new();

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return _now;
            }
        }

        public void Advance(TimeSpan by)
        {
            lock (_sync)
            {
                _now += by;
            }
        }

        public Task AdvanceDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Enqueue(delay);
            Advance(delay);
            return Task.CompletedTask;
        }
    }

    internal sealed class CountingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private int _hits;

        public int Hits => Volatile.Read(ref _hits);

        public ConcurrentQueue<HttpResponseMessage> Responses { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _hits);
            var response = await respond(request, cancellationToken);
            Responses.Enqueue(response);
            return response;
        }
    }

    /// <summary>
    /// Empty response content that records whether it was disposed.
    /// </summary>
    internal sealed class TrackedContent : ByteArrayContent
    {
        public TrackedContent()
            : base([])
        {
        }

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// A reservation that counts its calls and answers through the given delegates. By default it
    /// admits every request and every peek.
    /// </summary>
    internal sealed class SpyReservation(
        Func<IndexerRequestContext, CancellationToken, Task<RequestDeferral?>>? reserve = null,
        Func<int, SearchOrigin, CancellationToken, Task<RequestDeferral?>>? peek = null) : IIndexerRequestReservation
    {
        private int _reserveCalls;
        private int _peekCalls;

        public int ReserveCalls => Volatile.Read(ref _reserveCalls);

        public int PeekCalls => Volatile.Read(ref _peekCalls);

        public ConcurrentQueue<IndexerRequestContext> Reserved { get; } = new();

        public Task<RequestDeferral?> TryReserveAsync(IndexerRequestContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _reserveCalls);
            Reserved.Enqueue(context);
            return reserve?.Invoke(context, cancellationToken) ?? Task.FromResult<RequestDeferral?>(null);
        }

        public Task<RequestDeferral?> PeekAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _peekCalls);
            return peek?.Invoke(indexerId, origin, cancellationToken) ?? Task.FromResult<RequestDeferral?>(null);
        }
    }

    /// <summary>
    /// The state store in memory, counting every write.
    /// </summary>
    internal sealed class RecordingStateStore : IIndexerRequestStateStore
    {
        private readonly ConcurrentDictionary<int, IndexerRequestState> _rows = new();
        private int _writes;

        public int Writes => Volatile.Read(ref _writes);

        public IndexerRequestState? Row(int indexerId) => _rows.GetValueOrDefault(indexerId);

        public Task<IReadOnlyList<IndexerRequestState>> LoadAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<IndexerRequestState>>(_rows.Values.Select(Copy).ToList());

        public Task UpsertAsync(IndexerRequestState state, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _writes);
            _rows[state.IndexerId] = Copy(state);
            return Task.CompletedTask;
        }

        public Task ClearConsecutiveFailuresAsync(int indexerId, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _writes);
            if (_rows.TryGetValue(indexerId, out var row))
            {
                var cleared = Copy(row);
                cleared.ConsecutiveFailures = 0;
                cleared.UpdatedUtc = nowUtc;
                _rows[indexerId] = cleared;
            }

            return Task.CompletedTask;
        }

        private static IndexerRequestState Copy(IndexerRequestState state) => new()
        {
            IndexerId = state.IndexerId,
            ConsecutiveFailures = state.ConsecutiveFailures,
            CooldownUntilUtc = state.CooldownUntilUtc,
            CooldownReason = state.CooldownReason,
            LastDispatchUtc = state.LastDispatchUtc,
            UpdatedUtc = state.UpdatedUtc
        };
    }
}
