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

namespace Listenarr.Infrastructure.Search.RequestBudget;

/// <summary>
/// Which kinds of indexer request count against an indexer's daily request limit. Grabs are
/// not charged: indexers count them separately, and refusing one would waste the search that
/// found the release. Every other kind is an HTTP request the indexer serves and counts.
/// </summary>
public static class IndexerRequestBudgetKinds
{
    public static readonly IReadOnlySet<IndexerRequestKind> Charged = new HashSet<IndexerRequestKind>
    {
        IndexerRequestKind.Search,
        IndexerRequestKind.FallbackQuery,
        IndexerRequestKind.Enrichment,
        IndexerRequestKind.Redirect,
        IndexerRequestKind.ConnectionTest
    };

    public static readonly IReadOnlySet<IndexerRequestKind> Uncharged = new HashSet<IndexerRequestKind>
    {
        IndexerRequestKind.DownloadFetch
    };

    /// <summary>
    /// Whether a request of <paramref name="kind"/> is charged. A kind in neither set is
    /// charged, so a kind added later over-counts until it is classified rather than slipping
    /// past the limit.
    /// </summary>
    public static bool IsCharged(IndexerRequestKind kind) => !Uncharged.Contains(kind);
}
