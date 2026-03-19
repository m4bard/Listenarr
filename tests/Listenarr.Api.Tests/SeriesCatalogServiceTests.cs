using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Listenarr.Api.Services;
using Listenarr.Domain.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class SeriesCatalogServiceTests
    {
        [Fact]
        public async Task GetCatalogAsync_UsesPersistedCatalogCache_BeforeAudible()
        {
            var audible = new Mock<AudibleService>(new HttpClient(), Mock.Of<ILogger<AudibleService>>()) { CallBase = false };
            var audiobookRepository = new Mock<IAudiobookRepository>();
            var logger = new Mock<ILogger<SeriesCatalogService>>();

            audiobookRepository
                .Setup(repository => repository.GetCachedSeriesByNameAsync("Mistborn", "us"))
                .ReturnsAsync(new SeriesCacheEntry
                {
                    SeriesName = "Mistborn",
                    SeriesNameNormalized = "mistborn",
                    SeriesAsin = "SERIES123",
                    Region = "us",
                    ImageUrl = "mistborn.jpg",
                    Description = "Persisted series description",
                    CatalogBooks = new List<CachedSeriesCatalogBook>
                    {
                        new()
                        {
                            Asin = "BOOK1",
                            Title = "The Final Empire",
                            Authors = new List<string> { "Brandon Sanderson" },
                            Language = "english"
                        },
                        new()
                        {
                            Asin = "BOOK2",
                            Title = "The Well of Ascension",
                            Authors = new List<string> { "Brandon Sanderson" },
                            Language = "german"
                        }
                    }
                });

            var service = new SeriesCatalogService(
                audible.Object,
                audiobookRepository.Object,
                logger.Object);

            var result = await service.GetCatalogAsync("Mistborn", "us", 10, "english");

            Assert.NotNull(result);
            Assert.Single(result!.Books);
            Assert.Equal("The Final Empire", result.Books[0].Title);
            Assert.Equal("Mistborn", result.Series.Name);

            audible.Verify(service => service.LookupSeriesAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            audible.Verify(service => service.GetTypedBooksBySeriesAsinAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetCatalogAsync_ForceRefresh_BypassesPersistedCatalogCache_AndPersistsFreshBooks()
        {
            var audible = new Mock<AudibleService>(new HttpClient(), Mock.Of<ILogger<AudibleService>>()) { CallBase = false };
            var audiobookRepository = new Mock<IAudiobookRepository>();
            var logger = new Mock<ILogger<SeriesCatalogService>>();

            audiobookRepository
                .Setup(repository => repository.GetCachedSeriesByNameAsync("Mistborn", "us"))
                .ReturnsAsync(new SeriesCacheEntry
                {
                    SeriesName = "Mistborn",
                    SeriesNameNormalized = "mistborn",
                    SeriesAsin = "SERIES123",
                    Region = "us",
                    ImageUrl = "old-mistborn.jpg",
                    CatalogBooks = new List<CachedSeriesCatalogBook>
                    {
                        new()
                        {
                            Title = "Old Cached Book",
                            Authors = new List<string> { "Brandon Sanderson" },
                            MetadataSource = "OpenLibrary"
                        }
                    }
                });

            audiobookRepository
                .Setup(repository => repository.UpsertCachedSeriesAsync(It.IsAny<SeriesCacheEntry>()))
                .ReturnsAsync((SeriesCacheEntry entry) => entry);

            audible
                .Setup(service => service.GetTypedBooksBySeriesAsinAsync("SERIES123", "us"))
                .ReturnsAsync(new List<AudibleSearchResult>
                {
                    new()
                    {
                        Asin = "BOOK1",
                        Title = "The Final Empire",
                        Authors = new List<AudibleAuthor> { new() { Name = "Brandon Sanderson" } },
                        ImageUrl = "final-empire.jpg",
                        Language = "english",
                        Link = "https://audible.example/final-empire",
                        Series = new List<AudibleSeries> { new() { Name = "Mistborn", Position = "1" } }
                    }
                });

            var service = new SeriesCatalogService(
                audible.Object,
                audiobookRepository.Object,
                logger.Object);

            var result = await service.GetCatalogAsync("Mistborn", "us", 10, forceRefresh: true);

            Assert.NotNull(result);
            Assert.Single(result!.Books);
            Assert.Equal("The Final Empire", result.Books[0].Title);

            audible.Verify(
                svc => svc.GetTypedBooksBySeriesAsinAsync("SERIES123", "us"),
                Times.Once);
            audiobookRepository.Verify(
                repository => repository.UpsertCachedSeriesAsync(It.Is<SeriesCacheEntry>(entry =>
                    entry.SeriesAsin == "SERIES123" &&
                    entry.CatalogBooks != null &&
                    entry.CatalogBooks.Count == 1 &&
                    entry.CatalogBooks[0].Title == "The Final Empire")),
                Times.Once);
        }
    }
}
