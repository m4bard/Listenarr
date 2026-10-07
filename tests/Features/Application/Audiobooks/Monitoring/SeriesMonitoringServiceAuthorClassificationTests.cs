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
using Listenarr.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Application.Audiobooks.Monitoring
{
    /// <summary>
    /// Same gap as AuthorMonitoringServiceAuthorClassificationTests, tracker#341 Fix 4 / §5,
    /// on the series-monitoring side of the identical MapToMetadata / FindExistingLibraryMatch
    /// pair in SeriesMonitoringService.Mapping.cs.
    /// </summary>
    [Trait("Name", "SeriesMonitoringServiceAuthorClassificationTests")]
    [Trait("Category", "Application")]
    public class SeriesMonitoringServiceAuthorClassificationTests : Listenarr.Tests.Common.BaseTests
    {
        private static SeriesMonitoringService BuildService(
            ListenArrDbContext dbContext,
            Mock<ISeriesCatalogService> seriesCatalogService,
            Mock<ILibraryAddService> libraryAddService)
        {
            return new SeriesMonitoringService(
                new EfMonitoredSeriesRepository(dbContext),
                new AudiobookRepository(dbContext),
                seriesCatalogService.Object,
                libraryAddService.Object,
                Mock.Of<ILogger<SeriesMonitoringService>>());
        }

        [Fact]
        public async Task SyncSeriesAsync_ClassifiesTheCatalogBooksAuthorsBeforeAddingToTheLibrary()
        {
            var dbOptions = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(databaseName: $"series-monitor-classify-{Guid.NewGuid():N}")
                .Options;
            await using var dbContext = new ListenArrDbContext(dbOptions);
            dbContext.MonitoredSeries.Add(new MonitoredSeries
            {
                Id = 1,
                SeriesName = "A Synthetic Series",
                SeriesNameNormalized = "a synthetic series",
                Region = "us",
                Language = "english",
            });
            await dbContext.SaveChangesAsync();

            var seriesCatalogService = new Mock<ISeriesCatalogService>();
            seriesCatalogService
                .Setup(s => s.GetCatalogAsync(
                    "A Synthetic Series", "us", 500, null, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SeriesCatalogFetchResultBuilder()
                    .WithSeries("A Synthetic Series", "SERIESTEST1")
                    .WithBook(new AudibleSearchResultBuilder()
                        .WithAsin("TEST-ASIN-200")
                        .WithTitle("A Synthetic Title")
                        .WithAuthor("Jane Doe - introduction")
                        .WithAuthor("Someone Else")
                        .WithLanguage("english")
                        .WithSeries("A Synthetic Series", "1")
                        .Build())
                    .Build());

            LibraryAddOperationRequest? captured = null;
            var libraryAddService = new Mock<ILibraryAddService>();
            libraryAddService
                .Setup(s => s.AddToLibraryAsync(It.IsAny<LibraryAddOperationRequest>(), It.IsAny<CancellationToken>()))
                .Callback<LibraryAddOperationRequest, CancellationToken>((request, _) => captured = request)
                .ReturnsAsync(new LibraryAddOperationResult { Added = true, Audiobook = new Audiobook { Id = 2 } });

            var service = BuildService(dbContext, seriesCatalogService, libraryAddService);

            var result = await service.SyncSeriesAsync(1);

            Assert.True(result.Succeeded);
            Assert.NotNull(captured);
            Assert.Equal(
                new[] { "Someone Else", "Jane Doe" },
                captured!.Metadata.Authors);
        }

        [Fact]
        public async Task SyncSeriesAsync_MatchesAnExistingLibraryBookEvenWhenTheCatalogByelineCarriesARole()
        {
            var dbOptions = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(databaseName: $"series-monitor-match-{Guid.NewGuid():N}")
                .Options;
            await using var dbContext = new ListenArrDbContext(dbOptions);
            dbContext.MonitoredSeries.Add(new MonitoredSeries
            {
                Id = 1,
                SeriesName = "A Synthetic Series",
                SeriesNameNormalized = "a synthetic series",
                Region = "us",
                Language = "english",
            });
            dbContext.Audiobooks.Add(new Audiobook
            {
                Id = 10,
                Title = "A Synthetic Title",
                Authors = new List<string> { "Someone Else", "Jane Doe" },
                Language = "english",
                Monitored = true,
            });
            await dbContext.SaveChangesAsync();

            var seriesCatalogService = new Mock<ISeriesCatalogService>();
            seriesCatalogService
                .Setup(s => s.GetCatalogAsync(
                    "A Synthetic Series", "us", 500, null, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SeriesCatalogFetchResultBuilder()
                    .WithSeries("A Synthetic Series", "SERIESTEST2")
                    .WithBook(new AudibleSearchResultBuilder()
                        .WithAsin("TEST-ASIN-201")
                        .WithTitle("A Synthetic Title")
                        .WithAuthor("Jane Doe - introduction")
                        .WithAuthor("Someone Else")
                        .WithLanguage("english")
                        .WithSeries("A Synthetic Series", "1")
                        .Build())
                    .Build());

            var libraryAddService = new Mock<ILibraryAddService>();

            var service = BuildService(dbContext, seriesCatalogService, libraryAddService);

            var result = await service.SyncSeriesAsync(1);

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.ExistingCount);
            Assert.Equal(0, result.AddedCount);
            libraryAddService.Verify(
                s => s.AddToLibraryAsync(It.IsAny<LibraryAddOperationRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
