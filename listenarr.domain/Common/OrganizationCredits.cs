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

using System.Text.RegularExpressions;

namespace Listenarr.Domain.Common
{
    /// <summary>
    /// Detects a credited "author" that is actually a publisher or imprint.
    /// </summary>
    /// <remarks>
    /// tracker#341 Fix 2.5 ("Organizations credited as authors are mechanically detectable and
    /// are NOT a non-English-only problem"). The finding measured six library rows, English and
    /// Spanish, where the credited name in author position is a publisher or imprint rather than
    /// a person, and found the detection splits into two independent tests that have to be
    /// combined, not used alone:
    ///
    /// 1. The name carries an organization-suffix word (Press, Publishing, Media, Studios,
    /// Verlag, Edizioni, Éditions, Ltd, LLC, GmbH, …). This alone is safe: the finding records no
    /// false positive from it.
    ///
    /// 2. The name matches the book's <c>publisher_name</c> exactly. This one is NOT safe alone:
    /// the finding measured four false positives in the same dataset, all self-published authors
    /// whose imprint is their own name. tracker#341's own fix for that is to require that the
    /// name does NOT also appear as a first-position author anywhere else — a self-published
    /// author will have other first-position credits under their own name; an imprint masquerading
    /// as an author will not.
    ///
    /// This class only has real, wired evidence for test 1. Test 2's exception clause
    /// ("the name does not also appear as a first-position author elsewhere") needs a
    /// cross-book lookup this change does not build: it is the same "needs infrastructure this
    /// change does not have" gap as the cross-source half of <see cref="AuthorCredits"/>'s
    /// bare-anywhere-wins rule. <see cref="IsOrganizationCredit"/> takes that lookup as a
    /// parameter so the combined test is correct and ready once a caller can supply it, rather
    /// than guessing at the wiring. No call site in this change passes a real implementation of
    /// it; see the commit message for why evaluating test 2 unconditionally would reintroduce
    /// the false positive the finding measured.
    /// </remarks>
    public static class OrganizationCredits
    {
        // The vocabulary tracker#341 §9 cites verbatim, with "…" marking it as non-exhaustive.
        // Kept to exactly the cited list rather than extended, since anything added here is a
        // judgement call the finding did not make.
        private const string OrganizationSuffixWords =
            "press|publishing|media|studios|verlag|edizioni|éditions|ltd|llc|gmbh";

        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

        private static readonly Regex OrganizationSuffix = new(
            "\\b(?:" + OrganizationSuffixWords + ")\\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            MatchTimeout);

        /// <summary>
        /// True when a credited name itself carries an organization-type word.
        /// </summary>
        /// <remarks>
        /// This is the half of the combined test that tracker#341 found safe on its own: no
        /// false positive was measured against it in the finding's dataset.
        /// </remarks>
        public static bool HasOrganizationVocabulary(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            try
            {
                return OrganizationSuffix.IsMatch(name);
            }
            catch (RegexMatchTimeoutException)
            {
                // Same reasoning as AuthorCredits: on a pathological input, answering "not an
                // organization" keeps a person rather than discarding a credit on a timeout.
                return false;
            }
        }

        /// <summary>
        /// The combined test from tracker#341 §9: an organization-vocabulary match on its own,
        /// or a publisher-name match guarded by the first-position exception.
        /// </summary>
        /// <param name="name">The credited name under test.</param>
        /// <param name="publisherName">The book's publisher, if known.</param>
        /// <param name="hasOtherFirstPositionCredit">
        /// Whether <paramref name="name"/> is independently known to appear as a first-position
        /// author on some other record. This is the exception tracker#341 requires before
        /// treating a publisher-name match as an organization: without it, a self-published
        /// author whose imprint is their own name is misclassified, which the finding measured
        /// four times in the same dataset. Pass <c>true</c> only when a caller has actually
        /// checked this; the safe default for "not checked" is <c>false</c>, which, combined with
        /// publisher equality, would call every self-published author an organization. No
        /// production call site in this change supplies anything but a vocabulary match for
        /// that reason — see the type-level remarks.
        /// </param>
        public static bool IsOrganizationCredit(
            string? name,
            string? publisherName,
            bool hasOtherFirstPositionCredit)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (HasOrganizationVocabulary(name))
            {
                return true;
            }

            if (hasOtherFirstPositionCredit)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(publisherName)
                && string.Equals(name.Trim(), publisherName.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
