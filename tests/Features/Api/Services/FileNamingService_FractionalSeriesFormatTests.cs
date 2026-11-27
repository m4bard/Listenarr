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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Services
{
    /// <summary>
    /// A {Token:Format} applied to a fractional series position such as 1.5.
    /// </summary>
    /// <remarks>
    /// Whole numbers, empty values and non-numeric positions are pinned alongside the fractional
    /// cases so a change that fixed 1.5 by touching the existing paths would fail here too.
    /// </remarks>
    [Trait("Name", nameof(FileNamingService_FractionalSeriesFormatTests))]
    [Trait("Category", "FileNamingService")]
    public class FileNamingService_FractionalSeriesFormatTests : BaseTests
    {
        private readonly FileNamingService _service;

        public FileNamingService_FractionalSeriesFormatTests()
        {
            _service = new FileNamingService(
                new Mock<IConfigurationService>().Object,
                new Mock<ILogger<FileNamingService>>().Object);
        }

        private string Render(string pattern, object seriesNumber) =>
            _service.ApplyNamingPattern(
                pattern,
                new Dictionary<string, object> { { "SeriesNumber", seriesNumber }, { "Title", "Title" } });

        [Theory]
        [InlineData("1", "[#1]")]
        [InlineData("1.5", "[#1.5]")]
        [InlineData("1.50", "[#1.5]")]
        [InlineData("01", "[#1]")]
        [InlineData("2.0", "[#2]")]
        public void LiteralPrefixFormat_RendersWholeAndFractionalPositions(string position, string expected)
        {
            Assert.Equal(expected, Render("[{SeriesNumber:'#'0.##}]", position));
        }

        [Fact]
        public void LiteralPrefixFormat_OmitsTheSegmentWhenEmpty()
        {
            Assert.Equal("Title", Render("{Title} [{SeriesNumber:'#'0.##}]", ""));
        }

        [Theory]
        [InlineData("1-4", "{SeriesNumber:'#'0.##}", "1-4")]
        [InlineData("1-4", "{SeriesNumber:00}", "1-4")]
        [InlineData("1.5", "{SeriesNumber}", "1.5")]
        [InlineData("2.0", "{SeriesNumber}", "2.0")]
        public void NonNumericAndPlainTokens_StayText(string position, string pattern, string expected)
        {
            Assert.Equal(expected, Render(pattern, position));
        }

        [Theory]
        [InlineData("1", "01")]
        [InlineData("01", "01")]
        [InlineData("12", "12")]
        public void IntegerFormat_OnWholeNumbers_IsUnchanged(string position, string expected)
        {
            Assert.Equal(expected, Render("{SeriesNumber:00}", position));
            Assert.Equal(expected, Render("{SeriesNumber:00}", int.Parse(position, CultureInfo.InvariantCulture)));
        }

        [Fact]
        public void IntegerFormat_OnFraction_KeepsTheFractionRatherThanRoundingIntoTheNextBook()
        {
            Assert.Equal("1.5", Render("{SeriesNumber:00}", "1.5"));
        }

        [Fact]
        public void FractionFormat_KeepsAFractionTheFormatWouldRoundAway()
        {
            Assert.Equal("1.04", Render("{SeriesNumber:0.#}", "1.04"));
            Assert.Equal("1.3", Render("{SeriesNumber:0.#}", "1.25"));
        }

        [Fact]
        public void WholeValuedDecimal_UnderIntegerFormat_PadsLikeTheWholeNumber()
        {
            Assert.Equal("02", Render("{SeriesNumber:00}", "2.0"));
        }

        [Theory]
        [InlineData("1,5")]
        [InlineData("1e2")]
        [InlineData("99999999999999999999999999999.5")]
        public void UnparseablePositions_StayText_NotReinterpreted(string position)
        {
            Assert.Equal(position, Render("{SeriesNumber:'#'0.##}", position));
        }

        [Theory]
        [InlineData("-1.5", "1.5")]
        [InlineData(" 1.5", "1.5")]
        public void SignedOrPaddedPositions_AreNotParsed_SoNoFormatIsApplied(string position, string expectedAfterSanitizing)
        {
            // The existing path sanitizer trims the sign and the padding; the point is that the
            // literal prefix of the format never appears.
            Assert.Equal(expectedAfterSanitizing, Render("{SeriesNumber:'#'0.##}", position));
        }

        [Fact]
        public void InvalidFormat_OnFraction_FallsBackToText()
        {
            Assert.Equal("1.5", Render("{SeriesNumber:Z}", "1.5"));
        }

        [Theory]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        public void Rendering_IsCultureInvariant(string cultureName)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(cultureName);

                Assert.Equal("[#1.5]", Render("[{SeriesNumber:'#'0.##}]", "1.5"));
                Assert.Equal("[#1]", Render("[{SeriesNumber:'#'0.##}]", "1"));
                Assert.Equal("1,5", Render("{SeriesNumber:'#'0.##}", "1,5"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void IntegerPaths_FormatWithInvariantCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                const string expected = "1,234";
                Assert.Equal("1.234", 1234.ToString("N0"));
                Assert.Equal(expected, Render("{SeriesNumber:N0}", 1234));
                Assert.Equal(expected, Render("{SeriesNumber:N0}", "1234"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void FractionalSeriesPosition_UnderGermanCulture_RendersTheIssueExample()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                Assert.Equal("Series [#1.5] - Title", _service.ApplyNamingPattern(
                    "{Series} [{SeriesNumber:'#'0.##}] - {Title}",
                    new Dictionary<string, object> { { "Series", "Series" }, { "SeriesNumber", "1.5" }, { "Title", "Title" } }));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }
}
