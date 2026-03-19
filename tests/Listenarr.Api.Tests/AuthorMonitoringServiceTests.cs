using System.Collections.Generic;
using System.Threading.Tasks;
using Listenarr.Api.Services;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class AuthorMonitoringServiceTests
    {
        [Fact]
        public async Task MonitorAuthorAsync_PersistsAuthorAndAddsOnlyMissingBooksForSelectedLanguage()
        {
            var dbOptions = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(databaseName: $"author-monitor-{System.Guid.NewGuid():N}")
                .Options;

            await using var dbContext = new ListenArrDbContext(dbOptions);
            dbContext.Audiobooks.Add(new Audiobook
            {
                Id = 10,
                Title = "Project Hail Mary",
                Authors = new List<string> { "Andy Weir" },
                Language = "english",
                Monitored = true
            });
            await dbContext.SaveChangesAsync();

            var authorCatalogService = new Mock<IAuthorCatalogService>();
            authorCatalogService
                .Setup(service => service.GetCatalogAsync("Andy Weir", "uk", 500, null, false, It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new AuthorCatalogFetchResult
                {
                    Author = new AuthorLookupItem
                    {
                        Asin = "AUTHOR123",
                        Name = "Andy Weir"
                    },
                    Books = new List<AudimetaSearchResult>
                    {
                        new()
                        {
                            Title = "Project Hail Mary",
                            Authors = new List<AudimetaAuthor> { new() { Name = "Andy Weir" } },
                            Language = "en-us"
                        },
                        new()
                        {
                            Asin = "B000MARTIAN",
                            Title = "The Martian",
                            Authors = new List<AudimetaAuthor> { new() { Name = "Andy Weir" } },
                            Language = "english"
                        },
                        new()
                        {
                            Asin = "B000GERMAN",
                            Title = "Der Marsianer",
                            Authors = new List<AudimetaAuthor> { new() { Name = "Andy Weir" } },
                            Language = "de"
                        }
                    }
                });

            var libraryAddService = new Mock<ILibraryAddService>();
            libraryAddService
                .Setup(service => service.AddToLibraryAsync(
                    It.Is<LibraryAddOperationRequest>(request =>
                        request.Metadata.Title == "The Martian" &&
                        request.Monitored &&
                        request.HistorySource == "AuthorMonitoring"),
                    It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new LibraryAddOperationResult
                {
                    Added = true,
                    Message = "Audiobook added to library successfully",
                    Audiobook = new Audiobook
                    {
                        Id = 11,
                        Title = "The Martian",
                        Authors = new List<string> { "Andy Weir" },
                        Asin = "B000MARTIAN",
                        Monitored = true
                    }
                });

            var service = new AuthorMonitoringService(
                dbContext,
                authorCatalogService.Object,
                libraryAddService.Object,
                Mock.Of<ILogger<AuthorMonitoringService>>());

            var result = await service.MonitorAuthorAsync(new MonitorAuthorRequest
            {
                Name = "Andy Weir",
                Region = "uk",
                Language = "english"
            });

            Assert.NotNull(result.MonitoredAuthor);
            Assert.True(result.SyncResult.Succeeded);
            Assert.Equal(1, result.SyncResult.AddedCount);
            Assert.Equal(1, result.SyncResult.ExistingCount);
            Assert.Equal(0, result.SyncResult.FailedCount);
            Assert.Equal("AUTHOR123", result.MonitoredAuthor!.AuthorAsin);
            Assert.Equal("uk", result.MonitoredAuthor.Region);
            Assert.Equal("english", result.MonitoredAuthor.Language);
            Assert.NotNull(result.MonitoredAuthor.LastSuccessfulSyncAt);

            libraryAddService.Verify(service => service.AddToLibraryAsync(
                It.IsAny<LibraryAddOperationRequest>(),
                It.IsAny<System.Threading.CancellationToken>()),
                Times.Once);

            var storedAuthor = await dbContext.MonitoredAuthors.SingleAsync();
            Assert.Equal("Andy Weir", storedAuthor.AuthorName);
            Assert.Equal("andy weir", storedAuthor.AuthorNameNormalized);
            Assert.Equal("AUTHOR123", storedAuthor.AuthorAsin);
        }
    }
}
