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
using Microsoft.Extensions.Logging.Abstractions;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.Metadata
{
    /// <summary>
    /// Tracker #172 (G2): BookFormat/FormatType "unabridged" contains the substring "abridged",
    /// so a plain Contains() check flags every unabridged book as abridged. These tests pin the
    /// word-boundary fix on both metadata conversion entry points.
    /// </summary>
    [Trait("Name", "MetadataConvertersAbridgedTests")]
    [Trait("Category", "Application")]
    public sealed class MetadataConvertersAbridgedTests : BaseTests
    {
        private static MetadataConverters CreateConverters()
        {
            return new MetadataConverters(null, NullLogger<MetadataConverters>.Instance);
        }

        private static AudibleBookResponse BuildAudibleResponse(string? bookFormat)
        {
            return new AudibleBookResponse
            {
                Asin = "B0TESTASIN",
                Title = "Test Audiobook",
                BookFormat = bookFormat
            };
        }

        private static AudnexusBookResponse BuildAudnexusResponse(string? formatType)
        {
            return new AudnexusBookResponse
            {
                Asin = "B0TESTASIN",
                Title = "Test Audiobook",
                FormatType = formatType
            };
        }

        [Theory]
        [InlineData("unabridged", false)]
        [InlineData("Unabridged", false)]
        [InlineData("UNABRIDGED", false)]
        [InlineData("abridged", true)]
        [InlineData("Abridged", true)]
        [InlineData(null, false)]
        [InlineData("", false)]
        // Established from AudibleSearchResultFilter.cs, which defends against BookFormat
        // holding "podcast": a real, non-abridged value this field can take.
        [InlineData("podcast", false)]
        public void ConvertAudibleToMetadata_DerivesAbridgedByWordBoundary(string? bookFormat, bool expectedAbridged)
        {
            var converters = CreateConverters();
            var audibleData = BuildAudibleResponse(bookFormat);

            var metadata = converters.ConvertAudibleToMetadata(audibleData, "B0TESTASIN");

            Assert.Equal(expectedAbridged, metadata.Abridged);
        }

        [Theory]
        [InlineData("unabridged", false)]
        [InlineData("Unabridged", false)]
        [InlineData("UNABRIDGED", false)]
        [InlineData("abridged", true)]
        [InlineData("Abridged", true)]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("podcast", false)]
        public void ConvertAudnexusToMetadata_DerivesAbridgedByWordBoundary(string? formatType, bool expectedAbridged)
        {
            var converters = CreateConverters();
            var audnexusData = BuildAudnexusResponse(formatType);

            var metadata = converters.ConvertAudnexusToMetadata(audnexusData, "B0TESTASIN");

            Assert.Equal(expectedAbridged, metadata.Abridged);
        }

        // Control: the title is untouched by this fix, proving a broken apparatus (one that
        // always returns Abridged = false regardless of input) would not pass this suite.
        [Fact]
        public void ConvertAudibleToMetadata_TitlePassesThroughUnaffected()
        {
            var converters = CreateConverters();
            var audibleData = BuildAudibleResponse("abridged");
            audibleData.Title = "The Hobbit";

            var metadata = converters.ConvertAudibleToMetadata(audibleData, "B0TESTASIN");

            Assert.Equal("The Hobbit", metadata.Title);
            Assert.True(metadata.Abridged);
        }
    }
}
