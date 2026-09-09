using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Core;

/// <summary>
/// Throwaway probe: measures the peak number of simultaneously in-flight
/// per-indexer provider calls that IndexerSearchWorkflow.SearchIndexersAsync
/// produces. Not intended to be committed.
/// </summary>
public class IndexerFanOutConcurrencyProbe : BaseTests
{
    private sealed class ConcurrencyRecordingProvider : IIndexerSearchProvider
    {
        private int _inFlight;
        public int PeakConcurrency;
        public int TotalCalls;

        public string IndexerType => "Torznab";

        public async Task<List<IndexerSearchResult>> SearchAsync(
            Indexer indexer,
            string query,
            string? category = null,
            SearchRequest? request = null)
        {
            Interlocked.Increment(ref TotalCalls);
            var now = Interlocked.Increment(ref _inFlight);
            int observed;
            do
            {
                observed = Volatile.Read(ref PeakConcurrency);
                if (now <= observed) break;
            }
            while (Interlocked.CompareExchange(ref PeakConcurrency, now, observed) != observed);

            await Task.Delay(120);
            Interlocked.Decrement(ref _inFlight);
            return new List<IndexerSearchResult>();
        }
    }

    private static IndexerSearchWorkflow BuildWorkflow(
        int indexerCount,
        ConcurrencyRecordingProvider provider)
    {
        var indexers = Enumerable.Range(1, indexerCount)
            .Select(i => new Indexer
            {
                Id = i,
                Name = $"indexer-{i}",
                Implementation = "Torznab",
                Type = "Torrent",
                Url = $"http://indexer-{i}.invalid",
                IsEnabled = true,
                EnableAutomaticSearch = true
            })
            .ToList();

        var repo = new Mock<IIndexerRepository>();
        repo.Setup(r => r.GetEnabledAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexers);

        return new IndexerSearchWorkflow(
            new HttpClient(),
            Mock.Of<IConfigurationService>(),
            repo.Object,
            new IIndexerSearchProvider[] { provider },
            new IndexerAdditionalSettingsParser(NullLogger<IndexerAdditionalSettingsParser>.Instance),
            NullLogger<IndexerSearchWorkflow>.Instance);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(40)]
    public async Task FanOut_peak_concurrency_equals_enabled_indexer_count(int indexerCount)
    {
        var provider = new ConcurrencyRecordingProvider();
        var workflow = BuildWorkflow(indexerCount, provider);

        await workflow.SearchIndexersAsync("some query", isAutomaticSearch: true);

        Assert.Equal(indexerCount, provider.TotalCalls);
        Assert.Equal(indexerCount, provider.PeakConcurrency);
    }
}
