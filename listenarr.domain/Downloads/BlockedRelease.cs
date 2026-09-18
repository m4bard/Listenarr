/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Domain.Downloads
{
    /// <summary>
    /// One release that failed for one book, and should not be grabbed for that book again.
    ///
    /// Scoped to the book rather than global on purpose: a release that is broken for the
    /// title it was grabbed for says nothing about the indexer, and nothing about the other
    /// releases of the same title. This mirrors how the *arr family scopes a blocklist
    /// entry to the media item it was rejected for.
    /// </summary>
    public class BlockedRelease
    {
        public int Id { get; set; }

        public int AudiobookId { get; set; }

        /// <summary>
        /// The primary key for the release, as <see cref="ReleaseIdentity"/> works it out: a
        /// normalised torrent info-hash where one is known, since that identifies the same
        /// release across indexers, otherwise the normalised release title. Never the indexer
        /// URL: a Usenet download link carries a per-fetch token, so it differs between the grab
        /// and the next search. Never the download client's own id either, which is per
        /// submission and would not match the same release on a later search.
        ///
        /// This is the uniqueness rule for a row rather than the whole of the match.
        /// <see cref="Title"/> and <see cref="Size"/> below are the second key, and either key
        /// can produce a match; see
        /// <see cref="ReleaseIdentity.Matches(BlockedRelease, SearchResult)"/>.
        /// </summary>
        public ReleaseIdentifier ReleaseIdentifier { get; set; }

        /// <summary>
        /// The release title as the indexer advertised it. Load-bearing rather than decoration:
        /// this is the second lookup key, and it is what lets a row written under an info-hash
        /// still be matched by the same release on a later search where the indexer served no
        /// magnet. Kept as advertised rather than pre-normalised, so the row stays readable and
        /// so the normalisation rules can change without stranding rows.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// The size the indexer advertised, compared with tolerance rather than for equality and
        /// treated as no constraint at all when null. Both are deliberate and both are Readarr's
        /// behaviour (src/NzbDrone.Core/Blocklisting/BlocklistService.cs:157-167).
        /// </summary>
        public long? Size { get; set; }

        public DateTime BlockedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Why it was blocked. Not load-bearing for behaviour, only for explaining it later.</summary>
        public string Reason { get; set; } = string.Empty;
    }
}
