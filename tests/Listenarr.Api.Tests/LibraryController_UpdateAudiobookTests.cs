using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Listenarr.Api.Controllers;
using Listenarr.Api.Services;
using Listenarr.Application.Services;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class LibraryController_UpdateAudiobookTests
    {
        [Fact]
        public async Task UpdateAudiobook_PersistsExpandedMetadataFields()
        {
            // Arrange
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var dbContext = new ListenArrDbContext(options);

            var existingAudiobook = new Audiobook
            {
                Id = 1,
                Title = "Original Title",
                Subtitle = "Original Subtitle",
                Authors = new List<string> { "Original Author" },
                Narrators = new List<string> { "Original Narrator" },
                Description = "Original description",
                Publisher = "Original Publisher",
                Language = "english",
                PublishedDate = "2024-01-01",
                PublishYear = "2024",
                Runtime = 600,
                Edition = "Original Edition",
                Version = "Original Version",
                Series = "Original Series",
                SeriesNumber = "1",
                Genres = new List<string> { "Fantasy" },
                ImageUrl = "https://example.com/original.jpg",
                Tags = new List<string> { "tag-one" },
                Monitored = true,
                Explicit = false,
                Abridged = false,
            };

            var mockRepo = new Mock<IAudiobookRepository>();
            mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existingAudiobook);
            mockRepo.Setup(r => r.UpdateAsync(It.IsAny<Audiobook>())).ReturnsAsync(true);

            var mockImageCache = new Mock<IImageCacheService>();
            var mockLogger = new Mock<ILogger<LibraryController>>();
            var mockFileNaming = new Mock<IFileNamingService>();

            var services = new ServiceCollection();
            var provider = services.BuildServiceProvider();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            var controller = new LibraryController(
                mockRepo.Object,
                mockImageCache.Object,
                mockLogger.Object,
                dbContext,
                scopeFactory,
                mockFileNaming.Object);

            var updatedAudiobook = new Audiobook
            {
                Title = "Edited Title",
                Subtitle = "Edited Subtitle",
                Authors = new List<string> { "Edited Author" },
                Narrators = new List<string> { "Edited Narrator" },
                Description = "Edited description",
                Publisher = "Edited Publisher",
                Language = "swedish",
                PublishedDate = "2025-02-01",
                PublishYear = "2025",
                Runtime = 720,
                Edition = "Collector Edition",
                Version = "Edited Version",
                Series = "Edited Series",
                SeriesNumber = "2",
                Genres = new List<string> { "Sci-Fi", "Adventure" },
                ImageUrl = "https://example.com/edited.jpg",
                Tags = new List<string> { "tag-two" },
                Monitored = false,
                Explicit = true,
                Abridged = true,
            };

            // Act
            var actionResult = await controller.UpdateAudiobook(1, updatedAudiobook);

            // Assert
            Assert.IsType<OkObjectResult>(actionResult);
            Assert.Equal("Edited Title", existingAudiobook.Title);
            Assert.Equal("Edited Subtitle", existingAudiobook.Subtitle);
            Assert.Equal(new List<string> { "Edited Author" }, existingAudiobook.Authors);
            Assert.Equal(new List<string> { "Edited Narrator" }, existingAudiobook.Narrators);
            Assert.Equal("Edited description", existingAudiobook.Description);
            Assert.Equal("Edited Publisher", existingAudiobook.Publisher);
            Assert.Equal("swedish", existingAudiobook.Language);
            Assert.Equal("2025-02-01", existingAudiobook.PublishedDate);
            Assert.Equal("2025", existingAudiobook.PublishYear);
            Assert.Equal(720, existingAudiobook.Runtime);
            Assert.Equal("Collector Edition", existingAudiobook.Edition);
            Assert.Equal("Edited Version", existingAudiobook.Version);
            Assert.Equal("Edited Series", existingAudiobook.Series);
            Assert.Equal("2", existingAudiobook.SeriesNumber);
            Assert.Equal(new List<string> { "Sci-Fi", "Adventure" }, existingAudiobook.Genres);
            Assert.Equal("https://example.com/edited.jpg", existingAudiobook.ImageUrl);
            Assert.Equal(new List<string> { "tag-two" }, existingAudiobook.Tags);
            Assert.False(existingAudiobook.Monitored);
            Assert.True(existingAudiobook.Explicit);
            Assert.True(existingAudiobook.Abridged);

            mockRepo.Verify(r => r.UpdateAsync(existingAudiobook), Times.Once);
        }
    }
}
