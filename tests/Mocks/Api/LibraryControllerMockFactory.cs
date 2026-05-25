using Listenarr.Application.Interfaces;
using Listenarr.Application.Interfaces.Repositories;
using Listenarr.Application.Audiobooks;
using Listenarr.Domain.Models;
using Moq;

namespace Listenarr.Tests.Mocks.Api
{
    public static class LibraryControllerMockFactory
    {
        public static IApplicationPathService CreateApplicationPathService(string contentRootPath)
            => Mock.Of<IApplicationPathService>(service => service.ContentRootPath == contentRootPath);

        public static ILibraryListService CreateLibraryListService()
            => Mock.Of<ILibraryListService>();

        public static Mock<IAudiobookFileRepository> CreateAudiobookFileRepository(IEnumerable<AudiobookFile> files)
        {
            var fileList = files.ToList();
            var repository = new Mock<IAudiobookFileRepository>();
            repository
                .Setup(r => r.GetFormatSummariesAsync(default))
                .ReturnsAsync(fileList.Select(f => new AudiobookFormatSummary
                {
                    AudiobookId = f.AudiobookId,
                    Path = f.Path,
                    Format = f.Format,
                    Container = f.Container,
                    Codec = f.Codec,
                    Bitrate = f.Bitrate,
                }).ToList());
            repository
                .Setup(r => r.GetCountsByAudiobookIdAsync(default))
                .ReturnsAsync(fileList
                    .GroupBy(f => f.AudiobookId)
                    .ToDictionary(g => g.Key, g => g.Count()));

            return repository;
        }

        public static Mock<IAudiobookFileRepository> CreateEmptyAudiobookFileRepository()
            => CreateAudiobookFileRepository(Array.Empty<AudiobookFile>());
    }
}
