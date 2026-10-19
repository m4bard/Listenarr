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

            // Not awaited before release: a fix that serialises writers behind the fill would
            // otherwise deadlock here. The 1 s window gives an unserialised writer time to finish.
            var create = _service.CreateAsync(Mapping(_a));
            await Task.WhenAny(create, Task.Delay(TimeSpan.FromSeconds(1)));
            _repo.GateClientId = null;
            _repo.Release.TrySetResult();
            Assert.Empty(await inFlight);
            await create;

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

        // Writer isolation: a stalled fill for A must not block a create, update or delete on B.
        [Fact]
        public async Task WritesOnOtherClient_DoNotWaitForAStalledFill()
        {
            var onB = await _service.CreateAsync(Mapping(_b));
            _repo.GateClientId = _a.Id;
            var stalledA = _service.GetPathMappingByClientAsync(_a);
            await _repo.ReadDone.Task;

            var writes = Task.Run(async () =>
            {
                var created = await _service.CreateAsync(Mapping(_b));
                created.LocalPath = FileUtils.GetAbsolutePath("library2");
                await _service.UpdateAsync(created);
                await _service.DeleteAsync(onB.Id);
            });
            var winner = await Task.WhenAny(writes, Task.Delay(TimeSpan.FromSeconds(5)));
            _repo.Release.TrySetResult();
            await stalledA;
            Assert.Same(writes, winner);
            await writes;
        }

        // Deadlock check: a move A->B while A's fill is stalled waits (correct), then completes
        // once the fill finishes; concurrent opposite moves B->A and A->B also both complete.
        [Fact]
        public async Task MovesBetweenClients_InBothDirectionsConcurrently_Complete()
        {
            var m1 = await _service.CreateAsync(Mapping(_a));
            var m2 = await _service.CreateAsync(new RemotePathMappingBuilder()
                .WithDownloadClientConfiguration(_b)
                .WithRemotePath(FileUtils.GetAbsolutePath("other"))
                .WithLocalPath(FileUtils.GetAbsolutePath("library"))
                .Build());

            _repo.GateClientId = _a.Id;
            var stalledA = _service.GetPathMappingByClientAsync(_a);
            await _repo.ReadDone.Task;

            m1.DownloadClientId = _b.Id;
            m2.DownloadClientId = _a.Id;
            var moves = Task.WhenAll(
                Task.Run(() => _service.UpdateAsync(m1)),
                Task.Run(() => _service.UpdateAsync(m2)));
            await Task.WhenAny(moves, Task.Delay(TimeSpan.FromMilliseconds(500)));
            Assert.False(moves.IsCompleted); // control: both touch A, so both must wait on the fill
            _repo.GateClientId = null;
            _repo.Release.TrySetResult();
            await stalledA;

            var done = await Task.WhenAny(moves, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(moves, done);
            Assert.Equal(m2.Id, Assert.Single(await _service.GetPathMappingByClientAsync(_a)).Id);
            Assert.Equal(m1.Id, Assert.Single(await _service.GetPathMappingByClientAsync(_b)).Id);
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
