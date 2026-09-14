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

using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Search.Indexers.Common;

/// <summary>
/// Placeholder releases for an install that has no indexer configured yet, so the search screen
/// shows the shape of a result rather than an empty page.
/// </summary>
/// <remarks>
/// Deliberately not the workflow's business: this fabricates data, where the workflow asks real
/// indexers real questions. It is only ever reached when nothing is configured at all. An indexer
/// set emptied by failure backoff is a different case and must never land here, or the
/// automatic-search scorer would be handed five invented releases to grab from.
/// </remarks>
internal static class IndexerMockResultGenerator
{
    private const int MockResultCount = 5;

    public static List<IndexerSearchResult> Generate(ILogger logger, string query)
    {
        return Generate(logger, query, "Mock Indexer", "Torrent");
    }

    public static List<IndexerSearchResult> Generate(ILogger logger, string query, string indexerName, string indexerType)
    {
        var random = new Random();
        var results = new List<IndexerSearchResult>();
        var isUsenet = indexerType.Equals("Usenet", StringComparison.OrdinalIgnoreCase);

        logger.LogInformation("Generating {Count} mock {Type} results for indexer {IndexerName}", MockResultCount, indexerType, indexerName);

        for (int i = 0; i < MockResultCount; i++)
        {
            var result = new IndexerSearchResult
            {
                Id = Guid.NewGuid().ToString(),
                Title = $"{query} - Quality {i + 1}",
                Artist = "Various Authors",
                Album = $"{query} Series",
                Category = "Audiobook",
                Size = random.Next(200_000_000, 1_500_000_000),
                Seeders = isUsenet ? 0 : random.Next(5, 100),
                Leechers = isUsenet ? 0 : random.Next(0, 20),
                Source = indexerName,
                PublishedDate = DateTime.UtcNow.AddDays(-random.Next(1, 365)).ToString("o"),
                Quality = i switch
                {
                    0 => "MP3 64kbps",
                    1 => "MP3 128kbps",
                    2 => "MP3 192kbps",
                    3 => "M4B 128kbps",
                    _ => "FLAC"
                },
                Format = i >= 3 ? "M4B" : "MP3",
                Language = "English"
            };

            if (isUsenet)
            {
                result.NzbUrl = $"https://{indexerName.ToLowerInvariant()}.example.com/api/nzb/{Guid.NewGuid():N}";
                result.MagnetLink = string.Empty;
                result.TorrentUrl = string.Empty;
            }
            else
            {
                result.MagnetLink = $"magnet:?xt=urn:btih:{Guid.NewGuid():N}";
                result.NzbUrl = string.Empty;
            }

            results.Add(result);
        }

        return results;
    }
}
