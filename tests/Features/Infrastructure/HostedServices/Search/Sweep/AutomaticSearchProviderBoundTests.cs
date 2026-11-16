/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Net;
using System.Text;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep
{
    // The per-cycle request bound with a real provider: a Torznab indexer whose feed items each need
    // a comments page, behind the sweep's gate. Counted at the provider's stub server.
    [Trait("Name", "AutomaticSearchProviderBoundTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchProviderBoundTests : AutomaticSearchSweepTestBase
    {
        private const int Allowance = 250;
        private const int ItemsPerFeed = 10;
        private static readonly string[] Titles =
        [
            "A Princess of Mars",
            "Twenty Thousand Leagues Under the Sea",
            "The Wonderful Wizard of Oz"
        ];

        private readonly TorznabServer _torznab = new();

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            AdditionalProviders.Add(new TorznabNewznabSearchProvider(
                new HttpClient(new DelegatingHandlerMock(_torznab.HandleAsync)),
                Gate,
                NullLogger<TorznabNewznabSearchProvider>.Instance));
            BuildHost();
        }

        [Fact]
        public async Task CycleRequestBound_CountedAtTheTorznabServer()
        {
            await ConfigureAsync(maxRequests: Allowance);
            await using (var context = Database.CreateContext())
            {
                var indexer = new IndexerBuilder()
                    .WithName("Torznab")
                    .WithImplementation("Torznab")
                    .WithUrl("https://althub.example")
                    .WithEnabled()
                    .Build();
                indexer.EnableAutomaticSearch = true;
                context.Indexers.Add(indexer);
                await context.SaveChangesAsync();
            }

            // Each search is the query plus one comments page per zero-grab item: 30 x 11 = 330.
            await AddBooksAsync(30, StartUtc.AddMinutes(-5));

            await RunCycleAsync();

            Assert.True(_torznab.RequestCount > 0);
            Assert.True(_torznab.RequestCount <= Allowance, $"the Torznab server saw {_torznab.RequestCount} requests in one cycle, over {Allowance}");
        }

        // Answers every search with the same feed and every comments page with a page of comments.
        private sealed class TorznabServer
        {
            private static readonly string Feed = BuildFeed();
            private int _requestCount;

            public int RequestCount => Volatile.Read(ref _requestCount);

            public Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _requestCount);
                var body = request.RequestUri!.AbsolutePath.StartsWith("/details/", StringComparison.Ordinal)
                    ? "<html><body><p>7 comments</p></body></html>"
                    : Feed;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            }

            private static string BuildFeed()
            {
                var items = new StringBuilder();
                for (var index = 0; index < ItemsPerFeed; index++)
                {
                    items.Append($"""
                        <item>
                          <title>{Titles[index % Titles.Length]}</title>
                          <guid>guid-{index}</guid>
                          <comments>https://althub.example/details/{index}</comments>
                          <enclosure url="https://althub.example/download/{index}.torrent" length="1234" />
                        </item>
                        """);
                }

                return $"<rss><channel>{items}</channel></rss>";
            }
        }
    }
}
