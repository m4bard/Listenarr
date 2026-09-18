/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Text;
using System.Text.RegularExpressions;

namespace Listenarr.Domain.Downloads
{
    /// <summary>
    /// Works out the stable identity of a release, and decides whether a blocklist row and a
    /// search result are the same release.
    ///
    /// The download client's own id is deliberately not used. A qBittorrent info-hash
    /// happens to be both, but a SABnzbd nzo_id is allocated per submission, so keying on
    /// it would produce an entry that never matches the same release again and a blocklist
    /// that silently does nothing on Usenet.
    ///
    /// Two rules govern the rest of it.
    ///
    /// First, the key is derived from a <see cref="SearchResult"/> once, at the moment the release
    /// is grabbed, and stamped onto the <see cref="Download"/> that the grab creates. The failure
    /// path reads that stamp back rather than deriving its own. Three defects in this feature had
    /// the same shape, two sides deriving a key independently and drifting apart: first a per-fetch
    /// Usenet URL that differed between grab and failure, then a size read from Download.TotalSize,
    /// which the queue poller overwrites from the client's snapshot while the search side still
    /// sees the size the indexer advertised.
    ///
    /// Second, a row carries BOTH an info-hash key and the advertised title and size, and a match
    /// can come from either. That is the *arr family's shape, and one key is demonstrably not
    /// enough: a SearchResult has no info-hash field of its own, only a MagnetLink, so whether a
    /// hash can be derived at all depends on what the indexer put in that one response. A torrent
    /// grabbed while the indexer served a magnet and returned later as a .torrent URL would be
    /// stored under one key and looked up under another. Readarr answers this by looking a row up
    /// by info-hash when the release has one and by title when it does not
    /// (src/NzbDrone.Core/Blocklisting/BlocklistService.cs:38-62, with the two repository queries
    /// declared at src/NzbDrone.Core/Blocklisting/BlocklistRepository.cs:10-11). Its comparison is
    /// deliberately tolerant: a stored size within 2 MB counts as the same size, and a stored
    /// field left null matches anything (HasSameSize at BlocklistService.cs:157-167,
    /// HasSameIndexer at :136-144). Neither behaviour can be expressed by a digest over an exact
    /// byte count, which is why the fields are stored rather than hashed.
    /// </summary>
    public static class ReleaseIdentity
    {
        /// <summary>
        /// Where the grab-time key lives on a Download. <see cref="Download.Metadata"/> already
        /// carries ClientDownloadId and TorrentHash, so a release key is at home there.
        /// </summary>
        public const string MetadataKey = "ReleaseIdentity";

        /// <summary>A key derived from a normalised BitTorrent v1 info-hash.</summary>
        public const string InfoHashPrefix = "btih:";

        /// <summary>A key derived from the normalised release title.</summary>
        public const string TitlePrefix = "title:";

        /// <summary>
        /// Every prefix a key can carry, declared rather than discovered. A new branch emitting a
        /// key under a prefix nobody pinned is the failure the golden vectors exist to catch, and
        /// this set is what lets them check for it without scanning source text for literals.
        /// </summary>
        public static readonly IReadOnlySet<string> KeyPrefixes =
            new HashSet<string>(StringComparer.Ordinal) { InfoHashPrefix, TitlePrefix };

        /// <summary>
        /// How far two reported sizes may differ and still be the same release. Indexers do not
        /// report a byte-identical size on every listing, which is why Readarr allows slack here
        /// rather than demanding equality
        /// (src/NzbDrone.Core/Blocklisting/BlocklistService.cs:157-167, where the same 2 MB is
        /// spelled <c>2.Megabytes()</c>).
        /// </summary>
        public const long SizeToleranceBytes = 2L * 1024 * 1024;

        private const string TorrentHashMetadataKey = "TorrentHash";

        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        private const string MagnetInfoHashMarker = "urn:btih:";

        /// <summary>
        /// The key for a release as the indexer advertised it. This is the only place that decides
        /// which fields of a search result make up the key, so the grab side and the search-filter
        /// side cannot pick different ones.
        /// </summary>
        public static ReleaseIdentifier? For(SearchResult result)
        {
            if (result is null)
            {
                return null;
            }

            return KeyFor(TorrentHashFrom(result.MagnetLink), result.Title);
        }

        /// <summary>
        /// The key for a release that has already been grabbed. Prefers the value stamped on the
        /// download when it was created, because every field this could otherwise be recomputed
        /// from is mutable after the grab.
        /// </summary>
        public static ReleaseIdentifier? ForGrabbed(Download download)
        {
            if (download is null)
            {
                return null;
            }

            var stamped = download.GetMetadataString(MetadataKey);
            if (!string.IsNullOrWhiteSpace(stamped))
            {
                return ReleaseIdentifier.FromStorage(stamped);
            }

            // A download created before the stamp existed. Recomputing is all that is left for it.
            return KeyFor(
                download.GetMetadataString(TorrentHashMetadataKey),
                download.Title);
        }

        /// <summary>
        /// The size to record alongside a blocklist row for an already-grabbed download. Prefers
        /// ExpectedFileSize, which is copied from the search result and never written again,
        /// because TotalSize is overwritten from the download client's queue snapshot in
        /// QueueItemConverter and three times over in the direct-download worker. A row whose size
        /// came from TotalSize would sit further than the tolerance from what the indexer
        /// advertises on the next search, and the comparison would rightly call it a different
        /// release.
        /// </summary>
        public static long? SizeForGrabbed(Download download)
        {
            if (download is null)
            {
                return null;
            }

            return download.ExpectedFileSize ?? (download.TotalSize > 0 ? download.TotalSize : null);
        }

        /// <summary>
        /// Whether one blocklist row and one search result are the same release.
        /// </summary>
        public static bool Matches(BlockedRelease entry, SearchResult result)
        {
            if (entry is null || result is null)
            {
                return false;
            }

            return Matches(entry, TorrentHashFrom(result.MagnetLink), result.Title, result.Size);
        }

        /// <summary>
        /// Whether one blocklist row is the same release as the release fields given.
        ///
        /// Info-hash first and decisively: two releases with different v1 info-hashes are
        /// different torrents whatever their titles say, which is why Readarr's SameTorrent
        /// returns the hash comparison rather than falling through
        /// (src/NzbDrone.Core/Blocklisting/BlocklistService.cs:126-134). When either side has no
        /// hash, fall back to the advertised title and size with slack, the way Readarr falls back
        /// to its title lookup at :54 and :59.
        ///
        /// One deliberate difference from Readarr. Readarr picks which key to use from the
        /// incoming release alone, so a release carrying a hash is only ever looked up by hash and
        /// a row written when no magnet was on offer can never be matched once an indexer starts
        /// advertising one. Here the title fallback is reached whenever *either* side lacks a
        /// hash, which closes that direction too. It cannot over-block: where both sides have a
        /// hash the hash still settles it alone.
        /// </summary>
        public static bool Matches(BlockedRelease entry, string? infoHash, string? title, long? size)
        {
            if (entry is null)
            {
                return false;
            }

            var storedHash = entry.ReleaseIdentifier.InfoHash;
            var candidateHash = NormalizeInfoHash(infoHash);

            if (storedHash is not null && candidateHash is not null)
            {
                return string.Equals(storedHash, candidateHash, StringComparison.OrdinalIgnoreCase);
            }

            return HasSameTitle(entry, title) && HasSameSize(entry, size);
        }

        /// <summary>
        /// The key to store for a release. Internal on purpose: production code outside this
        /// assembly cannot compose a key out of release fields by hand, and has to come through
        /// <see cref="For(SearchResult)"/> or <see cref="ForGrabbed"/> instead. That is this
        /// class owning the format made into a compile error rather than a convention to remember.
        /// </summary>
        internal static ReleaseIdentifier? KeyFor(string? torrentInfoHash, string? title)
        {
            // A torrent info-hash is the release, across indexers and across submissions.
            var hash = NormalizeInfoHash(torrentInfoHash);
            if (hash is not null)
            {
                return ReleaseIdentifier.ForInfoHash(hash);
            }

            // The normalised title next. Stored as text rather than digested, so a row stays
            // readable and so the size can be compared with slack instead of being baked in.
            //
            // Observed on a live install: one dead Usenet post grabbed several hundred times over
            // half a day for a single book, identical title and identical size to the byte every
            // time, and a per-fetch download URL that differed every time. A key over the URL
            // could never match; a key over the title does. There is deliberately no URL branch
            // for that reason, and Readarr has none either: its only two keys are the info-hash
            // and the title (src/NzbDrone.Core/Blocklisting/BlocklistService.cs:38-62).
            var normalizedTitle = NormalizeTitle(title);
            return normalizedTitle.Length == 0
                ? null
                : ReleaseIdentifier.ForTitle(normalizedTitle);
        }

        /// <summary>
        /// Pull the info-hash out of a magnet, so a torrent is recognised as the same release even
        /// when the indexer hands back a different URL for it than last time. Returns null unless
        /// what came out is genuinely a v1 info-hash.
        /// </summary>
        internal static string? TorrentHashFrom(string? magnetLink)
        {
            if (string.IsNullOrWhiteSpace(magnetLink))
            {
                return null;
            }

            // Percent-decode first. `xt=urn%3Abtih%3A...` is a real form and searching the raw
            // string for the marker misses it entirely. Decoding also collapses `xt=` and `xt.1=`
            // into one case, because both are followed by the same `urn:btih:`.
            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(magnetLink);
            }
            catch (UriFormatException)
            {
                decoded = magnetLink;
            }

            var index = decoded.IndexOf(MagnetInfoHashMarker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            var rest = decoded[(index + MagnetInfoHashMarker.Length)..];
            var end = rest.IndexOf('&');
            return NormalizeInfoHash(end < 0 ? rest : rest[..end]);
        }

        /// <summary>
        /// A v1 info-hash as 40 lowercase hex characters, from either of the two encodings real
        /// magnets use for it, or null when the input is not an info-hash at all.
        ///
        /// Both encodings have to be accepted or one torrent gets two identities. Sonarr's own
        /// fixtures pin the pair: base32 ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR against hex
        /// CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951, in
        /// src/NzbDrone.Core.Test/Download/DownloadClientTests/QBittorrentTests/QBittorrentFixture.cs:469
        /// and again in
        /// src/NzbDrone.Core.Test/Download/DownloadClientTests/TransmissionTests/TransmissionFixture.cs:146.
        /// Sonarr itself never decodes base32 in its own code; it hands the whole magnet to
        /// MonoTorrent (MagnetLink.Parse(magnetUrl).InfoHash.ToHex(), at
        /// src/NzbDrone.Core/Indexers/TorrentRssParser.cs:85 and
        /// src/NzbDrone.Core/Download/TorrentClientBase.cs:224), which handles both encodings.
        /// Listenarr has no torrent library, so the encoding is decoded here instead; taking a
        /// dependency on one for a forty-character string would be the larger change.
        ///
        /// Validating rather than trusting matters as much as converting. An empty or malformed
        /// hash previously produced the bare key "btih:", which every release with a broken magnet
        /// would then have shared.
        /// </summary>
        internal static string? NormalizeInfoHash(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            var trimmed = candidate.Trim();

            if (trimmed.Length == 40 && IsHex(trimmed))
            {
                return trimmed.ToLowerInvariant();
            }

            if (trimmed.Length == 32)
            {
                return Base32ToHex(trimmed);
            }

            return null;
        }

        /// <summary>
        /// The title reduced to what two listings of one release will agree on: composed form, no
        /// leading or trailing space, runs of whitespace collapsed, case folded.
        ///
        /// Unicode composition is the part that is easy to leave out and expensive to add later.
        /// An accented author or title arrives as U+00E9 from one indexer and as e + U+0301 from
        /// another; without an NFC pass those are two keys for one release, and audiobook release
        /// names carry accents constantly.
        /// </summary>
        internal static string NormalizeTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            var composed = title;
            try
            {
                if (!title.IsNormalized(NormalizationForm.FormC))
                {
                    composed = title.Normalize(NormalizationForm.FormC);
                }
            }
            catch (ArgumentException)
            {
                // Unpaired surrogates cannot be normalised. An unnormalised key for a malformed
                // title beats no key at all.
                composed = title;
            }

            var collapsed = Regex.Replace(composed.Trim(), @"\s+", " ");
            return collapsed.ToLowerInvariant();
        }

        private static bool HasSameTitle(BlockedRelease entry, string? title)
        {
            var candidate = NormalizeTitle(title);
            if (candidate.Length == 0)
            {
                return false;
            }

            // Either title the row carries, not the column with the key's title as a fallback
            // behind it. A row written under an info-hash keeps the advertised title in Title,
            // which is what makes the match available in both directions; a row written under a
            // title carries it twice, once in each place.
            //
            // The two cannot disagree today: Title is written from download.Title, which is
            // assigned once at grab from the same candidate.Title the key came from. But the
            // premise of this design is "two keys, either can match", and reading one of them only
            // when the other is empty is that premise half-applied. Checking both costs a string
            // comparison and removes the case where a future writer sets Title to something
            // friendlier and silently strands the key's own title.
            var storedColumn = NormalizeTitle(entry.Title);
            var storedKey = entry.ReleaseIdentifier.TitleKey;

            return (storedColumn.Length > 0 && string.Equals(storedColumn, candidate, StringComparison.Ordinal))
                || (storedKey.Length > 0 && string.Equals(storedKey, candidate, StringComparison.Ordinal));
        }

        /// <summary>
        /// A size within tolerance, where a size nobody reported is no constraint on either side.
        ///
        /// The stored side is Readarr's, and it is the behaviour a digest cannot express: a
        /// missing stored size means "matches anything"
        /// (src/NzbDrone.Core/Blocklisting/BlocklistService.cs:157-161), whereas a missing field
        /// folded into a hash silently changes the key instead.
        ///
        /// The candidate side is deliberately NOT Readarr's, and the difference is worth stating
        /// because it is the one place this is more tolerant than the line above. Readarr's
        /// HasSameSize takes a non-nullable long, so a zero there is compared as a number and a
        /// sized row does not match it. Listenarr's SearchResult.Size is also a non-nullable long
        /// (listenarr.domain/Search/SearchResult.cs:131), but it defaults to 0, so an indexer that
        /// omits the size attribute produces a candidate reporting zero bytes rather than one
        /// reporting no size. Treating that as a real measurement of zero would mean a dead
        /// release re-listed without a size never matches the row written when it failed, which is
        /// the re-grab loop this feature exists to end. Treating it as "no size supplied" instead
        /// costs at most one over-block, bounded to one book and removable through the delete
        /// endpoint, and it keeps the rule symmetric with the stored side.
        /// </summary>
        private static bool HasSameSize(BlockedRelease entry, long? size)
        {
            if (!entry.Size.HasValue || entry.Size.Value <= 0 || !size.HasValue || size.Value <= 0)
            {
                return true;
            }

            return Math.Abs(entry.Size.Value - size.Value) <= SizeToleranceBytes;
        }

        private static bool IsHex(string value)
        {
            foreach (var character in value)
            {
                var isHexDigit = (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F');
                if (!isHexDigit)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 32 base32 characters decoded to the 20 bytes of a v1 info-hash and re-encoded as hex.
        /// The bit-buffer arrangement is the one in Sonarr's ConvertBase32.FromBase32String
        /// (src/NzbDrone.Common/ConvertBase32.cs:7-32), which is worth reading for the encoding
        /// but is unreferenced anywhere in Sonarr and does not validate: it feeds IndexOf's -1
        /// for an out-of-alphabet character straight into the bit buffer, so bad input comes back
        /// as plausible bytes. An identity used for blocklist matching has to refuse instead.
        /// </summary>
        private static string? Base32ToHex(string value)
        {
            var bytes = new byte[value.Length * 5 / 8];
            var index = 0;
            var buffer = 0;
            var bufferedBits = 0;

            foreach (var character in value.ToUpperInvariant())
            {
                var position = Base32Alphabet.IndexOf(character, StringComparison.Ordinal);
                if (position < 0)
                {
                    return null;
                }

                buffer = (buffer << 5) | position;
                bufferedBits += 5;

                if (bufferedBits >= 8)
                {
                    bufferedBits -= 8;
                    bytes[index++] = (byte)(buffer >> bufferedBits);
                }
            }

            return index == bytes.Length ? Convert.ToHexString(bytes).ToLowerInvariant() : null;
        }
    }
}
