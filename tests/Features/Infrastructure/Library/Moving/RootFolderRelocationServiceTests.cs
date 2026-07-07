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
    public async Task MetadataOnly_UnmappableReferenceSkipsBadAudiobookAndPersistsAttentionRecord()
    {
        var source = Path.Join(Path.GetTempPath(), $"metadata-skip-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"metadata-skip-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        int rootId;
        int invalidAudiobookId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source };
            db.RootFolders.Add(root);
            var firstBasePath = Path.Join(source, "A Valid");
            var invalidBasePath = Path.Join(source, "M Invalid");
            var lastBasePath = Path.Join(source, "Z Valid");
            var invalid = new Audiobook
            {
                Title = "M Invalid",
                BasePath = invalidBasePath,
                FilePath = invalidBasePath,
                ImageUrl = Path.Join(invalidBasePath, "cover.jpg")
            };
            db.Audiobooks.AddRange(
                new Audiobook
                {
                    Title = "A Valid",
                    BasePath = firstBasePath,
                    FilePath = Path.Join(firstBasePath, "book.m4b"),
                    ImageUrl = Path.Join(firstBasePath, "cover.jpg")
                },
                invalid,
                new Audiobook
                {
                    Title = "Z Valid",
                    BasePath = lastBasePath,
                    FilePath = Path.Join(lastBasePath, "book.m4b"),
                    ImageUrl = Path.Join(lastBasePath, "cover.jpg")
                });
            await db.SaveChangesAsync();
            rootId = root.Id;
            invalidAudiobookId = invalid.Id;
        }

        var result = await CreateService().StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.MetadataOnly,
                false,
                "Moved Library",
                false,
                FileSystemCaseSensitivityMode.Auto));

        await using var verification = await _factory.CreateDbContextAsync();
        Assert.Equal(target, (await verification.RootFolders.SingleAsync()).Path);
        Assert.Equal(RootFolderRelocationStatus.NeedsAttention, result.Status);
        Assert.Equal(3, result.TotalJobs);
        Assert.Equal(2, result.CompletedJobs);
        Assert.NotNull(result.RelocationId);

        var audiobooks = await verification.Audiobooks.OrderBy(audiobook => audiobook.Title).ToListAsync();
        Assert.Equal(Path.Join(target, "A Valid"), audiobooks[0].BasePath);
        Assert.Equal(Path.Join(target, "A Valid", "book.m4b"), audiobooks[0].FilePath);
        Assert.Equal(Path.Join(target, "A Valid", "cover.jpg"), audiobooks[0].ImageUrl);
        Assert.Equal(Path.Join(source, "M Invalid"), audiobooks[1].BasePath);
        Assert.Equal(Path.Join(source, "M Invalid"), audiobooks[1].FilePath);
        Assert.Equal(Path.Join(source, "M Invalid", "cover.jpg"), audiobooks[1].ImageUrl);
        Assert.Equal(Path.Join(target, "Z Valid"), audiobooks[2].BasePath);
        Assert.Equal(Path.Join(target, "Z Valid", "book.m4b"), audiobooks[2].FilePath);
        Assert.Equal(Path.Join(target, "Z Valid", "cover.jpg"), audiobooks[2].ImageUrl);

        var relocation = await verification.RootFolderRelocations
            .Include(candidate => candidate.SkippedItems)
            .SingleAsync();
        var skipped = Assert.Single(relocation.SkippedItems);
        Assert.Equal(rootId, relocation.ActiveRootFolderId);
        Assert.Equal(RootFolderRelocationStatus.NeedsAttention, relocation.Status);
        Assert.Equal(3, relocation.TotalJobs);
        Assert.Equal(2, relocation.CompletedJobs);
        Assert.Equal(invalidAudiobookId, skipped.AudiobookId);
        Assert.Contains("could not be mapped", skipped.Reason);
        Assert.Empty(await verification.MoveJobs.ToListAsync());
    }

    [Fact]
    public async Task RetryAsync_MetadataOnlySkippedAudiobookRewritesAndClearsAttentionRecord()
    {
        var source = Path.Join(Path.GetTempPath(), $"metadata-retry-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"metadata-retry-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        int rootId;
        int audiobookId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source };
            db.RootFolders.Add(root);
            var basePath = Path.Join(source, "Title");
            var audiobook = new Audiobook
            {
                Title = "Title",
                BasePath = basePath,
                FilePath = basePath,
                ImageUrl = Path.Join(basePath, "cover.jpg")
            };
            db.Audiobooks.Add(audiobook);
            await db.SaveChangesAsync();
            rootId = root.Id;
            audiobookId = audiobook.Id;
        }

        var service = CreateService();
        var started = await service.StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.MetadataOnly,
                false,
                "Moved Library",
                false,
                FileSystemCaseSensitivityMode.Auto));
        Assert.Equal(RootFolderRelocationStatus.NeedsAttention, started.Status);

        await using (var db = await _factory.CreateDbContextAsync())
        {
            var audiobook = await db.Audiobooks.SingleAsync(candidate => candidate.Id == audiobookId);
            audiobook.FilePath = Path.Join(source, "Title", "book.m4b");
            await db.SaveChangesAsync();
        }

        var result = await service.RetryAsync(started.RelocationId!.Value);

        Assert.Equal(RootFolderRelocationStatus.Completed, result.Status);
        Assert.Equal(1, result.CompletedJobs);
        Assert.Null(result.Error);
        await using var verification = await _factory.CreateDbContextAsync();
        var relocation = await verification.RootFolderRelocations.SingleAsync();
        var audiobookAfter = await verification.Audiobooks.SingleAsync();
        Assert.Null(relocation.ActiveRootFolderId);
        Assert.Equal(RootFolderRelocationStatus.Completed, relocation.Status);
        Assert.Empty(await verification.RootFolderRelocationSkippedItems.ToListAsync());
        Assert.Equal(Path.Join(target, "Title"), audiobookAfter.BasePath);
        Assert.Equal(Path.Join(target, "Title", "book.m4b"), audiobookAfter.FilePath);
        Assert.Equal(Path.Join(target, "Title", "cover.jpg"), audiobookAfter.ImageUrl);
    }

    [Fact]
    public async Task RetryAsync_AllJobsCompletedAfterFinalizationBlocked_AppliesRootMetadataAndCompletes()
    {
        var source = Path.Join(Path.GetTempPath(), $"retry-finalize-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"retry-finalize-target-{Guid.NewGuid():N}");
        var otherRootPath = Path.Join(Path.GetTempPath(), $"retry-finalize-other-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder
            {
                Name = "Library",
                Path = source,
                CaseSensitivityMode = FileSystemCaseSensitivityMode.Sensitive,
                ResolvedCaseSensitivity = FileSystemCaseSensitivity.Sensitive,
                PathIdentityState = PathIdentityState.Valid
            };
            db.RootFolders.Add(root);
            db.RootFolders.Add(new RootFolder
            {
                Name = "Other",
                Path = otherRootPath,
                IsDefault = true,
                CaseSensitivityMode = FileSystemCaseSensitivityMode.Sensitive,
                ResolvedCaseSensitivity = FileSystemCaseSensitivity.Sensitive,
                PathIdentityState = PathIdentityState.Valid
            });
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
                "Moved Library",
                true,
                FileSystemCaseSensitivityMode.Insensitive));
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var job = await db.MoveJobs.SingleAsync();
            job.Status = MoveJobStatus.Completed;
            job.ActiveDeduplicationKey = null;
            var relocation = await db.RootFolderRelocations.SingleAsync();
            relocation.Status = RootFolderRelocationStatus.NeedsAttention;
            relocation.Error = "Target filesystem identity became unavailable during finalization.";
            await db.SaveChangesAsync();
        }

        var result = await service.RetryAsync(started.RelocationId!.Value);

        Assert.Equal(RootFolderRelocationStatus.Completed, result.Status);
        await using var verification = await _factory.CreateDbContextAsync();
        var rootAfter = await verification.RootFolders.SingleAsync(root => root.Id == rootId);
        var otherRootAfter = await verification.RootFolders.SingleAsync(root => root.Id != rootId);
        var relocationAfter = await verification.RootFolderRelocations.SingleAsync();
        Assert.Equal(target, rootAfter.Path);
        Assert.Equal("Moved Library", rootAfter.Name);
        Assert.True(rootAfter.IsDefault);
        Assert.False(otherRootAfter.IsDefault);
        Assert.Equal(FileSystemCaseSensitivityMode.Insensitive, rootAfter.CaseSensitivityMode);
        Assert.Equal(FileSystemCaseSensitivity.Insensitive, rootAfter.ResolvedCaseSensitivity);
        Assert.Null(relocationAfter.ActiveRootFolderId);
        Assert.Equal(RootFolderRelocationStatus.Completed, relocationAfter.Status);
        Assert.Equal(relocationAfter.TotalJobs, relocationAfter.CompletedJobs);
    }

    [Fact]
    public async Task RetryAsync_AllJobsCompletedButTargetStillUnavailable_StaysNeedsAttentionWithoutMutatingRoot()
    {
        var source = Path.Join(Path.GetTempPath(), $"retry-finalize-unavailable-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"retry-finalize-unavailable-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder
            {
                Name = "Library",
                Path = source,
                CaseSensitivityMode = FileSystemCaseSensitivityMode.Sensitive,
                ResolvedCaseSensitivity = FileSystemCaseSensitivity.Sensitive,
                PathIdentityState = PathIdentityState.Valid
            };
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
                "Moved Library",
                true,
                FileSystemCaseSensitivityMode.Insensitive));
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var job = await db.MoveJobs.SingleAsync();
            job.Status = MoveJobStatus.Completed;
            job.ActiveDeduplicationKey = null;
            var relocation = await db.RootFolderRelocations.SingleAsync();
            relocation.Status = RootFolderRelocationStatus.NeedsAttention;
            relocation.Error = "Target filesystem identity became unavailable during finalization.";
            await db.SaveChangesAsync();
        }

        var retryService = new RootFolderRelocationService(
            _factory,
            new TargetUnavailableSemanticsResolver(target),
            new NoopHubBroadcaster(),
            TimeProvider.System);
        var result = await retryService.RetryAsync(started.RelocationId!.Value);

        Assert.Equal(RootFolderRelocationStatus.NeedsAttention, result.Status);
        Assert.Contains("became unavailable", result.Error, StringComparison.OrdinalIgnoreCase);
        await using var verification = await _factory.CreateDbContextAsync();
        var rootAfter = await verification.RootFolders.SingleAsync(root => root.Id == rootId);
        var relocationAfter = await verification.RootFolderRelocations.SingleAsync();
        Assert.Equal(source, rootAfter.Path);
        Assert.Equal("Library", rootAfter.Name);
        Assert.False(rootAfter.IsDefault);
        Assert.Equal(FileSystemCaseSensitivityMode.Sensitive, rootAfter.CaseSensitivityMode);
        Assert.Equal(RootFolderRelocationStatus.NeedsAttention, relocationAfter.Status);
        Assert.Equal(rootId, relocationAfter.ActiveRootFolderId);
    }

    [Fact]
    public async Task StartRelocation_RejectsTargetWithCurrentDirectorySegment()
    {
        var source = Path.Join(Path.GetTempPath(), $"relocation-current-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"relocation-current-target-{Guid.NewGuid():N}", ".");
        Directory.CreateDirectory(source);
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source };
            db.RootFolders.Add(root);
            await db.SaveChangesAsync();
            rootId = root.Id;
        }

        var service = CreateService();
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.Relocate,
                true,
                "Library",
                false,
                FileSystemCaseSensitivityMode.Auto)));
        Assert.Contains("current directory", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartRelocation_RejectsTargetWithParentTraversalSegment()
    {
        var source = Path.Join(Path.GetTempPath(), $"relocation-parent-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"relocation-parent-target-{Guid.NewGuid():N}", "Child", "..", "Other");
        Directory.CreateDirectory(source);
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source };
            db.RootFolders.Add(root);
            await db.SaveChangesAsync();
            rootId = root.Id;
        }

        var service = CreateService();
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.Relocate,
                true,
                "Library",
                false,
                FileSystemCaseSensitivityMode.Auto)));
        Assert.Contains("parent", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartRelocation_AllowsOrdinaryValidTargetPath()
    {
        var source = Path.Join(Path.GetTempPath(), $"relocation-valid-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"relocation-valid-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source };
            db.RootFolders.Add(root);
            await db.SaveChangesAsync();
            rootId = root.Id;
        }

        var service = CreateService();
        var result = await service.StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.Relocate,
                true,
                "Library",
                false,
                FileSystemCaseSensitivityMode.Auto));

        Assert.Equal(RootFolderRelocationStatus.Completed, result.Status);
        await using var verification = await _factory.CreateDbContextAsync();
        Assert.Equal(target, (await verification.RootFolders.SingleAsync()).Path);
    }

    [Fact]
    public async Task StartRelocation_RejectsCaseOnlyTargetConflictWithInsensitiveExistingRoot()
    {
        var basePath = Path.Join(Path.GetTempPath(), $"relocation-case-conflict-{Guid.NewGuid():N}");
        var source = Path.Join(Path.GetTempPath(), $"relocation-case-source-{Guid.NewGuid():N}");
        var existing = Path.Join(basePath, "Books");
        var target = Path.Join(basePath, "books");
        Directory.CreateDirectory(source);
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source, CaseSensitivityMode = FileSystemCaseSensitivityMode.Sensitive };
            db.RootFolders.Add(root);
            db.RootFolders.Add(new RootFolder
            {
                Name = "Existing",
                Path = existing,
                CaseSensitivityMode = FileSystemCaseSensitivityMode.Insensitive,
                ResolvedCaseSensitivity = FileSystemCaseSensitivity.Insensitive,
                PathIdentityState = PathIdentityState.Valid
            });
            await db.SaveChangesAsync();
            rootId = root.Id;
        }

        var service = CreateService();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.Relocate,
                true,
                "Library",
                false,
                FileSystemCaseSensitivityMode.Sensitive)));
        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartRelocation_RejectsNestedTargetConflictWithInsensitiveExistingRoot()
    {
        var basePath = Path.Join(Path.GetTempPath(), $"relocation-nested-conflict-{Guid.NewGuid():N}");
        var source = Path.Join(Path.GetTempPath(), $"relocation-nested-source-{Guid.NewGuid():N}");
        var existing = Path.Join(basePath, "Books");
        var target = Path.Join(basePath, "books", "Child");
        Directory.CreateDirectory(source);
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source, CaseSensitivityMode = FileSystemCaseSensitivityMode.Sensitive };
            db.RootFolders.Add(root);
            db.RootFolders.Add(new RootFolder
            {
                Name = "Existing",
                Path = existing,
                CaseSensitivityMode = FileSystemCaseSensitivityMode.Insensitive,
                ResolvedCaseSensitivity = FileSystemCaseSensitivity.Insensitive,
                PathIdentityState = PathIdentityState.Valid
            });
            await db.SaveChangesAsync();
            rootId = root.Id;
        }

        var service = CreateService();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.Relocate,
                true,
                "Library",
                false,
                FileSystemCaseSensitivityMode.Sensitive)));
        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartRelocation_RejectsCaseOnlyTargetConflictWhenTargetIsInsensitive()
    {
        var basePath = Path.Join(Path.GetTempPath(), $"relocation-reverse-case-conflict-{Guid.NewGuid():N}");
        var source = Path.Join(Path.GetTempPath(), $"relocation-reverse-case-source-{Guid.NewGuid():N}");
        var existing = Path.Join(basePath, "Books");
        var target = Path.Join(basePath, "books");
        Directory.CreateDirectory(source);
        int rootId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder { Name = "Library", Path = source, CaseSensitivityMode = FileSystemCaseSensitivityMode.Sensitive };
            db.RootFolders.Add(root);
            db.RootFolders.Add(new RootFolder
            {
                Name = "Existing",
                Path = existing,
                CaseSensitivityMode = FileSystemCaseSensitivityMode.Sensitive,
                ResolvedCaseSensitivity = FileSystemCaseSensitivity.Sensitive,
                PathIdentityState = PathIdentityState.Valid
            });
            await db.SaveChangesAsync();
            rootId = root.Id;
        }

        var service = CreateService();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.Relocate,
                true,
                "Library",
                false,
                FileSystemCaseSensitivityMode.Insensitive)));
        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    [Theory]
    [InlineData("audiobook")]
    [InlineData("source")]
    [InlineData("target")]
    public async Task StartRelocation_RejectsOverlappingActiveStandaloneMove(string conflictKind)
    {
        var source = Path.Join(Path.GetTempPath(), $"active-move-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"active-move-target-{Guid.NewGuid():N}");
        var audiobookPath = Path.Join(source, "Author", "Title");
        Directory.CreateDirectory(audiobookPath);
        int rootId;
        int audiobookId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var root = new RootFolder
            {
                Name = "Library",
                Path = source,
                CaseSensitivityMode = FileSystemCaseSensitivityMode.Insensitive
            };
            var audiobook = new Audiobook { Title = "Title", BasePath = audiobookPath };
            db.RootFolders.Add(root);
            db.Audiobooks.Add(audiobook);
            await db.SaveChangesAsync();
            rootId = root.Id;
            audiobookId = audiobook.Id;

            db.MoveJobs.Add(new MoveJob
            {
                AudiobookId = conflictKind == "audiobook" ? audiobookId : audiobookId + 1000,
                SourcePath = conflictKind == "source"
                    ? Path.Join(source.ToUpperInvariant(), "OTHER")
                    : Path.Join(Path.GetTempPath(), $"unrelated-source-{Guid.NewGuid():N}"),
                RequestedPath = conflictKind == "target"
                    ? Path.Join(target.ToUpperInvariant(), "OTHER")
                    : Path.Join(Path.GetTempPath(), $"unrelated-target-{Guid.NewGuid():N}"),
                Status = MoveJobStatus.Queued,
                EnqueuedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().StartAsync(
            rootId,
            new RootFolderPathChangeCommand(
                target,
                RootFolderRelocationMode.Relocate,
                true,
                "Renamed Library",
                false,
                FileSystemCaseSensitivityMode.Insensitive)));

        Assert.Contains("active move job", exception.Message, StringComparison.OrdinalIgnoreCase);
        await using var verification = await _factory.CreateDbContextAsync();
        Assert.Empty(await verification.RootFolderRelocations.ToListAsync());
        Assert.Empty(await verification.RootFolderRelocationSkippedItems.ToListAsync());
        Assert.Single(await verification.MoveJobs.ToListAsync());
        Assert.Equal(source, (await verification.RootFolders.SingleAsync()).Path);
        Assert.Equal(audiobookPath, (await verification.Audiobooks.SingleAsync()).BasePath);
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

    private sealed class TargetUnavailableSemanticsResolver(string unavailablePath) : IFileSystemSemanticsResolver
    {
        private readonly string _unavailablePath = Path.GetFullPath(unavailablePath);
        private readonly FileSystemSemanticsResolver _inner = new();

        public ValueTask<FileSystemSemanticsResolution> ResolveAsync(
            string path,
            FileSystemCaseSensitivityMode mode = FileSystemCaseSensitivityMode.Auto,
            CancellationToken cancellationToken = default)
        {
            var fullPath = Path.GetFullPath(path);
            if (string.Equals(
                fullPath,
                _unavailablePath,
                FileSystemPathSemantics.CurrentHostDefault.CaseSensitivity == FileSystemCaseSensitivity.Insensitive
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
            {
                return ValueTask.FromResult(new FileSystemSemanticsResolution(
                    new FileSystemPathSemantics(
                        FileSystemPathSemantics.CurrentHostDefault.Syntax,
                        FileSystemCaseSensitivity.Unknown),
                    PathIdentityState.Unavailable,
                    fullPath,
                    "Target filesystem identity became unavailable during finalization."));
            }

            return _inner.ResolveAsync(path, mode, cancellationToken);
        }
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
