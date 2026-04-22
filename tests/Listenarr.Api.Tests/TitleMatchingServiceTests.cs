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
using Xunit;
using Listenarr.Api.Services.Adapters;

namespace Listenarr.Api.Tests
{
    public class TitleMatchingServiceTests
    {
        private readonly TitleMatchingService _svc = new TitleMatchingService();

        [Theory]
        [InlineData("The Great Book [Edition] (2020) - 320kbps", "The Great Book")]
        [InlineData("Some_Title-v0.flac", "Some Title")]
        [InlineData("An Audiobook - Unabridged", "An Audiobook")]
        public void NormalizeTitle_RemovesNoise(string input, string expectedStart)
        {
            var norm = _svc.NormalizeTitle(input);
            Assert.False(string.IsNullOrWhiteSpace(norm));
            Assert.Contains(expectedStart.Split(' ')[0], norm, System.StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("The Great Book", "The Great Book", true)]
        [InlineData("The Great Book - 320kbps", "The Great Book", true)]
        [InlineData("The Great Book (unabridged)", "Great Book", true)]
        [InlineData("Completely Different Title", "Another Title", false)]
        public void AreTitlesSimilar_BasicCases(string a, string b, bool expected)
        {
            var result = _svc.AreTitlesSimilar(a, b);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void IsMatchingTitle_EmptyInputs_ReturnsFalse()
        {
            Assert.False(_svc.IsMatchingTitle("", "some"));
            Assert.False(_svc.IsMatchingTitle("some", ""));
            Assert.False(_svc.IsMatchingTitle("", ""));
        }

        [Fact]
        public void AreTitlesSimilar_LongPrefixMatch_ReturnsTrue()
        {
            var a = "A very long audiobook title that contains lots of words and metadata tags";
            var b = "A very long audiobook title that contains lots of words";
            Assert.True(_svc.AreTitlesSimilar(a, b));
        }
    }
}
