using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Listenarr.Api.Controllers;
using Listenarr.Api.Services;
using Listenarr.Domain.Models;
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

            db.AudiobookFiles.Add(new AudiobookFile
            {
                AudiobookId = book.Id,
                Path = $@"Z:\definitely-missing\{Guid.NewGuid():N}.m4b",
                Size = 1024,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            using var provider = new ServiceCollection().BuildServiceProvider();
            var controller = new LibraryController(
                Mock.Of<IAudiobookRepository>(),
                Mock.Of<IImageCacheService>(),
                NullLogger<LibraryController>.Instance,
                db,
                provider.GetRequiredService<IServiceScopeFactory>(),
                Mock.Of<IFileNamingService>());

            var actionResult = await controller.GetAll();
            var ok = Assert.IsType<OkObjectResult>(actionResult);

            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
            var wanted = doc.RootElement
                .EnumerateArray()
                .Single(item => item.GetProperty("id").GetInt32() == book.Id)
                .GetProperty("wanted")
                .GetBoolean();

            Assert.False(wanted);
        }
    }
}
