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
    }

    [Fact]
    public async Task MetadataOnly_UpdatesRootAndAudiobooksInOneTransaction()
    {
        var source = Path.Join(Path.GetTempPath(), $"metadata-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"metadata-target-{Guid.NewGuid():N}");
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
        Assert.Equal(Path.Join(target, "Title"), (await verification.Audiobooks.SingleAsync()).BasePath);
        Assert.Empty(await verification.RootFolderRelocations.ToListAsync());
        Assert.Equal(RootFolderRelocationStatus.Completed, result.Status);
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

        await service.OnMoveJobStateChangedAsync(jobId, MoveJobStatus.Completed);

        await using var verification = await _factory.CreateDbContextAsync();
        var rootAfter = await verification.RootFolders.SingleAsync();
        var relocationAfter = await verification.RootFolderRelocations.SingleAsync();
        Assert.Equal(target, rootAfter.Path);
        Assert.Equal("Finalized Library", rootAfter.Name);
        Assert.Equal(RootFolderRelocationStatus.Completed, relocationAfter.Status);
        Assert.Null(relocationAfter.ActiveRootFolderId);
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
}
