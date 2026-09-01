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

using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Indexers;

/// <summary>
/// HttpClient.Timeout surfaces as a TaskCanceledException, which is an OperationCanceledException.
/// The indexer search stack has to tell that apart from the caller abandoning the search: the first
/// is one indexer failing, the second is the whole operation going away. These tests pin both sides
/// at each of the three layers that tolerate a single failing indexer.
/// </summary>
[Trait("Area", "Search")]
[Trait("Name", "IndexerSearchTimeoutTests")]
[Trait("Category", "IndexerSearch")]
public class IndexerSearchTimeoutTests : BaseTests
{
    private const string SlowIndexer = "Slow Indexer";
    private const string FastIndexer = "Fast Indexer";

    [Fact]
    [Trait("Method", "TorznabNewznabSearchProvider.SearchAsync")]
    [Trait("Scenario", "HttpClientTimeoutIsNotTreatedAsCancellation")]
    public async Task TorznabProvider_WhenHttpClientTimeoutElapses_ReturnsNoResults()
    {
        // Given an indexer whose HTTP call outlives HttpClient.Timeout
        using var handler = new NeverRespondingHandler();
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(75) };
        var provider = new TorznabNewznabSearchProvider(
            httpClient,
            NullLogger<TorznabNewznabSearchProvider>.Instance);

        // When the provider searches it with no caller cancellation in play
        var results = await provider.SearchAsync(BuildTorznabIndexer(SlowIndexer), "dune");

        // Then the timeout is reported as this indexer returning nothing, not as an exception
        Assert.Empty(results);
    }

    [Fact]
    [Trait("Method", "TorznabNewznabSearchProvider.SearchAsync")]
    [Trait("Scenario", "CallerCancellationStillPropagates")]
    public async Task TorznabProvider_WhenCallerCancels_Propagates()
    {
        // Given a caller that has already given up
        using var handler = new NeverRespondingHandler();
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        var provider = new TorznabNewznabSearchProvider(
            httpClient,
            NullLogger<TorznabNewznabSearchProvider>.Instance);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // When the provider searches with that signalled token
        // Then the cancellation is not swallowed the way a timeout is
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.SearchAsync(
                BuildTorznabIndexer(SlowIndexer),
                "dune",
                null,
                null,
                cancellation.Token));
    }

    [Fact]
    [Trait("Method", "IndexerSearchWorkflow.SearchIndexerResultsAsync")]
    [Trait("Scenario", "ProviderTimeoutIsNotTreatedAsCancellation")]
    public async Task SearchIndexerResults_WhenProviderTimesOut_ReturnsNoResults()
    {
        // Given a single indexer whose provider throws the exception HttpClient.Timeout produces
        var indexer = BuildTorznabIndexer(SlowIndexer);
        var provider = new StubSearchProvider(_ => throw NewHttpClientTimeout());
        var workflow = BuildWorkflow(
            [indexer],
            provider,
            NullLogger<IndexerSearchWorkflow>.Instance);

        // When that indexer is searched directly, bypassing the multi-indexer fan-out
        var results = await workflow.SearchIndexerResultsAsync(indexer.Id.ToString(), "dune");

        // Then the caller gets an empty result rather than a fault
        Assert.Empty(results);
    }

    [Fact]
    [Trait("Method", "IndexerSearchWorkflow.SearchIndexerResultsAsync")]
    [Trait("Scenario", "CallerCancellationStillPropagates")]
    public async Task SearchIndexerResults_WhenCallerCancels_Propagates()
    {
        // Given a caller that has already given up
        var indexer = BuildTorznabIndexer(SlowIndexer);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var provider = new StubSearchProvider(token => throw new OperationCanceledException(token));
        var workflow = BuildWorkflow(
            [indexer],
            provider,
            NullLogger<IndexerSearchWorkflow>.Instance);

        // When the search runs under that signalled token
        // Then cancellation reaches the caller instead of looking like an empty indexer
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workflow.SearchIndexerResultsAsync(
                indexer.Id.ToString(),
                "dune",
                null,
                null,
                cancellation.Token));
    }

    [Fact]
    [Trait("Method", "IndexerSearchWorkflow.SearchIndexersAsync")]
    [Trait("Scenario", "OneTimingOutIndexerDoesNotFailTheSearch")]
    public async Task SearchIndexers_WhenOneIndexerTimesOut_OtherIndexersStillReturnResults()
    {
        // Given two configured indexers, one of which times out inside HttpClient
        var slow = BuildTorznabIndexer(SlowIndexer);
        var fast = BuildTorznabIndexer(FastIndexer);
        var provider = new StubSearchProvider(
            _ => throw NewHttpClientTimeout(),
            indexer => indexer.Name == SlowIndexer);
        var workflow = BuildWorkflow(
            [slow, fast],
            provider,
            NullLogger<IndexerSearchWorkflow>.Instance);

        // When all indexers are searched
        var results = await workflow.SearchIndexersAsync("dune");

        // Then the healthy indexer's results survive the slow one
        var result = Assert.Single(results);
        Assert.Equal(FastIndexer, result.Source);
    }

    [Fact]
    [Trait("Method", "IndexerSearchWorkflow.SearchIndexersAsync")]
    [Trait("Scenario", "TimeoutOutsideTheProviderCallDoesNotFailTheSearch")]
    public async Task SearchIndexers_WhenTimeoutSurfacesOutsideTheProviderCall_OtherIndexersStillReturnResults()
    {
        // Given a timeout raised in the fan-out lambda itself rather than inside SearchIndexerAsync,
        // so the per-indexer guard is the only one that can contain it
        var slow = BuildTorznabIndexer(SlowIndexer);
        var fast = BuildTorznabIndexer(FastIndexer);
        var provider = new StubSearchProvider(_ => throw NewHttpClientTimeout(), _ => false);
        var workflow = BuildWorkflow(
            [slow, fast],
            provider,
            new ThrowOnMessageLogger<IndexerSearchWorkflow>(
                $"Searching indexer {SlowIndexer} (",
                NewHttpClientTimeout));

        // When all indexers are searched
        var results = await workflow.SearchIndexersAsync("dune");

        // Then Task.WhenAll still completes and the healthy indexer's results come back
        var result = Assert.Single(results);
        Assert.Equal(FastIndexer, result.Source);
    }

    [Fact]
    [Trait("Method", "IndexerSearchWorkflow.SearchIndexersAsync")]
    [Trait("Scenario", "CallerCancellationStillPropagates")]
    public async Task SearchIndexers_WhenCallerCancels_Propagates()
    {
        // Given a caller that has already given up on a multi-indexer search
        var slow = BuildTorznabIndexer(SlowIndexer);
        var fast = BuildTorznabIndexer(FastIndexer);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var provider = new StubSearchProvider(token => throw new OperationCanceledException(token));
        var workflow = BuildWorkflow(
            [slow, fast],
            provider,
            NullLogger<IndexerSearchWorkflow>.Instance);

        // When the search runs under that signalled token
        // Then the cancellation is not mistaken for every indexer timing out
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workflow.SearchIndexersAsync(
                "dune",
                cancellationToken: cancellation.Token));
    }

    private static Indexer BuildTorznabIndexer(string name)
    {
        return new IndexerBuilder()
            .WithName(name)
            .WithImplementation("Torznab")
            .WithUrl("https://indexer.example.com")
            .WithApiKey("test-api-key")
            .Build();
    }

    /// <summary>
    /// The exception HttpClient raises when its own Timeout elapses: a TaskCanceledException whose
    /// inner exception is a TimeoutException, carrying no caller token.
    /// </summary>
    private static TaskCanceledException NewHttpClientTimeout()
    {
        return new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout elapsing.",
            new TimeoutException());
    }

    private IndexerSearchWorkflow BuildWorkflow(
        IReadOnlyList<Indexer> indexers,
        IIndexerSearchProvider provider,
        ILogger<IndexerSearchWorkflow> logger)
    {
        var repository = new Mock<IIndexerRepository>();
        repository
            .Setup(r => r.GetEnabledAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(indexers.ToList());
        repository
            .Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) =>
                indexers.FirstOrDefault(indexer => indexer.Id == id));
        repository
            .Setup(r => r.GetByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, CancellationToken _) =>
                indexers.FirstOrDefault(indexer => indexer.Name == name));

        return new IndexerSearchWorkflow(
            new HttpClient(),
            new Mock<IConfigurationService>().Object,
            repository.Object,
            [provider],
            new IndexerAdditionalSettingsParser(NullLogger<IndexerAdditionalSettingsParser>.Instance),
            logger);
    }

    /// <summary>
    /// Accepts the connection and then never answers, so HttpClient.Timeout is what ends the call.
    /// </summary>
    private sealed class NeverRespondingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The delay above never completes normally.");
        }
    }

    private sealed class StubSearchProvider : IIndexerSearchProvider
    {
        private readonly Func<CancellationToken, List<IndexerSearchResult>> _onSearch;
        private readonly Func<Indexer, bool> _shouldFail;

        public StubSearchProvider(
            Func<CancellationToken, List<IndexerSearchResult>> onSearch,
            Func<Indexer, bool>? shouldFail = null)
        {
            _onSearch = onSearch;
            _shouldFail = shouldFail ?? (_ => true);
        }

        public string IndexerType => "Torznab";

        public Task<List<IndexerSearchResult>> SearchAsync(
            Indexer indexer,
            string query,
            string? category = null,
            SearchRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (_shouldFail(indexer))
            {
                return Task.FromResult(_onSearch(cancellationToken));
            }

            return Task.FromResult(new List<IndexerSearchResult>
            {
                new()
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = $"{query} from {indexer.Name}",
                    Source = indexer.Name
                }
            });
        }
    }

    /// <summary>
    /// Raises a caller-supplied exception from the one log call that sits in the fan-out lambda but
    /// outside SearchIndexerAsync, so the per-indexer guard can be exercised on its own.
    /// </summary>
    private sealed class ThrowOnMessageLogger<T> : ILogger<T>
    {
        private readonly string _messagePrefix;
        private readonly Func<Exception> _exceptionFactory;

        public ThrowOnMessageLogger(string messagePrefix, Func<Exception> exceptionFactory)
        {
            _messagePrefix = messagePrefix;
            _exceptionFactory = exceptionFactory;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (message.StartsWith(_messagePrefix, StringComparison.Ordinal))
            {
                throw _exceptionFactory();
            }
        }
    }
}
