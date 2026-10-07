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

namespace Listenarr.Tests.Features.Application.Audiobooks.Catalog
{
    /// <summary>
    /// tracker#341 Fix 4 / §5: "the classifier is not applied ... on author cache keys."
    /// GetCatalogAsync used the raw `name` argument, trimmed only, everywhere: the author
    /// lookup, the direct-catalog fetch, and the persisted AuthorCacheEntry's name and
    /// normalized key. A caller that passes a role-suffixed byline (reachable from search, per
    /// the finding) got its own cache row and its own author lookup keyed on "Jane Doe -
    /// introduction" rather than being folded into the existing "Jane Doe" entry.
    /// </summary>
    [Trait("Name", "AuthorCatalogServiceAuthorClassificationTests")]
    [Trait("Category", "Application")]
    public class AuthorCatalogServiceAuthorClassificationTests : Listenarr.Tests.Common.BaseTests
    {
        private static readonly HttpClient SharedHttpClient = new();

        [Fact]
        public async Task GetCatalogAsync_StripsTheRoleSuffixBeforeLookingUpOrCachingTheAuthor()
        {
            var audible = new Mock<AudibleService>(SharedHttpClient, Mock.Of<ILogger<AudibleService>>()) { CallBase = false };
            var audnexus = new Mock<IAudnexusService>();
            var audiobookRepository = new Mock<IAudiobookRepository>();
            var searchService = new Mock<ISearchService>();
            var logger = new Mock<ILogger<AuthorCatalogService>>();

            audible
                .Setup(service => service.LookupAuthorAsync("Jane Doe", "us"))
                .ReturnsAsync(new AuthorLookupItem { Asin = "AUTHORTESTCACHE1", Name = "Jane Doe" });

            audible
                .Setup(service => service.GetAllBooksByAuthorAsync("Jane Doe", "AUTHORTESTCACHE1", It.IsAny<int>(), "us", null))
                .ReturnsAsync(new AudibleSearchResponse
                {
                    Results = new List<AudibleSearchResult>(),
                    TotalResults = 0
                });

            AuthorCacheEntry? persisted = null;
            audiobookRepository
                .Setup(repo => repo.UpsertCachedAuthorAsync(It.IsAny<AuthorCacheEntry>()))
                .Callback<AuthorCacheEntry>(entry => persisted = entry)
                .ReturnsAsync((AuthorCacheEntry entry) => entry);

            var service = new AuthorCatalogService(
                audible.Object,
                audnexus.Object,
                audiobookRepository.Object,
                searchService.Object,
                logger.Object);

            await service.GetCatalogAsync("Jane Doe - introduction", "us", limit: 10);

            audible.Verify(s => s.LookupAuthorAsync("Jane Doe", "us"), Times.Once);
            audible.Verify(s => s.LookupAuthorAsync("Jane Doe - introduction", "us"), Times.Never);

            Assert.NotNull(persisted);
            Assert.Equal("jane doe", persisted!.AuthorNameNormalized);
            Assert.DoesNotContain("introduction", persisted.AuthorNameNormalized);
        }
    }
}
