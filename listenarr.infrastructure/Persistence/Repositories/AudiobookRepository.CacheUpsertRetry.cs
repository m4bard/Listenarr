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
        // the series one in AudiobookRepository.cs -- and the reasoning below holds for both, so
        // it is written down once here rather than twice.
        //
        // One thing differs, and the author catch spells it out where it matters. The author
        // upsert refuses to rebind an ASIN onto a row named for somebody else, which closes off
        // one route to an update-path collision that is still open on the series side.
        //
        // Both cache tables carry a unique index on (NameNormalized, Region), and both upserts
        // resolve an existing row with a read and then write in a separate statement. Two
        // callers that miss the read at the same time therefore both insert, and whichever one
        // reaches the index second gets a UNIQUE violation. Concurrent Audnexus author lookups
        // are enough to produce it: the losing write is dropped and that author's cache entry
        // goes unwritten for the cycle.
        //
        // Retrying closes the gap, because the second pass reads the row the winner committed
        // and takes the update path. Normally one retry does it; the bound is what guarantees
        // termination if some repeat we have not thought of keeps the read missing.
        //
        // INSERT ... ON CONFLICT DO UPDATE is not a drop-in here. It takes a single conflict
        // target, which can only be the unique (NameNormalized, Region) index, but the
        // resolution in both methods is ASIN-first against an index that is deliberately not
        // unique. Finding the row that holds an ASIN whose name differs is not expressible as a
        // conflict clause on the name index, so the SQL form would quietly change what the
        // method does. It would also restate the field merge rules, JSON columns and value
        // converters included, somewhere they have to be kept in step by hand.
        //
        // BEGIN IMMEDIATE would make the read and the write atomic rather than detecting the
        // loss afterwards, and it would hold across processes. It is not used because EF's
        // BeginTransactionAsync issues a deferred BEGIN, so it means dropping to the raw
        // connection, and a later refactor that restores the deferred form turns the symptom
        // into SQLITE_BUSY without anything failing loudly. It also serializes every cache
        // upsert to fix a collision that is rare by construction.
        //
        // Two limits on the retry, both deliberate.
        //
        // A blank normalized name is never retried. The key being written is blank too, and
        // re-reading it would mean resolving on the empty string, which merges authors whose
        // names differ only in punctuation onto one row. There is no correct row to find, so
        // that case keeps the behaviour it has today.
        //
        // SaveChangesAsync saves the whole scoped context rather than this entity alone, and
        // the translated exception carries no table or column, so a violation raised by another
        // pending entity in the same scope would be read as this one's. No caller shares a
        // scope that way today, and the bound limits the cost if one ever does.
        private const int CacheUpsertAttempts = 3;
    }
}
