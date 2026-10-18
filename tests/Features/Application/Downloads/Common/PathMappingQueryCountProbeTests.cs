using Listenarr.Infrastructure.Configuration.Paths;
using Listenarr.Infrastructure.Persistence;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Downloads.Common
{
    // Probe (not for upstream): counts real repository reads that one gateway poll issues
    // on canary, with the real RemotePathMappingService and EfRemotePathMappingRepository.
    [Trait("Name", "PathMappingQueryCountProbeTests")]
    [Trait("Category", "DownloadClientGateway")]
    public class PathMappingQueryCountProbeTests : BaseTests
    {
        private sealed class CountingRepository(IRemotePathMappingRepository inner) : IRemotePathMappingRepository
        {
            public int ByClientReads;
            public Task<List<RemotePathMapping>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);
            public Task<RemotePathMapping?> GetByIdAsync(int id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);
            public Task<List<RemotePathMapping>> GetByClientIdAsync(string downloadClientId, CancellationToken ct = default)
            {
                Interlocked.Increment(ref ByClientReads);
                return inner.GetByClientIdAsync(downloadClientId, ct);
            }
            public Task<RemotePathMapping> SaveAsync(RemotePathMapping mapping, CancellationToken ct = default) => inner.SaveAsync(mapping, ct);
            public Task<bool> DeleteAsync(int id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
        }

        private async Task<(DownloadClientGateway Gateway, CountingRepository Counter, RemotePathMappingService Service, DownloadClientConfiguration Client)> BuildAsync()
        {
            var client = new DownloadClientConfigurationBuilder().WithType("mock").Build();
            await _remotePathMappingRepository.SaveAsync(new RemotePathMappingBuilder()
                .WithDownloadClientConfiguration(client)
                .WithRemotePath(FileUtils.GetAbsolutePath("downloads"))
                .WithLocalPath(FileUtils.GetAbsolutePath("mnt", "wdelements", "downloads"))
                .Build());

            var counter = new CountingRepository(new EfRemotePathMappingRepository(
                _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>()));
            var service = new RemotePathMappingService(counter, NullLogger<RemotePathMappingService>.Instance,
                _provider.GetRequiredService<IMemoryCache>());
            var gateway = new DownloadClientGateway(service,
                _provider.GetRequiredService<IDownloadClientAdapterFactory>(),
                _provider.GetRequiredService<IFileSystem>(),
                _provider.GetRequiredService<IFileSystemSemanticsResolver>(),
                NullLogger<DownloadClientGateway>.Instance);
            return (gateway, counter, service, client);
        }

        [Fact]
        public async Task Control_SingleTranslateIssuesOneRead()
        {
            var (_, counter, service, client) = await BuildAsync();
            await service.TranslatePathAsync(client, DownloadClientAdapterMock_RemotePath());
            Assert.Equal(1, counter.ByClientReads);
        }

        [Fact]
        public async Task DefaultMockQueue_TwoItems_ReadsPerPoll()
        {
            var (gateway, counter, _, client) = await BuildAsync();
            var items = await gateway.GetQueueAsync(client);
            // item 1: RemotePath + ContentPath + 3 SourceFiles = 5; item 2: 1 + 1 + 5 = 7
            Assert.Equal(2, items.Count);
            Assert.Equal(12, counter.ByClientReads);
            await gateway.GetQueueAsync(client);
            Assert.Equal(24, counter.ByClientReads);
        }

        private static string DownloadClientAdapterMock_RemotePath() => DownloadCLientAdapterMock.RemotePath;
    }
}
