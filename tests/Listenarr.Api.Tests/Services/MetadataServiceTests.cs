using Listenarr.Api.Services;
using Listenarr.Api.Services.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests.Services
{
    [Trait("Category", "MetadataService")]
    public class MetadataServiceTests : BaseTests
    {
        private readonly string CLIENT_CONFIG_ID = "empty-1";
        private readonly string DOWNLOAD_ID = "dl-1";
        private readonly int AUDIOBOOK_ID = 1;

        [Fact]
        [Trait("Method", "FetchMetadataAsync")]
        public async Task FetchMetadataAsync()
        {
            var sourceDirectory = GetTempDirectory("FetchMetadataAsync");
            var filePath = await GetFileAsync(sourceDirectory, "03 - Seconde Fondation Isaac Asimov.mp3");
            var ffprobePath = await GetFileAsync(sourceDirectory, "ffprobefake");

            var download = new Download
            {
                Id = DOWNLOAD_ID,
                DownloadClientId = CLIENT_CONFIG_ID,
                Metadata = new Dictionary<string, object>
                {
                    ["Uploader"] = "AnotherOneBiteTheDust",
                    ["Protocol"] = DownloadProtocol.Torrent,
                }
            };

            var audiobook = new Audiobook
            {
                Id = AUDIOBOOK_ID,
                Title = "Seconde Fondation",
                Authors = [
                    "Isaac Asimov"
                ],
                PublishYear = "1996",
                Series = "Le Cycle de Fondation"
            };

            var job = new DownloadProcessingJob
            {
                SourcePath = filePath
            };

            var ffmpegServiceMock = new Mock<IFfmpegService>();
            ffmpegServiceMock.Setup(s => s.GetFfprobePathAsync()).ReturnsAsync(ffprobePath);
            ffmpegServiceMock.Setup(s => s.RunFfprobeAsync(filePath))
                .ReturnsAsync(new AudioMetadata
                {
                    Title = "Super Tag",
                    Album = "Awesome unrelated",
                    Artist = "Mister nobody",
                    DiscNumber = 1,
                    TrackNumber = 3,
                    Year = 2026
                });

            var provider = MockUtils.CreateServiceProvider();
            var metadataService = new MetadataService(
                new Mock<HttpClient>().Object,
                provider.GetRequiredService<IConfigurationService>(),
                new Mock<ILogger<MetadataService>>().Object,
                ffmpegServiceMock.Object);

            var audiobookRepository = provider.GetRequiredService<IAudiobookRepository>();
            var downloadRepository = provider.GetRequiredService<IDownloadRepository>();

            await audiobookRepository.AddAsync(audiobook);
            await downloadRepository.AddAsync(download);

            var metadata = await metadataService.FetchMetadataAsync(job, download, audiobook, default);

            Assert.NotNull(metadata);
            Assert.Equal("Le Cycle de Fondation", metadata.Series);
            Assert.Equal("Seconde Fondation", metadata.Title);
            Assert.Equal("Isaac Asimov", metadata.Artist);
            Assert.Equal("Isaac Asimov", metadata.AlbumArtist);
            Assert.Equal(1996, metadata.Year);
            Assert.Equal(3, metadata.TrackNumber);
            Assert.Equal(1, metadata.DiscNumber);
        }
    }
}
