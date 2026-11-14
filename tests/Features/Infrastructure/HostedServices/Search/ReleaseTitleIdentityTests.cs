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

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search
{
    [Trait("Name", "ReleaseTitleIdentityTests")]
    [Trait("Category", "Infrastructure")]
    public class ReleaseTitleIdentityTests : BaseTests
    {
        [Theory]
        [InlineData("A Princess of Mars", null, "A PRINCESS OF MARS")]
        [InlineData("Edgar Rice Burroughs - A Princess of Mars", "Edgar Rice Burroughs", "A PRINCESS OF MARS")]
        [InlineData("Edgar Rice Burroughs - A Princess of Mars - 2012", "Edgar Rice Burroughs", "A PRINCESS OF MARS")]
        [InlineData("Edgar Rice Burroughs - A Princess of Mars - 2012 - [M4B]", "Edgar Rice Burroughs", "A PRINCESS OF MARS")]
        [InlineData("A Princess of Mars - M4B", null, "A PRINCESS OF MARS")]
        [InlineData("  Edgar Rice Burroughs – A Princess of Mars  ", "Edgar Rice Burroughs", "A PRINCESS OF MARS")]
        public void TryParse_SplitsAuthorFromTitleAndDropsSuffixes(
            string title, string? expectedAuthor, string expectedKey)
        {
            Assert.True(ReleaseTitleIdentity.TryParse(title, out var author, out var key));
            Assert.Equal(expectedAuthor, author);
            Assert.Equal(expectedKey, key);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("  ")]
        [InlineData("Edgar Rice Burroughs - A Princess of Mars - Barsoom")]
        public void TryParse_UnsupportedLayout_IsRejected(string? title)
        {
            Assert.False(ReleaseTitleIdentity.TryParse(title, out _, out _));
        }
    }
}
