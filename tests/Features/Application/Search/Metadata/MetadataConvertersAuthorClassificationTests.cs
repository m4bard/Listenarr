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
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Metadata
{
    /// <summary>
    /// tracker#341 Fix 4 / §5: the role classifier is applied in exactly one place,
    /// AudibleBookMetadata.ToAudiobook(). These two conversions take
    /// metadata.Authors?.FirstOrDefault() raw, so a suffixed contributor who happens to be
    /// listed first in the provider's byline surfaces as the search result's author instead of
    /// the real first author.
    /// </summary>
    [Trait("Name", "MetadataConvertersAuthorClassificationTests")]
    [Trait("Category", "Application")]
    public class MetadataConvertersAuthorClassificationTests : BaseTests
    {
        private static MetadataConverters Converter() =>
            new(imageCacheService: null,
                NullLogger<MetadataConverters>.Instance,
                requestContextAccessor: null);

        private static AudibleBookMetadata MetadataWithAuthors(params string[] authors) => new()
        {
            Title = "A Title",
            Authors = authors.ToList(),
        };

        [Fact]
        public async Task ConvertMetadataToSearchResultAsync_PicksTheAuthorNotTheTranslatorListedFirst()
        {
            var metadata = MetadataWithAuthors("Constance Garnett - translator", "Fyodor Dostoevsky");

            var result = await Converter().ConvertMetadataToSearchResultAsync(metadata, asin: "TEST-ASIN-1");

            Assert.Equal("Fyodor Dostoevsky", result.Author);
        }

        [Fact]
        public async Task ConvertMetadataToSearchResultAsync_StripsTheRoleWhenEveryCreditNamesOne()
        {
            // tracker#341: when nobody survives unsuffixed, the credit is corrected rather than
            // discarded, same answer AuthorCredits.Primary already gives.
            var metadata = MetadataWithAuthors("Jane Doe - editor");

            var result = await Converter().ConvertMetadataToSearchResultAsync(metadata, asin: "TEST-ASIN-2");

            Assert.Equal("Jane Doe", result.Author);
        }

        [Fact]
        public async Task ConvertMetadataToSearchResultAsync_LeavesAnOrdinaryByelineAlone()
        {
            var metadata = MetadataWithAuthors("Someone Else");

            var result = await Converter().ConvertMetadataToSearchResultAsync(metadata, asin: "TEST-ASIN-3");

            Assert.Equal("Someone Else", result.Author);
        }

        [Fact]
        public async Task ConvertMetadataToMetadataSearchResultAsync_PicksTheAuthorNotTheTranslatorListedFirst()
        {
            var metadata = MetadataWithAuthors("Constance Garnett - translator", "Fyodor Dostoevsky");

            var result = await Converter().ConvertMetadataToMetadataSearchResultAsync(metadata, asin: "TEST-ASIN-4");

            Assert.Equal("Fyodor Dostoevsky", result.Author);
        }

        [Fact]
        public async Task ConvertMetadataToMetadataSearchResultAsync_StripsTheRoleWhenEveryCreditNamesOne()
        {
            var metadata = MetadataWithAuthors("Jane Doe - editor");

            var result = await Converter().ConvertMetadataToMetadataSearchResultAsync(metadata, asin: "TEST-ASIN-5");

            Assert.Equal("Jane Doe", result.Author);
        }

        [Fact]
        public async Task ConvertMetadataToSearchResultAsync_FallsBackWhenNoAuthorSurvives()
        {
            // The control: when there are no credits at all, the existing fallback chain
            // (fallbackAuthor, then "Unknown Author") still has to run. This fix must not
            // disturb that path.
            var metadata = new AudibleBookMetadata { Title = "A Title", Authors = new List<string>() };

            var result = await Converter().ConvertMetadataToSearchResultAsync(
                metadata, asin: "TEST-ASIN-6", fallbackAuthor: "Fallback Author");

            Assert.Equal("Fallback Author", result.Author);
        }
    }
}
