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
namespace Listenarr.Tests.Features.Domain.Utils
{
    public class StringUtilsNormalizeAuthorNameTests
    {
        // Previously known-good: bare period stripping already made these match.
        [Fact]
        public void NormalizeAuthorName_DottedInitials_MatchesUndottedInitials()
        {
            Assert.Equal(
                StringUtils.NormalizeAuthorName("J.N. Chaney"),
                StringUtils.NormalizeAuthorName("JN Chaney"));
        }

        // Regression cases: before the fix, stripping periods left the space between spaced-out
        // initials behind, so "J. N. Chaney" normalized to "j n chaney" while "J.N. Chaney"
        // normalized to "jn chaney" -- different strings for the same person. Verified failing
        // against the pre-fix implementation (reflection probe against the private methods in
        // AudiobookRepository and AuthorMonitoringService.Mapping) before this fix was written.
        [Theory]
        [InlineData("James S.A. Corey", "James S. A. Corey")]
        [InlineData("D.J. Molles", "D. J. Molles")]
        [InlineData("P P Corcoran", "PP Corcoran")]
        [InlineData("J.N. Chaney", "J. N. Chaney")]
        public void NormalizeAuthorName_SpacedInitials_MatchesTightInitials(string a, string b)
        {
            Assert.Equal(StringUtils.NormalizeAuthorName(a), StringUtils.NormalizeAuthorName(b));
        }

        // All six pairs (the two known-good above plus the four regression cases) normalize to
        // the same key within each pair.
        [Theory]
        [InlineData("J.N. Chaney", "JN Chaney")]
        [InlineData("James S.A. Corey", "James S. A. Corey")]
        [InlineData("D.J. Molles", "D. J. Molles")]
        [InlineData("P P Corcoran", "PP Corcoran")]
        [InlineData("J.N. Chaney", "J. N. Chaney")]
        public void NormalizeAuthorName_AllSixPairs_NormalizeIdentically(string a, string b)
        {
            Assert.Equal(StringUtils.NormalizeAuthorName(a), StringUtils.NormalizeAuthorName(b));
        }

        // Overcorrection guard: two different people who happen to share initials but not a
        // surname must still normalize differently. The initials-merging fix must not be
        // permissive enough to blur distinct authors together.
        [Fact]
        public void NormalizeAuthorName_SameInitialsDifferentSurname_StillDiffer()
        {
            Assert.NotEqual(
                StringUtils.NormalizeAuthorName("J. N. Chaney"),
                StringUtils.NormalizeAuthorName("J. N. Smith"));
        }

        [Fact]
        public void NormalizeAuthorName_NullOrWhitespace_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, StringUtils.NormalizeAuthorName(null));
            Assert.Equal(string.Empty, StringUtils.NormalizeAuthorName("   "));
        }
    }
}
