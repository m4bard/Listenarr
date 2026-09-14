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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Utils
{
    [Trait("Name", "StringUtilsMatchesAuthorKeyTests")]
    [Trait("Category", "Domain")]
    public class StringUtilsMatchesAuthorKeyTests : BaseTests
    {
        [Fact]
        public void MatchesAuthorKey_StoredKeyEqualsTheQuery_Matches()
        {
            Assert.True(StringUtils.MatchesAuthorKey("andy weir", "Andy Weir", "andy weir"));
        }

        [Fact]
        public void MatchesAuthorKey_StoredKeyWrittenByAnEarlierNormalizer_StillMatches()
        {
            // "j n chaney" is what the pre-unification normalizer produced. The row is still that
            // author's row, and refusing to recognize it would report a conflict that is not one.
            Assert.True(StringUtils.MatchesAuthorKey("j n chaney", "J. N. Chaney", "jn chaney"));
        }

        [Fact]
        public void MatchesAuthorKey_DifferentAuthor_DoesNotMatch()
        {
            Assert.False(StringUtils.MatchesAuthorKey("author one", "Author One", "author two"));
        }

        [Fact]
        public void MatchesAuthorKey_EmptyQuery_DoesNotMatch()
        {
            // An empty incoming key means "no name supplied", which is never evidence of a clash.
            Assert.False(StringUtils.MatchesAuthorKey("", "", ""));
        }
    }
}
