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
using Listenarr.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Application.Audiobooks.Monitoring
{
    /// <summary>
    /// tracker#341 Fix 4 / §5, the second "author cache keys" site: MonitorAuthorAsync built
    /// MonitoredAuthor.AuthorNameNormalized (and its display AuthorName) from the raw request
    /// name. A role-suffixed byline reaching this call -- the finding's own example is a
    /// "Monitor this author" action taken straight off a search result chip -- got its own
    /// MonitoredAuthors row and its own clickable chip instead of being folded into the real
    /// author.
    /// </summary>
    [Trait("Name", "AuthorMonitoringServiceCacheKeyTests")]
    [Trait("Category", "Application")]
    public class AuthorMonitoringServiceCacheKeyTests : Listenarr.Tests.Common.BaseTests
    {
        [Fact]
        public async Task MonitorAuthorAsync_StripsTheRoleSuffixFromTheStoredAndNormalizedName()
        {
            var dbOptions = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(databaseName: $"author-monitor-cachekey-{Guid.NewGuid():N}")
                .Options;
            await using var dbContext = new ListenArrDbContext(dbOptions);

            var authorCatalogService = new Mock<IAuthorCatalogService>();
            authorCatalogService
                .Setup(s => s.GetCatalogAsync("Jane Doe", "us", 500, null, false, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AuthorCatalogFetchResult
                {
                    Author = new AuthorLookupItem { Asin = "AUTHORTESTCACHE2", Name = "Jane Doe" },
                    Books = new List<AudibleSearchResult>(),
                });

            var service = new AuthorMonitoringService(
                new EfMonitoredAuthorRepository(dbContext),
                new AudiobookRepository(dbContext),
                authorCatalogService.Object,
                Mock.Of<ILibraryAddService>(),
                Mock.Of<ILogger<AuthorMonitoringService>>());

            var result = await service.MonitorAuthorAsync(new MonitorAuthorRequest
            {
                Name = "Jane Doe - introduction",
                Region = "us",
                Language = "english",
            });

            Assert.NotNull(result.MonitoredAuthor);
            Assert.Equal("Jane Doe", result.MonitoredAuthor!.AuthorName);
            Assert.Equal("jane doe", result.MonitoredAuthor.AuthorNameNormalized);
        }

        [Fact]
        public async Task GetMonitoredAuthorAsync_LooksUpByTheStrippedNormalizedName()
        {
            var dbOptions = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(databaseName: $"author-monitor-cachekey-lookup-{Guid.NewGuid():N}")
                .Options;
            await using var dbContext = new ListenArrDbContext(dbOptions);
            dbContext.MonitoredAuthors.Add(new MonitoredAuthor
            {
                Id = 1,
                AuthorName = "Jane Doe",
                AuthorNameNormalized = "jane doe",
                Region = "us",
                Language = "english",
            });
            await dbContext.SaveChangesAsync();

            var service = new AuthorMonitoringService(
                new EfMonitoredAuthorRepository(dbContext),
                new AudiobookRepository(dbContext),
                Mock.Of<IAuthorCatalogService>(),
                Mock.Of<ILibraryAddService>(),
                Mock.Of<ILogger<AuthorMonitoringService>>());

            var found = await service.GetMonitoredAuthorAsync("Jane Doe - introduction", "us", "english");

            Assert.NotNull(found);
            Assert.Equal(1, found!.Id);
        }
    }
}
