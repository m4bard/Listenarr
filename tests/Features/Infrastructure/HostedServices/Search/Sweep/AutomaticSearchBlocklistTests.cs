/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep
{
    // The sweep selects and starts the download itself, so it has to drop releases already blocked
    // for the book on its own; otherwise a release that failed and was blocked is grabbed again.
    [Trait("Name", "AutomaticSearchBlocklistTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchBlocklistTests : AutomaticSearchSweepTestBase
    {
        [Fact]
        public async Task BlockedRelease_IsNotGrabbed_TheNextOneIs()
        {
            await ConfigureAsync();
            await AddIndexerAsync("Only");
            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            // Both score the same; the blocked one comes first, so it would win the selection.
            var blocked = Release("blocked", "https://indexer.invalid/blocked");
            var allowed = Release("allowed", "https://indexer.invalid/allowed");
            Indexers.Script = (_, _) => new IndexerScript(1, new[] { blocked, allowed });
            using (var scope = _provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IBlocklistService>().BlockAsync(
                    book.Id, ReleaseIdentity.For(SearchResultConverters.ToSearchResult(blocked))!,
                    blocked.Title, blocked.Size, "failed");
            }

            await RunCycleAsync();

            Downloads.Verify(d => d.StartDownloadAsync(
                It.Is<SearchResult>(result => result.SourceLink == allowed.SourceLink), It.IsAny<string>(), book.Id, It.IsAny<CancellationToken>()), Times.Once);
            Downloads.Verify(d => d.StartDownloadAsync(
                It.Is<SearchResult>(result => result.SourceLink == blocked.SourceLink), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private static IndexerSearchResult Release(string title, string sourceLink) => new()
        {
            Title = title,
            Source = AcceptableSource,
            DownloadType = DirectDownloadMetadataKeys.ClientId,
            SourceLink = sourceLink
        };
    }
}
