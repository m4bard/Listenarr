using Listenarr.Infrastructure.Persistence;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Configuration.Paths
{
    // Review probe only, not for upstream.
    [Trait("Name", "ReviewProbeCacheRaceTests")]
    public class ReviewProbeCacheRaceTests : BaseTests
    {
        // Lets a test hold a by-client read after it has hit the DB but before it returns.
        private sealed class GatedRepository(IRemotePathMappingRepository inner) : IRemotePathMappingRepository
        {
            public string? GateClientId;
            public readonly TaskCompletionSource ReadDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _reads;
            public int Reads => _reads;

            public Task<List<RemotePathMapping>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);
            public Task<RemotePathMapping?> GetByIdAsync(int id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);
            public async Task<List<RemotePathMapping>> GetByClientIdAsync(string downloadClientId, CancellationToken ct = default)
            {
                Interlocked.Increment(ref _reads);
                var result = await inner.GetByClientIdAsync(downloadClientId, ct);
                if (downloadClientId == GateClientId)
                {
                    ReadDone.TrySetResult();
                    await Release.Task;
                }
                return result;
            }
            public Task<RemotePathMapping> SaveAsync(RemotePathMapping mapping, CancellationToken ct = default) => inner.SaveAsync(mapping, ct);
            public Task<bool> DeleteAsync(int id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
        }

        private GatedRepository _repo = null!;
        private RemotePathMappingService _service = null!;
        private DownloadClientConfiguration _a = null!;
        private DownloadClientConfiguration _b = null!;

        public override async Task InitializeAsync()
        {
            _a = await CreateDownloadClientConfiguration();
            _b = await CreateDownloadClientConfiguration();
            _repo = new GatedRepository(new EfRemotePathMappingRepository(
                _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>()));
            _service = new RemotePathMappingService(_repo, NullLogger<RemotePathMappingService>.Instance,
                _provider.GetRequiredService<IMemoryCache>());
        }

        private RemotePathMapping Mapping(DownloadClientConfiguration c) => new RemotePathMappingBuilder()
            .WithDownloadClientConfiguration(c)
            .WithRemotePath(FileUtils.GetAbsolutePath("downloads"))
            .WithLocalPath(FileUtils.GetAbsolutePath("library"))
            .Build();

        // Control: create strictly after a completed cold read. Must see the mapping.
        [Fact]
        public async Task Control_CreateAfterColdReadCompletes_IsSeen()
        {
            Assert.Empty(await _service.GetPathMappingByClientAsync(_a));
            await _service.CreateAsync(Mapping(_a));
            Assert.Single(await _service.GetPathMappingByClientAsync(_a));
        }

        // Race: a cold read has fetched the old (empty) set; a create through the service
        // saves and clears the key; then the in-flight read stores the old set.
        [Fact]
        public async Task Race_CreateDuringInFlightColdRead_NextReadSeesMapping()
        {
            _repo.GateClientId = _a.Id;
            var inFlight = _service.GetPathMappingByClientAsync(_a);
            await _repo.ReadDone.Task;

            await _service.CreateAsync(Mapping(_a));
            _repo.GateClientId = null;
            _repo.Release.TrySetResult();
            Assert.Empty(await inFlight);

            Assert.Single(await _service.GetPathMappingByClientAsync(_a));
        }

        // Moving a mapping to another client clears only the new client's key.
        [Fact]
        public async Task MoveMappingToOtherClient_OldClientNoLongerSeesIt()
        {
            var created = await _service.CreateAsync(Mapping(_a));
            Assert.Single(await _service.GetPathMappingByClientAsync(_a));
            Assert.Empty(await _service.GetPathMappingByClientAsync(_b));

            created.DownloadClientId = _b.Id;
            await _service.UpdateAsync(created);

            Assert.Single(await _service.GetPathMappingByClientAsync(_b)); // control: new client invalidated
            Assert.Empty(await _service.GetPathMappingByClientAsync(_a));
        }

        // Per-client isolation of the cold-miss lock: a stalled read for A must not block B.
        [Fact]
        public async Task ColdMissLock_IsPerClient()
        {
            _repo.GateClientId = _a.Id;
            var stalledA = _service.GetPathMappingByClientAsync(_a);
            await _repo.ReadDone.Task;

            var b = _service.GetPathMappingByClientAsync(_b);
            var winner = await Task.WhenAny(b, Task.Delay(TimeSpan.FromSeconds(5)));
            _repo.Release.TrySetResult();
            await stalledA;
            Assert.Same(b, winner);
        }

        // The out-of-band window really ends at the TTL (the fifth PR test only pins its start).
        [Fact]
        public async Task ExpiryAfterTenSeconds_SeesOutOfBandRow()
        {
            Assert.Empty(await _service.GetPathMappingByClientAsync(_a));
            await _remotePathMappingRepository.SaveAsync(Mapping(_a));
            Assert.Empty(await _service.GetPathMappingByClientAsync(_a));
            await Task.Delay(TimeSpan.FromSeconds(10.5));
            Assert.Single(await _service.GetPathMappingByClientAsync(_a));
        }
    }
}
