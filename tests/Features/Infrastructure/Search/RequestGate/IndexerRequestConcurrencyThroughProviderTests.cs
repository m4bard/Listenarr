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

using Listenarr.Tests.Common;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.ProviderWireFixture;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestGate;

/// <summary>
/// Q-T3 through a real provider: the MyAnonamouse enrichment runs its detail requests at once,
/// and the gate, not the provider, decides how many reach the wire and in which order. Pacing
/// runs on the harness's manual clock, so no test waits out a real interval.
/// </summary>
[Trait("Name", "IndexerRequestConcurrencyThroughProviderTests")]
[Trait("Category", "IndexerRequestGate")]
public sealed class IndexerRequestConcurrencyThroughProviderTests : BaseTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Concurrency_CapAndInteractiveFirst_EnrichmentNeverExceedsPerIndexerCap()
    {
        // Given: four detail requests that each take a moment at the wire
        var wire = new ConcurrencyWatch();
        using var harness = new IndexerRequestGateHarness(new RecordingStateStore(), async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == DetailPath)
            {
                using var _ = wire.Enter();
                await Task.Delay(50, token);
            }

            return await MyAnonamouseWire(resultCount: 4)(request, token);
        });
        var provider = CreateMyAnonamouseProvider(harness.Gate, harness.Handler);

        // When
        var outcome = await provider.SearchAsync(
            CreateMyAnonamouseIndexer(), "query", null, EnrichRequest(topResults: 4),
            new IndexerSearchContext(SearchOrigin.Sweep, CycleAllowance: null), CancellationToken.None);

        // Then: all five requests were sent, and the wire never saw more than the cap of two at once
        Assert.Equal(IndexerSearchStatus.Completed, outcome.Status);
        Assert.Equal(5, harness.Handler.Hits);
        Assert.Equal(2, wire.Peak);
    }

    [Fact]
    public async Task Concurrency_CapAndInteractiveFirst_QueuedInteractiveSearchGoesBeforeQueuedSweepEnrichment()
    {
        // Given: a sweep search whose four detail requests hold both slots, with two more queued
        var permits = new SemaphoreSlim(0);
        var arrivals = new List<string>();
        using var harness = new IndexerRequestGateHarness(new RecordingStateStore(), async (request, token) =>
        {
            var label = request.RequestUri!.AbsolutePath == DetailPath ? "detail"
                : request.RequestUri.Query.Contains("interactive", StringComparison.Ordinal) ? "interactive"
                : "search";
            lock (arrivals)
            {
                arrivals.Add(label);
            }

            if (label == "detail")
            {
                await permits.WaitAsync(token);
            }

            return await MyAnonamouseWire(resultCount: 4)(request, token);
        });
        var gate = new CountingGate(harness.Gate);
        var provider = CreateMyAnonamouseProvider(gate, harness.Handler);
        var sweep = provider.SearchAsync(
            CreateMyAnonamouseIndexer(), "query", null, EnrichRequest(topResults: 4),
            new IndexerSearchContext(SearchOrigin.Sweep, CycleAllowance: null), CancellationToken.None);
        await WaitUntilAsync(() => Arrivals(arrivals).Count == 3);

        // When: an interactive search arrives while both slots are taken and sweep requests are queued
        var interactive = provider.SearchAsync(
            CreateMyAnonamouseIndexer(), "interactive", null, null,
            new IndexerSearchContext(SearchOrigin.Interactive, CycleAllowance: null), CancellationToken.None);
        await WaitUntilAsync(() => gate.Contexts.Count == 6);
        await Task.Delay(100);
        permits.Release();
        await WaitUntilAsync(() => Arrivals(arrivals).Count >= 4);

        // Then: the next request to reach the wire is the interactive search, ahead of the queued sweep requests
        Assert.Equal(["search", "detail", "detail", "interactive"], Arrivals(arrivals).Take(4));

        permits.Release(4);
        var outcomes = await Task.WhenAll(sweep, interactive);
        Assert.All(outcomes, outcome => Assert.Equal(IndexerSearchStatus.Completed, outcome.Status));
        Assert.Equal(["search", "detail", "detail", "interactive", "detail", "detail"], Arrivals(arrivals));
    }

    private static List<string> Arrivals(List<string> arrivals)
    {
        lock (arrivals)
        {
            return arrivals.ToList();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Patience);
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    // Counts how many requests are inside the wire stub at once and keeps the peak.
    private sealed class ConcurrencyWatch
    {
        private int _current;
        private int _peak;

        public int Peak => Volatile.Read(ref _peak);

        public IDisposable Enter()
        {
            var now = Interlocked.Increment(ref _current);
            int seen;
            do
            {
                seen = Volatile.Read(ref _peak);
            }
            while (now > seen && Interlocked.CompareExchange(ref _peak, now, seen) != seen);

            return new Exit(this);
        }

        private sealed class Exit(ConcurrencyWatch owner) : IDisposable
        {
            public void Dispose() => Interlocked.Decrement(ref owner._current);
        }
    }
}
