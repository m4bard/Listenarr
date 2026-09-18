/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// The one child table of an audiobook that relational cascade does not reach.
    ///
    /// Its own file because AudiobookRepository.cs would otherwise go over the 500-line cap that
    /// ActiveProductionSourceFiles_RemainFocused enforces
    /// (tests/Features/Architecture/BackendArchitectureTests.cs:834-836).
    ///
    /// Worth knowing before adding anything to that file: the two lines this leaves behind there
    /// put it at exactly 500, so it now has no headroom at all and the next line added to it,
    /// comment or code, fails that test. It fails as
    /// "AudiobookRepository.cs (501 lines)" from BackendArchitectureTests.cs:837, which is where
    /// somebody editing that file will meet this before they think to open this one.
    ///
    /// Splitting another method out into a partial file the way this one is split is the fix, and
    /// it is deliberately not done here because it would mean moving an unrelated method for no
    /// behavioural gain. Note that the cheap version of that fix is already spent: the sweep this
    /// file holds was the obvious thing to move out, so the next extraction has to be a method
    /// that has nothing to do with why the file is full.
    ///
    /// The rule counts every line (File.ReadLines(file).Count() at BackendArchitectureTests.cs:834),
    /// so this note cannot live in the file it is about without being the line that trips it.
    /// </summary>
    public partial class AudiobookRepository
    {
        /// <summary>
        /// Queue the removal of a book's blocklist entries. The caller saves, so this lands in the
        /// same transaction as the delete of the book itself and a failure cannot leave the rows
        /// orphaned with the book already gone.
        ///
        /// Set&lt;BlockedRelease&gt; rather than a DbSet property for the reason given on
        /// BlocklistService.BlockedReleases: that property would sit in the one list every table
        /// this release touches has to edit.
        ///
        /// BlockedReleases carries an AudiobookId but no navigation property and therefore no
        /// foreign key. Giving it one means a second migration on top of the one that created the
        /// table, and in SQLite adding a foreign key rebuilds the table. Readarr sweeps the same
        /// rows the same way, from BlocklistService.HandleAsync(AuthorDeletedEvent), rather than
        /// leaning on the database.
        ///
        /// Without this the rows outlive the book: the blocklist endpoints are keyed on a book that
        /// no longer exists, and a book re-added later takes a new id and never sees them.
        /// </summary>
        private async Task RemoveBlocklistEntriesFor(int audiobookId)
        {
            var blockedReleases = await _db.Set<BlockedRelease>()
                .Where(entry => entry.AudiobookId == audiobookId)
                .ToListAsync();
            if (blockedReleases.Count > 0)
            {
                _db.Set<BlockedRelease>().RemoveRange(blockedReleases);
            }
        }
    }
}
