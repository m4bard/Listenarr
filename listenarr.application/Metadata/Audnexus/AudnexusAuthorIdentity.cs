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

namespace Listenarr.Application.Metadata.Audnexus
{
    /// <summary>
    /// Decides which row of an audnexus author search, if any, is the author that was asked for.
    /// </summary>
    /// <remarks>
    /// <c>GET /authors?name=</c> is a fuzzy search. Asked for a name the provider holds no
    /// identifier for, it answers with people who merely share a word of it, in an order that is
    /// not stable between calls, and it never claims any of them is the person asked for. Asked
    /// six times for the same translator it put a different stranger first nearly every time.
    ///
    /// So a row is an identity only when it names that person, or when it carries an identifier
    /// the caller already holds, which is a match on a key rather than on a guess. Everything
    /// else is somebody else, and the answer is to have no identifier: callers already treat a
    /// missing one as a skip.
    ///
    /// This is the family's rule as well. No *arr binds a free-text name to a provider id on an
    /// automatic path: Readarr's by-name author search has two callers and both hand a list of
    /// candidates to a person to choose from, and every path that persists one takes an
    /// identifier and refuses rather than substitutes when it does not resolve. The one place
    /// Sonarr does take the first row of a search, in import list sync, queries by an exact
    /// identifier and logs "Rejected, unable to find TVDB ID" when nothing comes back.
    /// </remarks>
    public static class AudnexusAuthorIdentity
    {
        private static readonly CompareInfo CompareInfo = CultureInfo.InvariantCulture.CompareInfo;

        // The same comparison the Audible side confirms a contributor with. Case and diacritics
        // are spelling, not identity: one provider writes "Jose Saramago" and the other writes
        // "Jose Saramago" with the accent, and refusing that pair means refusing to resolve the
        // author at all. Nothing looser than this, because the loose forms are what bound a
        // stranger in the first place.
        private const CompareOptions DiacriticIgnore = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

        /// <summary>
        /// The row that is <paramref name="askedName"/>, or null when none of them is.
        /// </summary>
        /// <param name="candidates">What the author search answered with.</param>
        /// <param name="askedName">The name the search was made for.</param>
        /// <param name="heldAsin">
        /// An identifier the caller already resolved by some other route, if it has one. A row
        /// carrying it is accepted because it is the same record, whatever it is named.
        /// </param>
        public static AudnexusAuthorSearchResult? Select(
            IReadOnlyList<AudnexusAuthorSearchResult>? candidates,
            string? askedName,
            string? heldAsin = null)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            var named = candidates.FirstOrDefault(candidate => NamesAgree(candidate.Name, askedName));

            if (named != null)
            {
                return named;
            }

            if (string.IsNullOrWhiteSpace(heldAsin))
            {
                return null;
            }

            return candidates.FirstOrDefault(candidate =>
                !string.IsNullOrWhiteSpace(candidate.Asin) &&
                string.Equals(candidate.Asin, heldAsin, StringComparison.OrdinalIgnoreCase));
        }

        private static bool NamesAgree(string? candidateName, string? askedName)
        {
            if (string.IsNullOrWhiteSpace(candidateName) || string.IsNullOrWhiteSpace(askedName))
            {
                return false;
            }

            return CompareInfo.Compare(candidateName.Trim(), askedName.Trim(), DiacriticIgnore) == 0;
        }
    }
}
