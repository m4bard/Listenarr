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
using Listenarr.Domain.Common;

namespace Listenarr.Domain.Audiobooks.Rules
{
    /// <summary>
    /// One spelling replaced by another, for the caller to log. The caller owns the logging
    /// because only it knows which book the correction belongs to and how its titles are redacted.
    /// </summary>
    public sealed record AuthorSpellingCorrection(string Stored, string Canonical);

    public static class AuthorNameCanonicalization
    {
        /// <summary>
        /// Rewrites entries of <paramref name="authors"/> to <paramref name="candidateName"/>
        /// where the stored string and the candidate differ only in ways
        /// <see cref="StringUtils.NormalizeAuthorName"/> already erases, and returns what changed.
        ///
        /// Both callers reach this after a successful remote author lookup, which returns the
        /// provider's own spelling of the name alongside the ASIN. That name used to be discarded
        /// a few lines before the ASIN it came with was stored, which is why two books by one
        /// author could keep two spellings indefinitely.
        ///
        /// The lookup's own predicate accepts a bare substring in either direction, so
        /// "Conan Doyle" resolves "Sir Arthur Conan Doyle" and hands back the shorter name. The
        /// guard here is deliberately narrower than the thing that produced the candidate: this
        /// can change how an author is spelled and it cannot change who the author is. A pen
        /// name, a translator credited in the author field, or a deliberate honorific survives.
        /// </summary>
        public static IReadOnlyList<AuthorSpellingCorrection> AdoptSpelling(
            IList<string>? authors,
            string? lookedUpName,
            string? candidateName)
        {
            if (authors == null
                || authors.Count == 0
                || string.IsNullOrWhiteSpace(candidateName)
                || !StringUtils.IsAuthorSpellingVariant(lookedUpName, candidateName))
            {
                return [];
            }

            var canonical = candidateName.Trim();
            List<AuthorSpellingCorrection>? corrections = null;
            for (var index = 0; index < authors.Count; index++)
            {
                var stored = authors[index];
                if (string.Equals(stored, canonical, StringComparison.Ordinal)
                    || !StringUtils.IsAuthorSpellingVariant(stored, canonical))
                {
                    continue;
                }

                authors[index] = canonical;
                corrections ??= [];
                corrections.Add(new AuthorSpellingCorrection(stored, canonical));
            }

            return corrections ?? (IReadOnlyList<AuthorSpellingCorrection>)[];
        }
    }
}
