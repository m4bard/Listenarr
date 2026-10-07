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
    /// tracker#341 Fix 4 / §5: "the classifier is not applied ... at monitored-author
    /// creation." MapToMetadata built the AudibleBookMetadata handed to the library-add
    /// pipeline from book.Authors.Select(a => a.Name) raw, so a suffixed contributor became a
    /// first-class stored author on anything a monitored author's catalog sync added. Also
    /// covers FindExistingLibraryMatch's title-author key, which used the same raw names and so
    /// could fail to match a catalog book against a library row whose Authors were already
    /// stripped by AudibleBookMetadata.ToAudiobook().
    /// </summary>
    [Trait("Name", "AuthorMonitoringServiceAuthorClassificationTests")]
    [Trait("Category", "Application")]
    public class AuthorMonitoringServiceAuthorClassificationTests : Listenarr.Tests.Common.BaseTests
    {
        private static AuthorMonitoringService BuildService(
            ListenArrDbContext dbContext,
            Mock<IAuthorCatalogService> authorCatalogService,
            Mock<ILibraryAddService> libraryAddService)
        {
            return new AuthorMonitoringService(
                new EfMonitoredAuthorRepository(dbContext),
                new AudiobookRepository(dbContext),
                authorCatalogService.Object,
                libraryAddService.Object,
                Mock.Of<ILogger<AuthorMonitoringService>>());
        }

        [Fact]
        public async Task SyncAuthorAsync_ClassifiesTheCatalogBooksAuthorsBeforeAddingToTheLibrary()
        {
            var dbOptions = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(databaseName: $"author-monitor-classify-{Guid.NewGuid():N}")
                .Options;
            await using var dbContext = new ListenArrDbContext(dbOptions);
            dbContext.MonitoredAuthors.Add(new MonitoredAuthor
            {
                Id = 1,
                AuthorName = "Someone Else",
                AuthorNameNormalized = "someone else",
                Region = "us",
                Language = "english"
            });
            await dbContext.SaveChangesAsync();

            var authorCatalogService = new Mock<IAuthorCatalogService>();
            authorCatalogService
                .Setup(s => s.GetCatalogAsync(
                    "Someone Else", "us", 500, null, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AuthorCatalogFetchResult
                {
                    Author = new AuthorLookupItem { Asin = "AUTHORTEST1", Name = "Someone Else" },
                    Books = new List<AudibleSearchResult>
                    {
                        new()
                        {
                            Asin = "TEST-ASIN-100",
                            Title = "A Synthetic Title",
                            Authors = new List<AudibleAuthor>
                            {
                                new() { Name = "Jane Doe - introduction" },
                                new() { Name = "Someone Else" },
                            },
                            Language = "english",
                        },
                    },
                });

            LibraryAddOperationRequest? captured = null;
            var libraryAddService = new Mock<ILibraryAddService>();
            libraryAddService
                .Setup(s => s.AddToLibraryAsync(It.IsAny<LibraryAddOperationRequest>(), It.IsAny<CancellationToken>()))
                .Callback<LibraryAddOperationRequest, CancellationToken>((request, _) => captured = request)
                .ReturnsAsync(new LibraryAddOperationResult { Added = true, Audiobook = new Audiobook { Id = 2 } });

            var service = BuildService(dbContext, authorCatalogService, libraryAddService);

            var result = await service.SyncAuthorAsync(1);

            Assert.True(result.Succeeded);
            Assert.NotNull(captured);
            Assert.Equal(
                new[] { "Someone Else", "Jane Doe" },
                captured!.Metadata.Authors);
        }

        [Fact]
        public async Task SyncAuthorAsync_MatchesAnExistingLibraryBookEvenWhenTheCatalogByelineCarriesARole()
        {
            var dbOptions = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(databaseName: $"author-monitor-match-{Guid.NewGuid():N}")
                .Options;
            await using var dbContext = new ListenArrDbContext(dbOptions);
            dbContext.MonitoredAuthors.Add(new MonitoredAuthor
            {
                Id = 1,
                AuthorName = "Someone Else",
                AuthorNameNormalized = "someone else",
                Region = "us",
                Language = "english"
            });
            // The library row is stored the way ToAudiobook() already stores it: stripped,
            // author-first. The title-author key has to be built from the same shape on the
            // catalog side or the match fails and the book is re-added.
            dbContext.Audiobooks.Add(new Audiobook
            {
                Id = 10,
                Title = "A Synthetic Title",
                Authors = new List<string> { "Someone Else", "Jane Doe" },
                Language = "english",
                Monitored = true,
            });
            await dbContext.SaveChangesAsync();

            var authorCatalogService = new Mock<IAuthorCatalogService>();
            authorCatalogService
                .Setup(s => s.GetCatalogAsync(
                    "Someone Else", "us", 500, null, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AuthorCatalogFetchResult
                {
                    Author = new AuthorLookupItem { Asin = "AUTHORTEST2", Name = "Someone Else" },
                    Books = new List<AudibleSearchResult>
                    {
                        new()
                        {
                            Asin = "TEST-ASIN-101",
                            Title = "A Synthetic Title",
                            Authors = new List<AudibleAuthor>
                            {
                                new() { Name = "Jane Doe - introduction" },
                                new() { Name = "Someone Else" },
                            },
                            Language = "english",
                        },
                    },
                });

            var libraryAddService = new Mock<ILibraryAddService>();

            var service = BuildService(dbContext, authorCatalogService, libraryAddService);

            var result = await service.SyncAuthorAsync(1);

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.ExistingCount);
            Assert.Equal(0, result.AddedCount);
            libraryAddService.Verify(
                s => s.AddToLibraryAsync(It.IsAny<LibraryAddOperationRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
