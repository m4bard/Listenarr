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

namespace Listenarr.Application.Search.Discovery
{
    public sealed class DiscoveryStatusService(
        IIndexerRepository indexerRepository,
        IDiscoveryStore store,
        IIndexerFeedProvider feedProvider,
        TimeProvider timeProvider) : IDiscoveryStatusService
    {
        /// <summary>The most open gaps listed per indexer.</summary>
        internal const int OpenGapLimit = 20;

        public async Task<IReadOnlyList<DiscoveryIndexerStatusReport>> GetStatusAsync(CancellationToken cancellationToken)
        {
            var indexers = await indexerRepository.GetAllAsync(cancellationToken);
            var reports = new List<DiscoveryIndexerStatusReport>();
            foreach (var indexer in indexers.Where(indexer => indexer.IsEnabled && indexer.EnableRss).OrderBy(indexer => indexer.Id))
            {
                var status = await store.GetStatusAsync(indexer.Id, OpenGapLimit, cancellationToken);
                var checkpoint = status.Checkpoint;
                reports.Add(new DiscoveryIndexerStatusReport(
                    indexer.Id,
                    indexer.Name,
                    ClassifyFeed(indexer),
                    checkpoint?.NewestPublishedUtc,
                    checkpoint?.LastPollUtc,
                    checkpoint?.LastSuccessUtc,
                    checkpoint?.FutureDatedItemCount ?? 0,
                    checkpoint?.UndatedItemCount ?? 0,
                    status.OpenGaps
                        .Select(gap => new DiscoveryCoverageGapReport(gap.Id, gap.GapStartUtc, gap.GapEndUtc, gap.DetectedUtc))
                        .ToList()));
            }

            return reports;
        }

        public Task<bool> AcknowledgeGapAsync(int gapId, CancellationToken cancellationToken) =>
            store.AcknowledgeGapAsync(gapId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        private DiscoveryFeedSupport ClassifyFeed(Indexer indexer)
        {
            if (!feedProvider.Supports(indexer))
            {
                return DiscoveryFeedSupport.Unsupported;
            }

            return string.IsNullOrWhiteSpace(indexer.Categories)
                ? DiscoveryFeedSupport.NoCategories
                : DiscoveryFeedSupport.Supported;
        }
    }
}
