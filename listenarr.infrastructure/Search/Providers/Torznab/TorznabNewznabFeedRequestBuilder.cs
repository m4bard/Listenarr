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

using System.Globalization;

namespace Listenarr.Infrastructure.Search.Providers.Torznab;

/// <summary>
/// Builds the query-less feed request: the indexer's recent releases in its configured
/// categories, one page per offset.
/// </summary>
internal static class TorznabNewznabFeedRequestBuilder
{
    /// <summary>
    /// The page size asked for. An indexer may cap pages lower, so callers page by the number
    /// of items received rather than by this.
    /// </summary>
    public const int PageSize = 100;

    public static string BuildUrl(Indexer indexer, int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        var queryParams = new List<string> { "t=search" };

        if (!string.IsNullOrEmpty(indexer.ApiKey))
        {
            queryParams.Add($"apikey={Uri.EscapeDataString(indexer.ApiKey)}");
        }

        if (!string.IsNullOrEmpty(indexer.Categories))
        {
            queryParams.Add($"cat={Uri.EscapeDataString(indexer.Categories)}");
        }

        queryParams.Add($"limit={PageSize.ToString(CultureInfo.InvariantCulture)}");
        queryParams.Add($"offset={offset.ToString(CultureInfo.InvariantCulture)}");

        if (TorznabNewznabRequestBuilder.RequestsExtendedAttributes(indexer))
        {
            queryParams.Add("extended=1");
        }

        return $"{TorznabNewznabRequestBuilder.BuildApiUrl(indexer)}?{string.Join("&", queryParams)}";
    }
}
