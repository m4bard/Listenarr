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
using Listenarr.Api.Dtos.ManualImport;

namespace Listenarr.Tests.Dtos.ManualImport
{
    public class ManualImportItemDtoTests
    {
        [Theory]
        [InlineData("/library/Stalky and Co./track.m4b")]
        [InlineData("/library/Alice's Adventures in Wonderland/01 - Down the Rabbit-Hole.m4b")]
        public void FullPath_SegmentEndingInPeriod_IsAccepted(string path)
        {
            var dto = new ManualImportItemDto { FullPath = path };

            Assert.NotNull(dto.FullPath);
        }

        [Theory]
        [InlineData("../etc/passwd")]
        [InlineData("foo/../bar")]
        [InlineData("foo\\..\\bar")]
        [InlineData("/library/../etc/passwd")]
        public void FullPath_TraversalSegment_ThrowsArgumentException(string path)
        {
            var ex = Assert.Throws<ArgumentException>(() => new ManualImportItemDto { FullPath = path });

            Assert.StartsWith("Path traversal attempts are not allowed.", ex.Message);
            Assert.Equal("value", ex.ParamName);
        }

        [Fact]
        public void FullPath_InvalidPathChars_ThrowsArgumentException()
        {
            var invalidChar = Path.GetInvalidPathChars().First();
            var path = $"/library/book{invalidChar}name/track.m4b";

            var ex = Assert.Throws<ArgumentException>(() => new ManualImportItemDto { FullPath = path });

            Assert.StartsWith("Path is empty or contains invalid characters.", ex.Message);
            Assert.Equal("value", ex.ParamName);
        }

        [Fact]
        public void FullPath_NullOrWhitespace_SetsNull()
        {
            var dto = new ManualImportItemDto { FullPath = "   " };

            Assert.Null(dto.FullPath);
        }
    }
}
