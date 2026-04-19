using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using Listenarr.Api.Controllers;
using Listenarr.Api.Services;
using Listenarr.Application.Repositories;
using Listenarr.Application.Services;
using Listenarr.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class LibraryController_QualityCutoffTests
    {
        [Fact]
        [Trait("Scenario", "ImportPendingDownloadCountsAsActiveForQualityCutoff")]
        public async Task IsQualityCutoffMetAsync_ImportPendingDownload_ReturnsTrue()
        {
            var testDownload = new Download
            {
                Id = "dl-1",
                AudiobookId = 1,
                Status = DownloadStatus.ImportPending,
                Title = "Dune"
            };

            var downloadRepo = new Mock<IDownloadRepository>();
            downloadRepo.Setup(r => r.GetByAudiobookIdAsync(1, default)).ReturnsAsync(new List<Download> { testDownload });

            var audioFileRepo = new Mock<IAudiobookFileRepository>();
            audioFileRepo.Setup(r => r.GetByAudiobookIdAsync(1, default)).ReturnsAsync(new List<AudiobookFile>());

            var audiobook = new Audiobook
            {
                Id = 1,
                Title = "Dune",
                QualityProfile = new QualityProfile
                {
                    CutoffQuality = "MP3",
                    Qualities = new List<QualityDefinition>
                    {
                        new() { Quality = "MP3", Priority = 1 }
                    }
                }
            };

            var controller = new LibraryController(
                Mock.Of<IAudiobookRepository>(),
                Mock.Of<IImageCacheService>(),
                Mock.Of<ILogger<LibraryController>>(),
                Mock.Of<IServiceScopeFactory>(),
                Mock.Of<IHistoryRepository>(),
                audioFileRepo.Object,
                Mock.Of<IQualityProfileRepository>(),
                downloadRepo.Object,
                Mock.Of<IRootFolderRepository>(),
                Mock.Of<IDatabaseConnectionProvider>(),
                Mock.Of<IFileNamingService>());

            var method = typeof(LibraryController).GetMethod(
                "IsQualityCutoffMetAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var task = (Task<bool>?)method!.Invoke(controller, new object[] { audiobook, Mock.Of<IQualityProfileService>(), downloadRepo.Object, audioFileRepo.Object });
            Assert.NotNull(task);

            var result = await task!;

            Assert.True(result);
        }
    }
}
