using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Listenarr.Api.Controllers;
using Listenarr.Api.Services;
using Listenarr.Application.Repositories;
using Listenarr.Application.Services;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class LibraryController_WantedFlagRegressionTests
    {
        [Fact]
        public async Task GetAll_TreatsDbFileRecordAsNotWanted_EvenIfPathIsMissing()
        {
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            using var db = new ListenArrDbContext(options);

            var book = new Audiobook
            {
                Title = "Controller Book",
                Monitored = true
            };
            db.Audiobooks.Add(book);
            await db.SaveChangesAsync();

            var audioFile = new AudiobookFile
            {
                AudiobookId = book.Id,
                Path = $@"Z:\definitely-missing\{Guid.NewGuid():N}.m4b",
                Size = 1024,
                CreatedAt = DateTime.UtcNow
            };
            db.AudiobookFiles.Add(audioFile);
            await db.SaveChangesAsync();

            var allBooks = db.Audiobooks.ToList();
            var allFiles = db.AudiobookFiles.ToList();
            var allDownloads = new List<Download>();

            var mockRepo = new Mock<IAudiobookRepository>();
            mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(allBooks);

            var mockAudioFileRepo = new Mock<IAudiobookFileRepository>();
            mockAudioFileRepo.Setup(r => r.GetAllAsync(default)).ReturnsAsync(allFiles);

            var mockDownloadRepo = new Mock<IDownloadRepository>();
            mockDownloadRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(allDownloads);
            mockDownloadRepo.Setup(r => r.GetActiveAudiobookIdsAsync(It.IsAny<IEnumerable<DownloadStatus>>()))
                .ReturnsAsync(new List<int>());

            using var provider = new ServiceCollection().BuildServiceProvider();
            var controller = new LibraryController(
                mockRepo.Object,
                Mock.Of<IImageCacheService>(),
                NullLogger<LibraryController>.Instance,
                provider.GetRequiredService<IServiceScopeFactory>(),
                Mock.Of<IHistoryRepository>(),
                mockAudioFileRepo.Object,
                Mock.Of<IQualityProfileRepository>(),
                mockDownloadRepo.Object,
                Mock.Of<IRootFolderRepository>(),
                Mock.Of<IDatabaseConnectionProvider>(),
                Mock.Of<IFileNamingService>());

            var actionResult = await controller.GetAll();
            var ok = Assert.IsType<OkObjectResult>(actionResult);

            var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var doc = JsonDocument.Parse(json);
            var wanted = doc.RootElement
                .EnumerateArray()
                .Single(item => item.GetProperty("id").GetInt32() == book.Id)
                .GetProperty("wanted")
                .GetBoolean();

            Assert.False(wanted);
        }

        [Fact]
        public async Task GetAll_TreatsLegacyFilePathAsNotWanted_WhenNoFileRowsExist()
        {
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            using var db = new ListenArrDbContext(options);

            var book = new Audiobook
            {
                Title = "Legacy FilePath Book",
                Monitored = true,
                FilePath = @"C:\legacy\book.m4b",
                FileSize = 2048
            };
            db.Audiobooks.Add(book);
            await db.SaveChangesAsync();

            var allBooks = db.Audiobooks.ToList();

            var mockRepo = new Mock<IAudiobookRepository>();
            mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(allBooks);

            var mockAudioFileRepo = new Mock<IAudiobookFileRepository>();
            mockAudioFileRepo.Setup(r => r.GetAllAsync(default)).ReturnsAsync(new List<AudiobookFile>());

            var mockDownloadRepo = new Mock<IDownloadRepository>();
            mockDownloadRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Download>());
            mockDownloadRepo.Setup(r => r.GetActiveAudiobookIdsAsync(It.IsAny<IEnumerable<DownloadStatus>>()))
                .ReturnsAsync(new List<int>());

            using var provider = new ServiceCollection().BuildServiceProvider();
            var controller = new LibraryController(
                mockRepo.Object,
                Mock.Of<IImageCacheService>(),
                NullLogger<LibraryController>.Instance,
                provider.GetRequiredService<IServiceScopeFactory>(),
                Mock.Of<IHistoryRepository>(),
                mockAudioFileRepo.Object,
                Mock.Of<IQualityProfileRepository>(),
                mockDownloadRepo.Object,
                Mock.Of<IRootFolderRepository>(),
                Mock.Of<IDatabaseConnectionProvider>(),
                Mock.Of<IFileNamingService>());

            var actionResult = await controller.GetAll();
            var ok = Assert.IsType<OkObjectResult>(actionResult);

            var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var doc = JsonDocument.Parse(json);
            var item = doc.RootElement
                .EnumerateArray()
                .Single(element => element.GetProperty("id").GetInt32() == book.Id);

            Assert.False(item.GetProperty("wanted").GetBoolean());
            Assert.Equal("quality-match", item.GetProperty("status").GetString());
        }
    }
}
