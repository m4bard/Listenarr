/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Collections.Concurrent;
using System.Net;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep
{
    /// <summary>
    /// A settable UTC clock shared by the processor, the gate and the stub server.
    /// </summary>
    internal sealed class SweepClock(DateTime startUtc) : TimeProvider
    {
        private readonly Lock _lock = new();
        private DateTime _nowUtc = startUtc;

        public DateTime UtcNow
        {
            get
            {
                lock (_lock)
                {
                    return _nowUtc;
                }
            }
        }

        /// <summary>
        /// When set, a timer the processor creates (a <c>Task.Delay</c> through this clock) is
        /// recorded and completes at once, so a pacing delay costs the test nothing.
        /// </summary>
        public bool CompleteTimersAtOnce { get; set; }

        /// <summary>Every delay requested while <see cref="CompleteTimersAtOnce"/> is set.</summary>
        public ConcurrentQueue<TimeSpan> Delays { get; } = new();

        /// <summary>Runs inside each recorded delay, before it ends.</summary>
        public Action? OnDelay { get; set; }

        public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (!CompleteTimersAtOnce)
            {
                return base.CreateTimer(callback, state, dueTime, period);
            }

            Delays.Enqueue(dueTime);
            OnDelay?.Invoke();
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new CompletedTimer();
        }

        public void Advance(TimeSpan by)
        {
            lock (_lock)
            {
                _nowUtc = _nowUtc.Add(by);
            }
        }
    }

    internal sealed class CompletedTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// The indexer side of the wire. Counts every request it serves per indexer, so request bounds
    /// are measured where the requests land rather than at the gate that is meant to bound them.
    /// </summary>
    internal sealed class StubIndexerServer(SweepClock clock) : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<int, int> _served = new();

        /// <summary>How far the clock moves for each request served.</summary>
        public TimeSpan ClockStepPerRequest { get; set; } = TimeSpan.Zero;

        /// <summary>The status each indexer answers with.</summary>
        public Func<int, HttpStatusCode> StatusFor { get; set; } = _ => HttpStatusCode.OK;

        /// <summary>
        /// Indexers that do not answer until the caller's token is cancelled. A caller that never
        /// passes its token on gets an answer after <see cref="SilentAnswerAfter"/>, so such a
        /// control fails its assertions instead of hanging.
        /// </summary>
        public ConcurrentDictionary<int, bool> Silent { get; } = new();

        /// <summary>Completes when a request reaches a silent indexer.</summary>
        public TaskCompletionSource SilentRequestArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static TimeSpan SilentAnswerAfter { get; } = TimeSpan.FromSeconds(5);

        public int Total => _served.Values.Sum();

        public int ServedFor(int indexerId) => _served.GetValueOrDefault(indexerId);

        public void Reset() => _served.Clear();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Every request completes asynchronously, so searches run in parallel really interleave.
            await Task.Yield();
            var indexerId = int.Parse(request.RequestUri!.Segments[2].TrimEnd('/'));
            _served.AddOrUpdate(indexerId, 1, (_, served) => served + 1);
            clock.Advance(ClockStepPerRequest);
            if (Silent.ContainsKey(indexerId))
            {
                SilentRequestArrived.TrySetResult();
                await Task.Delay(SilentAnswerAfter, cancellationToken);
            }

            return new HttpResponseMessage(StatusFor(indexerId));
        }
    }

    /// <summary>
    /// What one indexer does for one query: how many requests it needs (the first is the query, the
    /// rest enrichment) and what it returns.
    /// </summary>
    internal sealed record IndexerScript(int Requests, IReadOnlyList<IndexerSearchResult> Results)
    {
        public static IndexerScript Nothing { get; } = new(1, Array.Empty<IndexerSearchResult>());
    }

    /// <summary>
    /// An indexer provider that sends each request of its script through the gate to the stub
    /// server. A deferral ends the search Deferred with what was gathered; a failed query is Failed.
    /// </summary>
    internal sealed class ScriptedSearchProvider : IIndexerSearchProvider
    {
        public const string Implementation = "SweepStubIndexer";
        private readonly IIndexerRequestGate _gate;
        private readonly HttpClient _client;

        public ScriptedSearchProvider(IIndexerRequestGate gate, StubIndexerServer server)
        {
            _gate = gate;
            _client = new HttpClient(server, disposeHandler: false) { BaseAddress = new Uri("http://indexer.invalid/") };
        }

        public string IndexerType => Implementation;

        /// <summary>The script per indexer id and query.</summary>
        public Func<int, string, IndexerScript> Script { get; set; } = (_, _) => IndexerScript.Nothing;

        /// <summary>Every search, in order: indexer id, query and the kind of its primary send.</summary>
        public ConcurrentQueue<(int IndexerId, string Query, IndexerRequestKind Kind)> Searches { get; } = new();

        public async Task<IndexerSearchOutcome> SearchAsync(
            Indexer indexer,
            string query,
            string? category,
            SearchRequest? request,
            IndexerSearchContext context,
            CancellationToken cancellationToken)
        {
            Searches.Enqueue((indexer.Id, query, context.QueryKind));
            var script = Script(indexer.Id, query);
            var results = new List<IndexerSearchResult>();
            var sent = 0;
            for (var index = 0; index < script.Requests; index++)
            {
                var kind = index == 0 ? context.QueryKind : IndexerRequestKind.Enrichment;
                var requestIndex = index;
                var sendResult = await _gate.SendAsync(
                    new IndexerRequestContext(indexer.Id, context.Origin, kind, context.CycleAllowance),
                    token => _client.GetAsync($"i/{indexer.Id}/{requestIndex}", token),
                    cancellationToken);
                if (sendResult is IndexerRequestResult.Deferred deferred)
                {
                    return new IndexerSearchOutcome(indexer.Id, IndexerSearchStatus.Deferred, results, sent, deferred.Deferral);
                }

                sent++;
                using var response = ((IndexerRequestResult.Sent)sendResult).Response;
                if (!response.IsSuccessStatusCode)
                {
                    if (index == 0)
                    {
                        return new IndexerSearchOutcome(
                            indexer.Id, IndexerSearchStatus.Failed, results, sent, FailureReason: $"HTTP {(int)response.StatusCode}");
                    }

                    continue;
                }

                if (index == 0)
                {
                    results.AddRange(script.Results);
                }
            }

            return new IndexerSearchOutcome(indexer.Id, IndexerSearchStatus.Completed, results, sent);
        }
    }

    /// <summary>
    /// The pass-through gate, plus deferrals a test asks for per indexer.
    /// </summary>
    internal sealed class ScriptedGate(TimeProvider clock) : IIndexerRequestGate
    {
        private readonly PassThroughIndexerRequestGate _inner = new(clock);

        /// <summary>Indexers whose requests are deferred, and until when.</summary>
        public ConcurrentDictionary<int, DateTime> DeferUntil { get; } = new();

        /// <summary>Every request that reached the gate, in order: indexer id and kind.</summary>
        public ConcurrentQueue<(int IndexerId, IndexerRequestKind Kind)> Sends { get; } = new();

        public Task<IndexerRequestResult> SendAsync(
            IndexerRequestContext context,
            Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
            CancellationToken cancellationToken)
        {
            Sends.Enqueue((context.IndexerId, context.Kind));
            if (DeferUntil.TryGetValue(context.IndexerId, out var untilUtc) && clock.GetUtcNow().UtcDateTime < untilUtc)
            {
                return Task.FromResult<IndexerRequestResult>(new IndexerRequestResult.Deferred(
                    new RequestDeferral(untilUtc, RequestDeferralReason.CycleRequestBound)));
            }

            return _inner.SendAsync(context, dispatch, cancellationToken);
        }

        public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken) =>
            _inner.GetDeferralAsync(indexerId, origin, cancellationToken);
    }

    /// <summary>
    /// Records every log entry, so a test can assert on a warning.
    /// </summary>
    internal sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
