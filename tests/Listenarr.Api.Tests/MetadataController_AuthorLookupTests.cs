using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Listenarr.Api.Controllers;
using Listenarr.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class MetadataController_AuthorLookupTests
    {
        [Fact]
        public async Task LookupAuthor_PrefersCachedImage_AndReturnsBiographyAndSimilarAuthors()
        {
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var metadataService = new Mock<IAudiobookMetadataService>();
            var audimeta = new Mock<AudimetaService>(new HttpClient(), Mock.Of<ILogger<AudimetaService>>()) { CallBase = false };
            var audnexus = new Mock<IAudnexusService>();
            var imageCache = new Mock<IImageCacheService>();
            var audiobookRepository = new Mock<IAudiobookRepository>();
            var asinLookup = new Mock<IAsinLookupService>();
            var authorCatalogService = new Mock<IAuthorCatalogService>();
            var seriesCatalogService = new Mock<ISeriesCatalogService>();
            var logger = new Mock<ILogger<MetadataController>>();

            audiobookRepository
                .Setup(repository => repository.GetAuthorAsinByNameAsync("Andy Weir"))
                .ReturnsAsync("AUTHOR123");

            imageCache
                .Setup(service => service.GetCachedImagePathAsync("AUTHOR123"))
                .ReturnsAsync("config/cache/images/authors/AUTHOR123.jpg");

            audimeta
                .Setup(service => service.GetAuthorByAsinAsync("AUTHOR123", "us"))
                .ReturnsAsync(new AuthorLookupItem
                {
                    Asin = "AUTHOR123",
                    Name = "Andy Weir",
                    Image = "https://example.com/andy-weir.jpg",
                    Description = "Audimeta biography"
                });

            audnexus
                .Setup(service => service.GetAuthorAsync("AUTHOR123", "us", false))
                .ReturnsAsync(new AudnexusAuthorResponse
                {
                    Asin = "AUTHOR123",
                    Name = "Andy Weir",
                    Similar = new List<AudnexusSimilarAuthor>
                    {
                        new() { Asin = "SIM123", Name = "Blake Crouch" },
                        new() { Asin = "SIM999", Name = "Ernest Cline" }
                    }
                });

            var controller = new MetadataController(
                metadataService.Object,
                audimeta.Object,
                audnexus.Object,
                imageCache.Object,
                memoryCache,
                audiobookRepository.Object,
                asinLookup.Object,
                authorCatalogService.Object,
                seriesCatalogService.Object,
                logger.Object);

            var result = await controller.LookupAuthor("Andy Weir", "us");

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var payload = Assert.IsType<MetadataController.AuthorLookupResponse>(ok.Value);

            Assert.Equal("AUTHOR123", payload.Asin);
            Assert.Equal("Andy Weir", payload.Name);
            Assert.Equal("/config/cache/images/authors/AUTHOR123.jpg", payload.CachedPath);
            Assert.Equal("Audimeta biography", payload.Description);
            Assert.Collection(
                payload.SimilarAuthors,
                author => Assert.Equal("Blake Crouch", author.Name),
                author => Assert.Equal("Ernest Cline", author.Name));

            imageCache.Verify(
                service => service.MoveToAuthorLibraryStorageAsync(It.IsAny<string>(), It.IsAny<string?>()),
                Times.Never);
        }

        [Fact]
        public async Task LookupAuthor_UsesPersistedAuthorCache_BeforeAudimeta()
        {
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var metadataService = new Mock<IAudiobookMetadataService>();
            var audimeta = new Mock<AudimetaService>(new HttpClient(), Mock.Of<ILogger<AudimetaService>>()) { CallBase = false };
            var audnexus = new Mock<IAudnexusService>();
            var imageCache = new Mock<IImageCacheService>();
            var audiobookRepository = new Mock<IAudiobookRepository>();
            var asinLookup = new Mock<IAsinLookupService>();
            var authorCatalogService = new Mock<IAuthorCatalogService>();
            var seriesCatalogService = new Mock<ISeriesCatalogService>();
            var logger = new Mock<ILogger<MetadataController>>();

            audiobookRepository
                .Setup(repository => repository.GetCachedAuthorByNameAsync("Andy Weir", "us"))
                .ReturnsAsync(new AuthorCacheEntry
                {
                    AuthorName = "Andy Weir",
                    AuthorNameNormalized = "andy weir",
                    AuthorAsin = "AUTHOR123",
                    Region = "us",
                    ImageUrl = "https://example.com/andy-weir.jpg",
                    Description = "Persisted biography",
                    SimilarAuthors = new List<CachedRelatedAuthor>
                    {
                        new() { Asin = "SIM123", Name = "Blake Crouch" }
                    }
                });

            imageCache
                .Setup(service => service.GetCachedImagePathAsync("AUTHOR123"))
                .ReturnsAsync("config/cache/images/authors/AUTHOR123.jpg");

            var controller = new MetadataController(
                metadataService.Object,
                audimeta.Object,
                audnexus.Object,
                imageCache.Object,
                memoryCache,
                audiobookRepository.Object,
                asinLookup.Object,
                authorCatalogService.Object,
                seriesCatalogService.Object,
                logger.Object);

            var result = await controller.LookupAuthor("Andy Weir", "us");

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var payload = Assert.IsType<MetadataController.AuthorLookupResponse>(ok.Value);

            Assert.Equal("AUTHOR123", payload.Asin);
            Assert.Equal("Andy Weir", payload.Name);
            Assert.Equal("Persisted biography", payload.Description);
            Assert.Equal("/config/cache/images/authors/AUTHOR123.jpg", payload.CachedPath);
            Assert.Single(payload.SimilarAuthors);

            audimeta.Verify(service => service.LookupAuthorAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            audimeta.Verify(service => service.GetAuthorByAsinAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task LookupAuthor_RefreshesMissingCachedImage_FromAudimetaAndStoresIt()
        {
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var metadataService = new Mock<IAudiobookMetadataService>();
            var audimeta = new Mock<AudimetaService>(new HttpClient(), Mock.Of<ILogger<AudimetaService>>()) { CallBase = false };
            var audnexus = new Mock<IAudnexusService>();
            var imageCache = new Mock<IImageCacheService>();
            var audiobookRepository = new Mock<IAudiobookRepository>();
            var asinLookup = new Mock<IAsinLookupService>();
            var authorCatalogService = new Mock<IAuthorCatalogService>();
            var seriesCatalogService = new Mock<ISeriesCatalogService>();
            var logger = new Mock<ILogger<MetadataController>>();

            audiobookRepository
                .Setup(repository => repository.GetCachedAuthorByNameAsync("Andy Weir", "us"))
                .ReturnsAsync(new AuthorCacheEntry
                {
                    AuthorName = "Andy Weir",
                    AuthorNameNormalized = "andy weir",
                    AuthorAsin = "AUTHOR123",
                    Region = "us",
                    ImageUrl = "https://db.example.com/andy-weir.jpg",
                    Description = "Persisted biography",
                    SimilarAuthors = new List<CachedRelatedAuthor>
                    {
                        new() { Asin = "SIM123", Name = "Blake Crouch" }
                    }
                });

            audiobookRepository
                .Setup(repository => repository.UpsertCachedAuthorAsync(It.IsAny<AuthorCacheEntry>()))
                .ReturnsAsync((AuthorCacheEntry entry) => entry);

            imageCache
                .Setup(service => service.GetCachedImagePathAsync("AUTHOR123"))
                .ReturnsAsync((string?)null);

            imageCache
                .Setup(service => service.MoveToAuthorLibraryStorageAsync("AUTHOR123", "https://audimeta.example.com/andy-weir.jpg"))
                .ReturnsAsync("config/cache/images/authors/AUTHOR123.jpg");

            audimeta
                .Setup(service => service.GetAuthorByAsinAsync("AUTHOR123", "us"))
                .ReturnsAsync(new AuthorLookupItem
                {
                    Asin = "AUTHOR123",
                    Name = "Andy Weir",
                    Image = "https://audimeta.example.com/andy-weir.jpg",
                    Description = "Audimeta biography"
                });

            var controller = new MetadataController(
                metadataService.Object,
                audimeta.Object,
                audnexus.Object,
                imageCache.Object,
                memoryCache,
                audiobookRepository.Object,
                asinLookup.Object,
                authorCatalogService.Object,
                seriesCatalogService.Object,
                logger.Object);

            var result = await controller.LookupAuthor("Andy Weir", "us");

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var payload = Assert.IsType<MetadataController.AuthorLookupResponse>(ok.Value);

            Assert.Equal("/config/cache/images/authors/AUTHOR123.jpg", payload.CachedPath);
            Assert.Equal("https://audimeta.example.com/andy-weir.jpg", payload.Image);
            Assert.Equal("Audimeta biography", payload.Description);

            imageCache.Verify(
                service => service.MoveToAuthorLibraryStorageAsync("AUTHOR123", "https://audimeta.example.com/andy-weir.jpg"),
                Times.Once);
            audnexus.Verify(service => service.GetAuthorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
            audiobookRepository.Verify(
                repository => repository.UpsertCachedAuthorAsync(It.Is<AuthorCacheEntry>(entry =>
                    entry.AuthorAsin == "AUTHOR123" &&
                    entry.ImageUrl == "https://audimeta.example.com/andy-weir.jpg")),
                Times.Once);
        }

        [Fact]
        public async Task LookupAuthor_RefreshesPersistedBiographyOnlyEntry_ToLoadSimilarAuthors()
        {
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var metadataService = new Mock<IAudiobookMetadataService>();
            var audimeta = new Mock<AudimetaService>(new HttpClient(), Mock.Of<ILogger<AudimetaService>>()) { CallBase = false };
            var audnexus = new Mock<IAudnexusService>();
            var imageCache = new Mock<IImageCacheService>();
            var audiobookRepository = new Mock<IAudiobookRepository>();
            var asinLookup = new Mock<IAsinLookupService>();
            var authorCatalogService = new Mock<IAuthorCatalogService>();
            var seriesCatalogService = new Mock<ISeriesCatalogService>();
            var logger = new Mock<ILogger<MetadataController>>();

            audiobookRepository
                .Setup(repository => repository.GetCachedAuthorByNameAsync("Andy Weir", "us"))
                .ReturnsAsync(new AuthorCacheEntry
                {
                    AuthorName = "Andy Weir",
                    AuthorNameNormalized = "andy weir",
                    AuthorAsin = "AUTHOR123",
                    Region = "us",
                    ImageUrl = "https://example.com/andy-weir.jpg",
                    Description = "Persisted Audimeta biography",
                    SimilarAuthors = new List<CachedRelatedAuthor>()
                });

            audiobookRepository
                .Setup(repository => repository.UpsertCachedAuthorAsync(It.IsAny<AuthorCacheEntry>()))
                .ReturnsAsync((AuthorCacheEntry entry) => entry);

            imageCache
                .Setup(service => service.GetCachedImagePathAsync("AUTHOR123"))
                .ReturnsAsync("config/cache/images/authors/AUTHOR123.jpg");

            audimeta
                .Setup(service => service.GetAuthorByAsinAsync("AUTHOR123", "us"))
                .ReturnsAsync(new AuthorLookupItem
                {
                    Asin = "AUTHOR123",
                    Name = "Andy Weir",
                    Image = "https://example.com/andy-weir.jpg",
                    Description = "Persisted Audimeta biography"
                });

            audnexus
                .Setup(service => service.GetAuthorAsync("AUTHOR123", "us", false))
                .ReturnsAsync(new AudnexusAuthorResponse
                {
                    Asin = "AUTHOR123",
                    Name = "Andy Weir",
                    Description = "Audnexus biography that should be ignored",
                    Similar = new List<AudnexusSimilarAuthor>
                    {
                        new() { Asin = "SIM123", Name = "Blake Crouch" },
                    }
                });

            var controller = new MetadataController(
                metadataService.Object,
                audimeta.Object,
                audnexus.Object,
                imageCache.Object,
                memoryCache,
                audiobookRepository.Object,
                asinLookup.Object,
                authorCatalogService.Object,
                seriesCatalogService.Object,
                logger.Object);

            var result = await controller.LookupAuthor("Andy Weir", "us");

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var payload = Assert.IsType<MetadataController.AuthorLookupResponse>(ok.Value);

            Assert.Equal("Persisted Audimeta biography", payload.Description);
            Assert.Single(payload.SimilarAuthors);
            Assert.Equal("Blake Crouch", payload.SimilarAuthors[0].Name);

            audnexus.Verify(service => service.GetAuthorAsync("AUTHOR123", "us", false), Times.Once);
        }

        [Fact]
        public async Task LookupAuthor_RefreshesPartialPersistedCache_WithBiographyAndSimilarAuthors()
        {
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var metadataService = new Mock<IAudiobookMetadataService>();
            var audimeta = new Mock<AudimetaService>(new HttpClient(), Mock.Of<ILogger<AudimetaService>>()) { CallBase = false };
            var audnexus = new Mock<IAudnexusService>();
            var imageCache = new Mock<IImageCacheService>();
            var audiobookRepository = new Mock<IAudiobookRepository>();
            var asinLookup = new Mock<IAsinLookupService>();
            var authorCatalogService = new Mock<IAuthorCatalogService>();
            var seriesCatalogService = new Mock<ISeriesCatalogService>();
            var logger = new Mock<ILogger<MetadataController>>();

            audiobookRepository
                .Setup(repository => repository.GetCachedAuthorByNameAsync("Andy Weir", "us"))
                .ReturnsAsync(new AuthorCacheEntry
                {
                    AuthorName = "Andy Weir",
                    AuthorNameNormalized = "andy weir",
                    AuthorAsin = "AUTHOR123",
                    Region = "us",
                    ImageUrl = "https://example.com/andy-weir.jpg",
                    Description = null,
                    SimilarAuthors = new List<CachedRelatedAuthor>()
                });

            audiobookRepository
                .Setup(repository => repository.UpsertCachedAuthorAsync(It.IsAny<AuthorCacheEntry>()))
                .ReturnsAsync((AuthorCacheEntry entry) => entry);

            imageCache
                .Setup(service => service.GetCachedImagePathAsync("AUTHOR123"))
                .ReturnsAsync("config/cache/images/authors/AUTHOR123.jpg");

            audimeta
                .Setup(service => service.GetAuthorByAsinAsync("AUTHOR123", "us"))
                .ReturnsAsync(new AuthorLookupItem
                {
                    Asin = "AUTHOR123",
                    Name = "Andy Weir",
                    Image = "https://example.com/andy-weir.jpg"
                });

            audnexus
                .Setup(service => service.GetAuthorAsync("AUTHOR123", "us", false))
                .ReturnsAsync(new AudnexusAuthorResponse
                {
                    Asin = "AUTHOR123",
                    Name = "Andy Weir",
                    Description = "Audnexus biography",
                    Similar = new List<AudnexusSimilarAuthor>
                    {
                        new() { Asin = "SIM123", Name = "Blake Crouch" },
                    }
                });

            var controller = new MetadataController(
                metadataService.Object,
                audimeta.Object,
                audnexus.Object,
                imageCache.Object,
                memoryCache,
                audiobookRepository.Object,
                asinLookup.Object,
                authorCatalogService.Object,
                seriesCatalogService.Object,
                logger.Object);

            var result = await controller.LookupAuthor("Andy Weir", "us");

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var payload = Assert.IsType<MetadataController.AuthorLookupResponse>(ok.Value);

            Assert.Equal("Audnexus biography", payload.Description);
            Assert.Single(payload.SimilarAuthors);
            Assert.Equal("Blake Crouch", payload.SimilarAuthors[0].Name);

            audnexus.Verify(service => service.GetAuthorAsync("AUTHOR123", "us", false), Times.Once);
            audiobookRepository.Verify(
                repository => repository.UpsertCachedAuthorAsync(It.Is<AuthorCacheEntry>(entry =>
                    entry.AuthorAsin == "AUTHOR123" &&
                    entry.Description == "Audnexus biography" &&
                    entry.SimilarAuthors != null &&
                    entry.SimilarAuthors.Count == 1)),
                Times.Once);
        }

        [Fact]
        public async Task LookupAuthor_FallsBackToAudnexus_WhenAudimetaCannotHydrateMissingFields()
        {
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var metadataService = new Mock<IAudiobookMetadataService>();
            var audimeta = new Mock<AudimetaService>(new HttpClient(), Mock.Of<ILogger<AudimetaService>>()) { CallBase = false };
            var audnexus = new Mock<IAudnexusService>();
            var imageCache = new Mock<IImageCacheService>();
            var audiobookRepository = new Mock<IAudiobookRepository>();
            var asinLookup = new Mock<IAsinLookupService>();
            var authorCatalogService = new Mock<IAuthorCatalogService>();
            var seriesCatalogService = new Mock<ISeriesCatalogService>();
            var logger = new Mock<ILogger<MetadataController>>();

            audiobookRepository
                .Setup(repository => repository.GetCachedAuthorByNameAsync("Andy Weir", "us"))
                .ReturnsAsync(new AuthorCacheEntry
                {
                    AuthorName = "Andy Weir",
                    AuthorNameNormalized = "andy weir",
                    AuthorAsin = "AUTHOR123",
                    Region = "us",
                    ImageUrl = null,
                    Description = null,
                    SimilarAuthors = new List<CachedRelatedAuthor>()
                });

            audiobookRepository
                .Setup(repository => repository.UpsertCachedAuthorAsync(It.IsAny<AuthorCacheEntry>()))
                .ReturnsAsync((AuthorCacheEntry entry) => entry);

            imageCache
                .Setup(service => service.GetCachedImagePathAsync("AUTHOR123"))
                .ReturnsAsync((string?)null);

            imageCache
                .Setup(service => service.MoveToAuthorLibraryStorageAsync("AUTHOR123", "https://audnexus.example.com/andy-weir.jpg"))
                .ReturnsAsync("config/cache/images/authors/AUTHOR123.jpg");

            audimeta
                .Setup(service => service.GetAuthorByAsinAsync("AUTHOR123", "us"))
                .ReturnsAsync((AuthorLookupItem?)null);

            audimeta
                .Setup(service => service.LookupAuthorAsync("Andy Weir", "us"))
                .ReturnsAsync((AuthorLookupItem?)null);

            audnexus
                .Setup(service => service.GetAuthorAsync("AUTHOR123", "us", false))
                .ReturnsAsync(new AudnexusAuthorResponse
                {
                    Asin = "AUTHOR123",
                    Name = "Andy Weir",
                    Description = "Audnexus biography",
                    Image = "https://audnexus.example.com/andy-weir.jpg",
                    Similar = new List<AudnexusSimilarAuthor>
                    {
                        new() { Asin = "SIM123", Name = "Blake Crouch" }
                    }
                });

            var controller = new MetadataController(
                metadataService.Object,
                audimeta.Object,
                audnexus.Object,
                imageCache.Object,
                memoryCache,
                audiobookRepository.Object,
                asinLookup.Object,
                authorCatalogService.Object,
                seriesCatalogService.Object,
                logger.Object);

            var result = await controller.LookupAuthor("Andy Weir", "us");

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var payload = Assert.IsType<MetadataController.AuthorLookupResponse>(ok.Value);

            Assert.Equal("/config/cache/images/authors/AUTHOR123.jpg", payload.CachedPath);
            Assert.Equal("https://audnexus.example.com/andy-weir.jpg", payload.Image);
            Assert.Equal("Audnexus biography", payload.Description);
            Assert.Single(payload.SimilarAuthors);
            Assert.Equal("Blake Crouch", payload.SimilarAuthors[0].Name);

            imageCache.Verify(
                service => service.MoveToAuthorLibraryStorageAsync("AUTHOR123", "https://audnexus.example.com/andy-weir.jpg"),
                Times.Once);
            audiobookRepository.Verify(
                repository => repository.UpsertCachedAuthorAsync(It.Is<AuthorCacheEntry>(entry =>
                    entry.AuthorAsin == "AUTHOR123" &&
                    entry.ImageUrl == "https://audnexus.example.com/andy-weir.jpg" &&
                    entry.Description == "Audnexus biography" &&
                    entry.SimilarAuthors != null &&
                    entry.SimilarAuthors.Count == 1)),
                Times.Once);
        }

        [Fact]
        public async Task LookupAuthor_RefreshRequest_BypassesCachedLookupAndRecachesFreshMetadata()
        {
            using var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var metadataService = new Mock<IAudiobookMetadataService>();
            var audimeta = new Mock<AudimetaService>(new HttpClient(), Mock.Of<ILogger<AudimetaService>>()) { CallBase = false };
            var audnexus = new Mock<IAudnexusService>();
            var imageCache = new Mock<IImageCacheService>();
            var audiobookRepository = new Mock<IAudiobookRepository>();
            var asinLookup = new Mock<IAsinLookupService>();
            var authorCatalogService = new Mock<IAuthorCatalogService>();
            var seriesCatalogService = new Mock<ISeriesCatalogService>();
            var logger = new Mock<ILogger<MetadataController>>();

            audiobookRepository
                .Setup(repository => repository.GetCachedAuthorByNameAsync("Andy Weir", "us"))
                .ReturnsAsync(new AuthorCacheEntry
                {
                    AuthorName = "Andy Weir",
                    AuthorNameNormalized = "andy weir",
                    AuthorAsin = "AUTHOR123",
                    Region = "us",
                    ImageUrl = "https://old.example.com/andy-weir.jpg",
                    Description = "Old biography",
                    SimilarAuthors = new List<CachedRelatedAuthor>
                    {
                        new() { Asin = "SIMOLD", Name = "Old Similar Author" }
                    }
                });

            audiobookRepository
                .Setup(repository => repository.UpsertCachedAuthorAsync(It.IsAny<AuthorCacheEntry>()))
                .ReturnsAsync((AuthorCacheEntry entry) => entry);

            imageCache
                .Setup(service => service.GetCachedImagePathAsync("AUTHOR123"))
                .ReturnsAsync("config/cache/images/authors/AUTHOR123.jpg");

            imageCache
                .Setup(service => service.MoveToAuthorLibraryStorageAsync("AUTHOR123", "https://fresh.example.com/andy-weir.jpg", true))
                .ReturnsAsync("config/cache/images/authors/AUTHOR123.jpg");

            audimeta
                .Setup(service => service.GetAuthorByAsinAsync("AUTHOR123", "us"))
                .ReturnsAsync(new AuthorLookupItem
                {
                    Asin = "AUTHOR123",
                    Name = "Andy Weir",
                    Image = "https://fresh.example.com/andy-weir.jpg",
                    Description = "Fresh biography"
                });

            audnexus
                .Setup(service => service.GetAuthorAsync("AUTHOR123", "us", false))
                .ReturnsAsync(new AudnexusAuthorResponse
                {
                    Asin = "AUTHOR123",
                    Name = "Andy Weir",
                    Similar = new List<AudnexusSimilarAuthor>
                    {
                        new() { Asin = "SIMNEW", Name = "Blake Crouch" }
                    }
                });

            var controller = new MetadataController(
                metadataService.Object,
                audimeta.Object,
                audnexus.Object,
                imageCache.Object,
                memoryCache,
                audiobookRepository.Object,
                asinLookup.Object,
                authorCatalogService.Object,
                seriesCatalogService.Object,
                logger.Object);

            var result = await controller.LookupAuthor("Andy Weir", "us", null, true);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var payload = Assert.IsType<MetadataController.AuthorLookupResponse>(ok.Value);

            Assert.Equal("Fresh biography", payload.Description);
            Assert.Single(payload.SimilarAuthors);
            Assert.Equal("Blake Crouch", payload.SimilarAuthors[0].Name);
            Assert.Equal("/config/cache/images/authors/AUTHOR123.jpg", payload.CachedPath);

            audimeta.Verify(service => service.GetAuthorByAsinAsync("AUTHOR123", "us"), Times.Once);
            imageCache.Verify(
                service => service.MoveToAuthorLibraryStorageAsync("AUTHOR123", "https://fresh.example.com/andy-weir.jpg", true),
                Times.Once);
            audiobookRepository.Verify(
                repository => repository.UpsertCachedAuthorAsync(It.Is<AuthorCacheEntry>(entry =>
                    entry.AuthorAsin == "AUTHOR123" &&
                    entry.Description == "Fresh biography" &&
                    entry.SimilarAuthors != null &&
                    entry.SimilarAuthors.Count == 1 &&
                    entry.SimilarAuthors[0].Name == "Blake Crouch")),
                Times.Once);
        }
    }
}
