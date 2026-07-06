using Listenarr.Tests.Mocks;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public sealed class RootFolderRelocationServiceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Join(
        Path.GetTempPath(),
        "listenarr-tests",
        $"relocation-{Guid.NewGuid():N}.db");
    private TestDbContextFactory _factory = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False")
            .Options;
        _factory = new TestDbContextFactory(options);
        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task StartRelocation_PersistsSagaAndJobsWithoutChangingRootOrAudiobooks()
    {
        var source = Path.Join(Path.GetTempPath(), $"relocation-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"relocation-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Join(source, "Author", "Title"));
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source };
            db.RootFolders.Add(root);
            db.Audiobooks.Add(new Audiobook
            {
                Title = "Title",
                BasePath = Path.Join(source, "Author", "Title")
            });
            await db.SaveChangesAsync();
            rootId = root.Id;
        }

        var service = CreateService();
        Assert.True(FileSystemPathIdentity.IsSameOrInside(
            Path.Join(source, "Author", "Title"),
            source,
            FileSystemPathSemantics.CurrentHostDefault));
        var result = await service.StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.Relocate,
                true,
                "Moved Library",
                true,
                FileSystemCaseSensitivityMode.Auto));

        await using var verification = await _factory.CreateDbContextAsync();
        var rootAfter = await verification.RootFolders.SingleAsync();
        var audiobookAfter = await verification.Audiobooks.SingleAsync();
        var relocation = await verification.RootFolderRelocations.SingleAsync();
        var job = await verification.MoveJobs.SingleAsync();
        Assert.Equal(source, rootAfter.Path);
        Assert.Equal(Path.Join(source, "Author", "Title"), audiobookAfter.BasePath);
        Assert.Equal(rootId, relocation.ActiveRootFolderId);
        Assert.Equal(relocation.Id, job.RelocationId);
        Assert.Equal(RootFolderRelocationStatus.Pending, result.Status);
        Assert.True(await service.IsBoundaryProtectedAsync(
            target,
            FileSystemPathSemantics.CurrentHostDefault));
        Assert.True(await service.IsBoundaryProtectedAsync(
            source,
            FileSystemPathSemantics.CurrentHostDefault));
    }

    [Fact]
    public async Task MetadataOnly_UpdatesRootAndAudiobooksInOneTransaction()
    {
        var source = Path.Join(Path.GetTempPath(), $"metadata-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"metadata-target-{Guid.NewGuid():N}");
        var unrelated = Path.Join(Path.GetTempPath(), $"metadata-unrelated-{Guid.NewGuid():N}", "bonus.mp3");
        Directory.CreateDirectory(source);
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source };
            db.RootFolders.Add(root);
            var localBasePath = Path.Join(source, "Title");
            db.Audiobooks.AddRange(
                new Audiobook
                {
                    Title = "Title",
                    BasePath = localBasePath,
                    FilePath = Path.Join(localBasePath, "book.m4b"),
                    ImageUrl = Path.Join(localBasePath, "cover.jpg"),
                    Files =
                    [
                        new AudiobookFile { Path = Path.Join(localBasePath, "book.m4b") },
                        new AudiobookFile { Path = Path.Join("disc-1", "chapter.mp3") },
                        new AudiobookFile { Path = unrelated }
                    ]
                },
                new Audiobook
                {
                    Title = "Remote Image",
                    BasePath = Path.Join(source, "Remote Image"),
                    ImageUrl = "https://example.test/cover.jpg"
                });
            await db.SaveChangesAsync();
            rootId = root.Id;
        }

        var result = await CreateService().StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.MetadataOnly,
                false,
                "Metadata Library",
                false,
                FileSystemCaseSensitivityMode.Auto));

        await using var verification = await _factory.CreateDbContextAsync();
        Assert.Equal(target, (await verification.RootFolders.SingleAsync()).Path);
        var audiobooks = await verification.Audiobooks
            .Include(audiobook => audiobook.Files)
            .OrderBy(audiobook => audiobook.Title)
            .ToListAsync();
        var remoteImageAudiobook = audiobooks[0];
        var localAudiobook = audiobooks[1];
        var expectedBasePath = Path.Join(target, "Title");
        Assert.Equal(expectedBasePath, localAudiobook.BasePath);
        Assert.Equal(Path.Join(expectedBasePath, "book.m4b"), localAudiobook.FilePath);
        Assert.Equal(Path.Join(expectedBasePath, "cover.jpg"), localAudiobook.ImageUrl);
        Assert.Contains(localAudiobook.Files!, file => file.Path == Path.Join(expectedBasePath, "book.m4b"));
        Assert.Contains(localAudiobook.Files!, file => file.Path == Path.Join("disc-1", "chapter.mp3"));
        Assert.Contains(localAudiobook.Files!, file => file.Path == unrelated);
        Assert.Equal(Path.Join(target, "Remote Image"), remoteImageAudiobook.BasePath);
        Assert.Equal("https://example.test/cover.jpg", remoteImageAudiobook.ImageUrl);
        Assert.Empty(await verification.RootFolderRelocations.ToListAsync());
        Assert.Equal(RootFolderRelocationStatus.Completed, result.Status);
    }

    [Fact]
    public async Task MetadataOnly_UnmappableReferenceRollsBackRootAndAllAudiobooks()
    {
        var source = Path.Join(Path.GetTempPath(), $"metadata-rollback-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"metadata-rollback-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source };
            db.RootFolders.Add(root);
            var validBasePath = Path.Join(source, "A Valid");
            var invalidBasePath = Path.Join(source, "Z Invalid");
            db.Audiobooks.AddRange(
                new Audiobook
                {
                    Title = "A Valid",
                    BasePath = validBasePath,
                    FilePath = Path.Join(validBasePath, "book.m4b")
                },
                new Audiobook
                {
                    Title = "Z Invalid",
                    BasePath = invalidBasePath,
                    FilePath = invalidBasePath
                });
            await db.SaveChangesAsync();
            rootId = root.Id;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.MetadataOnly,
                false,
                "Moved Library",
                false,
                FileSystemCaseSensitivityMode.Auto)));

        await using var verification = await _factory.CreateDbContextAsync();
        Assert.Equal(source, (await verification.RootFolders.SingleAsync()).Path);
        var audiobooks = await verification.Audiobooks.OrderBy(audiobook => audiobook.Title).ToListAsync();
        Assert.Equal(Path.Join(source, "A Valid"), audiobooks[0].BasePath);
        Assert.Equal(Path.Join(source, "A Valid", "book.m4b"), audiobooks[0].FilePath);
        Assert.Equal(Path.Join(source, "Z Invalid"), audiobooks[1].BasePath);
        Assert.Equal(Path.Join(source, "Z Invalid"), audiobooks[1].FilePath);
    }

    [Fact]
    public async Task CompletedJobs_FinalizeRootOnlyAfterEveryAudiobookPathMoved()
    {
        var source = Path.Join(Path.GetTempPath(), $"finalize-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"finalize-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source };
            db.RootFolders.Add(root);
            db.Audiobooks.Add(new Audiobook { Title = "Title", BasePath = Path.Join(source, "Title") });
            await db.SaveChangesAsync();
            rootId = root.Id;
        }

        var service = CreateService();
        await service.StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.Relocate,
                true,
                "Finalized Library",
                false,
                FileSystemCaseSensitivityMode.Auto));
        Guid jobId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var job = await db.MoveJobs.SingleAsync();
            job.Status = MoveJobStatus.Completed;
            job.ActiveDeduplicationKey = null;
            var audiobook = await db.Audiobooks.SingleAsync();
            audiobook.BasePath = job.RequestedPath;
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        await service.OnMoveJobStateChangedAsync(jobId);

        await using var verification = await _factory.CreateDbContextAsync();
        var rootAfter = await verification.RootFolders.SingleAsync();
        var relocationAfter = await verification.RootFolderRelocations.SingleAsync();
        Assert.Equal(target, rootAfter.Path);
        Assert.Equal("Finalized Library", rootAfter.Name);
        Assert.Equal(RootFolderRelocationStatus.Completed, relocationAfter.Status);
        Assert.Null(relocationAfter.ActiveRootFolderId);
    }

    [Fact]
    public async Task SupersededJob_ReconciliationRequiresAttentionAndCanRetry()
    {
        var source = Path.Join(Path.GetTempPath(), $"superseded-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"superseded-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source };
            db.RootFolders.Add(root);
            db.Audiobooks.Add(new Audiobook { Title = "Title", BasePath = Path.Join(source, "Title") });
            await db.SaveChangesAsync();
            rootId = root.Id;
        }

        var service = CreateService();
        var started = await service.StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.Relocate,
                true,
                "Library",
                false,
                FileSystemCaseSensitivityMode.Auto));
        Guid jobId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var job = await db.MoveJobs.SingleAsync();
            job.Status = MoveJobStatus.Superseded;
            job.ActiveDeduplicationKey = null;
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        await service.ReconcileActiveAsync();
        var needsAttention = await service.GetAsync(started.RelocationId!.Value);
        Assert.Equal(RootFolderRelocationStatus.NeedsAttention, needsAttention!.Status);

        await service.RetryAsync(started.RelocationId.Value);
        await using var verification = await _factory.CreateDbContextAsync();
        Assert.Equal(MoveJobStatus.Queued, (await verification.MoveJobs.SingleAsync()).Status);
        Assert.Equal(jobId, (await verification.MoveJobs.SingleAsync()).Id);
    }

    [Fact]
    public async Task RetryAsync_SupersededJobWithCanonicalReplacement_DoesNotCollide()
    {
        var source = Path.Join(Path.GetTempPath(), $"superseded-collision-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"superseded-collision-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source };
            db.RootFolders.Add(root);
            db.Audiobooks.Add(new Audiobook { Title = "Title", BasePath = Path.Join(source, "Title") });
            await db.SaveChangesAsync();
            rootId = root.Id;
        }

        var service = CreateService();
        var started = await service.StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.Relocate,
                true,
                "Library",
                false,
                FileSystemCaseSensitivityMode.Auto));
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var superseded = await db.MoveJobs.SingleAsync();
            var key = superseded.ActiveDeduplicationKey;
            superseded.Status = MoveJobStatus.Superseded;
            superseded.ActiveDeduplicationKey = null;
            db.MoveJobs.Add(new MoveJob
            {
                AudiobookId = superseded.AudiobookId,
                RequestedPath = superseded.RequestedPath,
                Status = MoveJobStatus.Running,
                ActiveDeduplicationKey = key
            });
            var relocation = await db.RootFolderRelocations.SingleAsync();
            relocation.Status = RootFolderRelocationStatus.NeedsAttention;
            await db.SaveChangesAsync();
        }

        var result = await service.RetryAsync(started.RelocationId!.Value);

        Assert.Equal(RootFolderRelocationStatus.NeedsAttention, result.Status);
        Assert.Contains("were superseded by a newer move", result.Error);
        await using var verification = await _factory.CreateDbContextAsync();
        Assert.Equal(
            MoveJobStatus.Superseded,
            (await verification.MoveJobs.SingleAsync(job => job.RelocationId != null)).Status);
        Assert.Equal(
            MoveJobStatus.Running,
            (await verification.MoveJobs.SingleAsync(job => job.RelocationId == null)).Status);
    }

    [Fact]
    public async Task StartRelocation_BroadcastFailureDoesNotUndoCommittedSaga()
    {
        var source = Path.Join(Path.GetTempPath(), $"broadcast-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"broadcast-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source };
            db.RootFolders.Add(root);
            db.Audiobooks.Add(new Audiobook { Title = "Title", BasePath = Path.Join(source, "Title") });
            await db.SaveChangesAsync();
            rootId = root.Id;
        }

        var service = new RootFolderRelocationService(
            _factory,
            new FileSystemSemanticsResolver(),
            new ThrowingHubBroadcaster(),
            TimeProvider.System);
        var result = await service.StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.Relocate,
                true,
                "Library",
                false,
                FileSystemCaseSensitivityMode.Auto));

        Assert.NotNull(result.RelocationId);
        await using var verification = await _factory.CreateDbContextAsync();
        Assert.Single(await verification.RootFolderRelocations.ToListAsync());
        Assert.Single(await verification.MoveJobs.ToListAsync());
    }

    private RootFolderRelocationService CreateService() => new(
        _factory,
        new FileSystemSemanticsResolver(),
        new NoopHubBroadcaster(),
        TimeProvider.System);

    private sealed class TestDbContextFactory(DbContextOptions<ListenArrDbContext> options)
        : IDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext() => new(options);
        public Task<ListenArrDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class ThrowingHubBroadcaster : IHubBroadcaster
    {
        public Task BroadcastQueueUpdateAsync(QueueSnapshot queueSnapshot) => Task.CompletedTask;

        public Task BroadcastAsync(
            string method,
            object payload,
            CancellationToken cancellationToken = default) =>
            throw new IOException("SignalR unavailable");

        public Task BroadcastAsync(
            RealtimeHubTarget target,
            string method,
            object payload,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
