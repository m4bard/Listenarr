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

using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search
{
    [Trait("Area", "Search")]
    [Trait("Name", "SearchServiceTests")]
    [Trait("Category", "SearchService")]
    public class SearchServiceTests : BaseTests
    {
        [Fact]
        [Trait("Method", "IntelligentSearchAsync")]
        [Trait("Scenario", "AudibleTitleResultUsesRequestedRegionForLinks")]
        public async Task IntelligentSearch_TitleAudibleResult_UsesRequestedRegionForProductLinks()
        {
            // Given
            using var httpClient = new HttpClient();
            var audible = new Mock<AudibleService>(httpClient, NullLogger<AudibleService>.Instance);
            var audibleResult = new AudibleSearchResultBuilder()
                .WithAsin("B0DUNE1234")
                .WithTitle("Dune")
                .WithAuthor("Frank Herbert")
                .WithLanguage("german")
                .Build();
            var audibleResponse = new AudibleSearchResponseBuilder()
                .WithResult(audibleResult)
                .WithTotalResults(1)
                .Build();

            audible
                .Setup(service => service.SearchByTitleAsync("Dune", 1, 50, "de", "german"))
                .ReturnsAsync(audibleResponse);

            Init(services => services.WithSingleton<AudibleService>(audible.Object));
            var searchService = _provider.GetRequiredService<ISearchService>();

            // When
            var results = await searchService.IntelligentSearchAsync("TITLE:Dune", region: "de", language: "german");

            // Then
            var result = Assert.Single(results);
            Assert.Equal("https://www.audible.de/pd/B0DUNE1234", result.ProductUrl);
            Assert.Equal("https://www.audible.de/pd/B0DUNE1234", result.SourceLink);
            Assert.Equal("Audible", result.MetadataSource);
            audible.Verify(service => service.SearchByTitleAsync("Dune", 1, 50, "de", "german"), Times.Once);
        }

        [Fact]
        [Trait("Method", "IntelligentSearchAsync")]
        [Trait("Scenario", "AudibleFirstAttemptSkippedWhenDisabled")]
        public async Task IntelligentSearch_SkipsAudibleFirstAttempt_WhenAudibleSearchDisabled()
        {
            // Given
            using var httpClient = new HttpClient();
            var audible = new Mock<AudibleService>(httpClient, NullLogger<AudibleService>.Instance);
            var audibleResult = new AudibleSearchResultBuilder()
                .WithAsin("B0DUNE1234")
                .WithTitle("Dune")
                .WithAuthor("Frank Herbert")
                .Build();
            var audibleResponse = new AudibleSearchResponseBuilder()
                .WithResult(audibleResult)
                .WithTotalResults(1)
                .Build();

            audible
                .Setup(service => service.SearchByTitleAsync("Dune", 1, 50, "us", null))
                .ReturnsAsync(audibleResponse);

            Init(services => services.WithSingleton<AudibleService>(audible.Object));

            var settings = new ApplicationSettingsBuilder().WithoutAudibleSearch().Build();
            settings.EnableOpenLibrarySearch = false;
            await _applicationSettingsRepository.SaveAsync(settings);

            var searchService = _provider.GetRequiredService<ISearchService>();

            // When
            var results = await searchService.IntelligentSearchAsync("TITLE:Dune");

            // Then
            Assert.Empty(results);
            audible.Verify(service => service.SearchByTitleAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        [Trait("Method", "IntelligentSearchAsync")]
        [Trait("Scenario", "DirectAsinLookupSkippedWhenAmazonSearchDisabled")]
        public async Task IntelligentSearch_SkipsDirectAsinLookup_WhenAmazonSearchDisabled()
        {
            // Given
            using var httpClient = new HttpClient();
            var audible = new Mock<AudibleService>(httpClient, NullLogger<AudibleService>.Instance);
            audible
                .Setup(service => service.GetBookMetadataAsync("B0TEST1234", "us", true, null))
                .ReturnsAsync(new AudibleBookResponse
                {
                    Asin = "B0TEST1234",
                    Title = "Region Test",
                    Authors = new List<AudibleAuthor> { new() { Name = "Test Author" } }
                });

            Init(services => services.WithSingleton<AudibleService>(audible.Object));

            var settings = new ApplicationSettingsBuilder().WithoutAmazonSearch().Build();
            await _applicationSettingsRepository.SaveAsync(settings);

            var searchService = _provider.GetRequiredService<ISearchService>();

            // When
            var results = await searchService.IntelligentSearchAsync("ASIN:B0TEST1234");

            // Then
            Assert.Empty(results);
            audible.Verify(service => service.GetBookMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
        }
    }
}
