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
namespace Listenarr.Infrastructure.Persistence.Repositories
{
    public partial class AudiobookRepository
    {
        // Why UpsertCachedAuthorAsync and UpsertCachedSeriesAsync retry. The two methods live
        // with the rest of their own cache -- the author one in AudiobookRepository.AuthorCache.cs,
        // the series one in AudiobookRepository.cs -- and the reasoning is the same for both, so
        // it is written down once here rather than twice.
        //
        // Both cache tables carry a unique index on (NameNormalized, Region), and both upserts
        // resolve an existing row with a read and then write in a separate statement. Two
        // callers that miss the read at the same time therefore both insert, and whichever one
        // reaches the index second gets a UNIQUE violation. Concurrent Audnexus author lookups
        // are enough to produce it: the losing write is dropped and that author's cache entry
        // goes unwritten for the cycle.
        //
        // Retrying is what closes the gap, because the second pass reads the row the winner just
        // committed and takes the update path. The alternative, an INSERT ... ON CONFLICT DO
        // UPDATE, would have to restate the field-by-field merge rules in SQL, including
        // the JSON columns that EF serializes through value converters, so the merge would then
        // live in two places that have to agree.
        //
        // One retry is enough once a winner exists. The bound is here so that a row deleted
        // between two attempts cannot spin.
        private const int CacheUpsertAttempts = 3;
    }
}
