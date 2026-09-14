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
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Application.Audiobooks.Catalog
{
    /// <summary>
    /// Both writers of AuthorCacheEntries resolve a row first and then describe the author they
    /// are about to persist. Neither may describe that author onto a row that belongs to somebody
    /// else, because the upsert copies whatever it is handed onto a tracked row. Every ASIN here
    /// is a fixture literal.
    /// </summary>
    [Trait("Area", "Library")]
    [Trait("Name", "AuthorCachePersistenceTests")]
    [Trait("Category", "Metadata")]
    public class AuthorCachePersistenceTests : BaseTests
    {
        private static readonly HttpClient SharedHttpClient = new();

        private static async Task<(SqliteConnection Connection, ListenArrDbContext Context)> OpenAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new ListenArrDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return (connection, context);
        }

        [Fact]
        public async Task PersistCatalogAsync_ResolvedRowNamedForSomebodyElse_IsNotWrittenOnto()
        {
            var audible = new Mock<AudibleService>(SharedHttpClient, Mock.Of<ILogger<AudibleService>>()) { CallBase = false };
            var audnexus = new Mock<IAudnexusService>();
            var repository = new Mock<IAudiobookRepository>();
            var searchService = new Mock<ISearchService>();

            var resolved = new AuthorCacheEntry
            {
                Id = 7,
                AuthorName = "Author Two",
                AuthorNameNormalized = "author two",
                AuthorAsin = null,
                ImageUrl = "author-two.jpg",
                Region = "us",
                SimilarAuthors = [new CachedRelatedAuthor { Asin = "FIXTUREAUT5", Name = "Somebody Else" }]
            };

            repository
                .Setup(repo => repo.GetCachedAuthorByNameAsync("Author One", "us"))
                .ReturnsAsync(resolved);

            audible
                .Setup(service => service.LookupAuthorAsync("Author One", "us"))
                .ReturnsAsync(new AuthorLookupItem { Asin = "FIXTUREAUT1", Name = "Author One" });
            audible
                .Setup(service => service.GetAllBooksByAuthorAsync("Author One", "FIXTUREAUT1", It.IsAny<int>(), "us", null))
                .ReturnsAsync(new AudibleSearchResponse
                {
                    Results = [new AudibleSearchResult { Asin = "FIXTUREPRD1", Title = "A Book" }],
                    TotalResults = 1
                });
            searchService
                .Setup(service => service.IntelligentSearchAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<double>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            AuthorCacheEntry? written = null;
            repository
                .Setup(repo => repo.UpsertCachedAuthorAsync(It.IsAny<AuthorCacheEntry>()))
                .Callback<AuthorCacheEntry>(entry => written = entry)
                .ReturnsAsync((AuthorCacheEntry entry) => entry);

            var service = new AuthorCatalogService(
                audible.Object,
                audnexus.Object,
                repository.Object,
                searchService.Object,
                Mock.Of<ILogger<AuthorCatalogService>>());

            await service.GetCatalogAsync("Author One", "us", 10);

            Assert.NotNull(written);
            Assert.NotSame(resolved, written);
            Assert.Equal("Author One", written!.AuthorName);
            // Author Two's photo and similar-authors list must not ride along onto Author One.
            Assert.Empty(written.SimilarAuthors ?? []);
            Assert.NotEqual("author-two.jpg", written.ImageUrl);
            Assert.Equal("Author Two", resolved.AuthorName);
        }

        [Fact]
        public async Task PersistCatalogAsync_ResolvedRowForThisAuthor_IsStillUpdatedInPlace()
        {
            var audible = new Mock<AudibleService>(SharedHttpClient, Mock.Of<ILogger<AudibleService>>()) { CallBase = false };
            var audnexus = new Mock<IAudnexusService>();
            var repository = new Mock<IAudiobookRepository>();
            var searchService = new Mock<ISearchService>();

            var resolved = new AuthorCacheEntry
            {
                Id = 7,
                AuthorName = "Author One",
                AuthorNameNormalized = "author one",
                AuthorAsin = null,
                Description = "Existing biography.",
                Region = "us"
            };

            repository
                .Setup(repo => repo.GetCachedAuthorByNameAsync("Author One", "us"))
                .ReturnsAsync(resolved);

            audible
                .Setup(service => service.LookupAuthorAsync("Author One", "us"))
                .ReturnsAsync(new AuthorLookupItem { Asin = "FIXTUREAUT1", Name = "Author One" });
            audible
                .Setup(service => service.GetAllBooksByAuthorAsync("Author One", "FIXTUREAUT1", It.IsAny<int>(), "us", null))
                .ReturnsAsync(new AudibleSearchResponse
                {
                    Results = [new AudibleSearchResult { Asin = "FIXTUREPRD1", Title = "A Book" }],
                    TotalResults = 1
                });
            searchService
                .Setup(service => service.IntelligentSearchAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<double>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            AuthorCacheEntry? written = null;
            repository
                .Setup(repo => repo.UpsertCachedAuthorAsync(It.IsAny<AuthorCacheEntry>()))
                .Callback<AuthorCacheEntry>(entry => written = entry)
                .ReturnsAsync((AuthorCacheEntry entry) => entry);

            var service = new AuthorCatalogService(
                audible.Object,
                audnexus.Object,
                repository.Object,
                searchService.Object,
                Mock.Of<ILogger<AuthorCatalogService>>());

            await service.GetCatalogAsync("Author One", "us", 10);

            // CONTROL. The ordinary path still reuses the row it resolved, keeping what the row
            // already knew about this author.
            Assert.Same(resolved, written);
            Assert.Equal("Existing biography.", written!.Description);
        }

        [Fact]
        public async Task PersistAuthorLookupAsync_ResolvedRowNamedForSomebodyElse_LeavesThatRowAlone()
        {
            var (connection, context) = await OpenAsync();
            await using var _ = connection;
            await using var __ = context;
            context.AuthorCacheEntries.Add(new AuthorCacheEntry
            {
                AuthorName = "Author One",
                AuthorNameNormalized = "author one",
                AuthorAsin = "FIXTURESHR1",
                ImageUrl = "author-one.jpg",
                Region = "us"
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // The row the ASIN-first branch of ResolvePersistedAuthorCacheAsync hands back when the
            // request carried an ASIN that is already somebody else's.
            var resolved = await context.AuthorCacheEntries.AsNoTracking().SingleAsync();
            var repository = new AudiobookRepository(context);
            var logger = Mock.Of<ILogger<MetadataController>>();
            var images = new MetadataImageCacheWorkflow(repository, Mock.Of<IImageCacheService>(), logger);
            var workflow = new MetadataLookupCacheWorkflow(repository, Mock.Of<IImageCacheService>(), images, logger);

            await workflow.PersistAuthorLookupAsync(resolved, "Author Two", "us", new MetadataController.AuthorLookupResponse
            {
                Asin = "FIXTURESHR1",
                Name = "Author Two"
            });

            context.ChangeTracker.Clear();
            var rows = await context.AuthorCacheEntries
                .AsNoTracking()
                .OrderBy(entry => entry.Id)
                .ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Equal("Author One", rows[0].AuthorName);
            Assert.Equal("author-one.jpg", rows[0].ImageUrl);
            Assert.Equal("Author Two", rows[1].AuthorName);
        }

        [Fact]
        public async Task PersistAuthorLookupAsync_ResolvedRowForThisAuthor_IsStillUpdatedInPlace()
        {
            var (connection, context) = await OpenAsync();
            await using var _ = connection;
            await using var __ = context;
            context.AuthorCacheEntries.Add(new AuthorCacheEntry
            {
                AuthorName = "Author One",
                AuthorNameNormalized = "author one",
                AuthorAsin = "FIXTUREAUT1",
                Region = "us"
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var resolved = await context.AuthorCacheEntries.AsNoTracking().SingleAsync();
            var repository = new AudiobookRepository(context);
            var logger = Mock.Of<ILogger<MetadataController>>();
            var images = new MetadataImageCacheWorkflow(repository, Mock.Of<IImageCacheService>(), logger);
            var workflow = new MetadataLookupCacheWorkflow(repository, Mock.Of<IImageCacheService>(), images, logger);

            await workflow.PersistAuthorLookupAsync(resolved, "Author One", "us", new MetadataController.AuthorLookupResponse
            {
                Asin = "FIXTUREAUT1",
                Name = "Author One",
                Description = "Fixture biography."
            });

            // CONTROL.
            context.ChangeTracker.Clear();
            var row = Assert.Single(await context.AuthorCacheEntries.AsNoTracking().ToListAsync());
            Assert.Equal("Fixture biography.", row.Description);
        }
    }
}
