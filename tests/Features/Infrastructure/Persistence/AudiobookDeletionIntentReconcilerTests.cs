using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

[Trait("Name", "AudiobookDeletionIntentReconcilerTests")]
[Trait("Category", "Infrastructure")]
public sealed class AudiobookDeletionIntentReconcilerTests : BaseTests
{
    [Fact]
    public async Task ReconcileAsync_PlannedIntent_RetainsFilesystemAndDatabaseUntilExplicitRetry()
    {
        var root = FileService.GetTempDirectory("delete-recovery-planned");
        var file = await FileService.GetFileAsync(root, "book.m4b", "audio");
        var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithTitle("Delete Recovery")
            .WithBasePath(root)
            .WithFilePath(file)
            .Build());
        var store = _provider.GetRequiredService<IAudiobookDeletionIntentStore>();
        var intent = await store.GetOrCreateAsync(audiobook.Id, deleteFolder: true);
        var reconciler = BuildReconciler(store);

        await reconciler.ReconcileAsync();

        Assert.True(File.Exists(file));
        Assert.NotNull(await _audiobookRepository.GetByIdAsync(audiobook.Id));
        var persisted = await GetIntentAsync(intent.Id);
        Assert.Equal(AudiobookDeletionIntentState.Planned, persisted.State);
        Assert.Contains(
            "live delete proof",
            persisted.Error ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReconcileAsync_CleanupAlreadyCommitted_DeletesDatabaseWithoutRepeatingFilesystemMutation()
    {
        var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithTitle("Delete Recovery Commit")
            .WithBasePath(FileService.GetTempDirectory("delete-recovery-commit"))
            .Build());
        var store = _provider.GetRequiredService<IAudiobookDeletionIntentStore>();
        var intent = await store.GetOrCreateAsync(audiobook.Id, deleteFolder: false);
        await store.MarkFilesystemCleanupCompletedAsync(intent.Id);
        var reconciler = BuildReconciler(store);

        await reconciler.ReconcileAsync();

        Assert.Null(await _audiobookRepository.GetByIdAsync(audiobook.Id));
        Assert.Equal(
            AudiobookDeletionIntentState.Completed,
            (await GetIntentAsync(intent.Id)).State);
    }

    [Fact]
    public async Task ReconcileAsync_DatabaseRowAlreadyDeletedAfterCleanup_CompletesIntent()
    {
        var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithTitle("Delete Recovery Post Commit Crash")
            .WithBasePath(FileService.GetTempDirectory("delete-recovery-post-commit"))
            .Build());
        var store = _provider.GetRequiredService<IAudiobookDeletionIntentStore>();
        var intent = await store.GetOrCreateAsync(audiobook.Id, deleteFolder: true);
        await store.MarkFilesystemCleanupCompletedAsync(intent.Id);
        Assert.True(await _audiobookRepository.DeleteByIdAsync(audiobook.Id));
        var reconciler = BuildReconciler(store);

        await reconciler.ReconcileAsync();

        Assert.Equal(
            AudiobookDeletionIntentState.Completed,
            (await GetIntentAsync(intent.Id)).State);
    }

    [Fact]
    public async Task ReconcileAsync_NeedsAttentionIntent_DoesNotBlockIndependentCommittedIntent()
    {
        var blockedBook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithTitle("Scoped Delete Repair")
            .WithBasePath(FileService.GetTempDirectory("delete-recovery-scoped-repair"))
            .Build());
        var committedBook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithTitle("Independent Delete Commit")
            .WithBasePath(FileService.GetTempDirectory("delete-recovery-independent"))
            .Build());
        var store = _provider.GetRequiredService<IAudiobookDeletionIntentStore>();
        var blockedIntent = await store.GetOrCreateAsync(
            blockedBook.Id,
            deleteFolder: true);
        await store.MarkNeedsAttentionAsync(
            blockedIntent.Id,
            "Injected scoped repair state.");
        var committedIntent = await store.GetOrCreateAsync(
            committedBook.Id,
            deleteFolder: false);
        await store.MarkFilesystemCleanupCompletedAsync(committedIntent.Id);
        var reconciler = BuildReconciler(store);

        await reconciler.ReconcileAsync();

        Assert.NotNull(await _audiobookRepository.GetByIdAsync(blockedBook.Id));
        Assert.Equal(
            AudiobookDeletionIntentState.NeedsAttention,
            (await GetIntentAsync(blockedIntent.Id)).State);
        Assert.Null(await _audiobookRepository.GetByIdAsync(committedBook.Id));
        Assert.Equal(
            AudiobookDeletionIntentState.Completed,
            (await GetIntentAsync(committedIntent.Id)).State);
    }

    [Fact]
    public async Task ReconcileAsync_PlannedIntentWithMissingDatabaseRow_BecomesScopedNeedsAttention()
    {
        var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithTitle("Delete Recovery Missing Row")
            .WithBasePath(FileService.GetTempDirectory("delete-recovery-missing-row"))
            .Build());
        var store = _provider.GetRequiredService<IAudiobookDeletionIntentStore>();
        var intent = await store.GetOrCreateAsync(audiobook.Id, deleteFolder: true);
        Assert.True(await _audiobookRepository.DeleteByIdAsync(audiobook.Id));
        var reconciler = BuildReconciler(store);

        await reconciler.ReconcileAsync();

        var persisted = await GetIntentAsync(intent.Id);
        Assert.Equal(AudiobookDeletionIntentState.NeedsAttention, persisted.State);
        Assert.Contains(
            "audiobook row disappeared",
            persisted.Error ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    private AudiobookDeletionIntentReconciler BuildReconciler(
        IAudiobookDeletionIntentStore store) =>
        new(
            store,
            _audiobookRepository,
            _provider.GetRequiredService<IAudiobookDeletionCommitService>(),
            NullLogger<AudiobookDeletionIntentReconciler>.Instance);

    private async Task<AudiobookDeletionIntent> GetIntentAsync(Guid intentId)
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await db.AudiobookDeletionIntents
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == intentId);
    }

    [Fact]
    public async Task ReconcileAsync_OrphanedPlannedIntent_ParksIntentWithoutFailingRecovery()
    {
        var orphan = await CreateOrphanedPlannedIntentAsync("delete-recovery-orphan");
        var filesystem = new Mock<IAudiobookFilesystemDeleteService>(MockBehavior.Strict);
        var reconciler = CreateReconciler(filesystem.Object);

        await reconciler.ReconcileAsync();

        filesystem.VerifyNoOtherCalls();
        var intent = await GetIntentAsync(orphan.IntentId);
        Assert.Equal(AudiobookDeletionIntentState.NeedsAttention, intent.State);
        Assert.False(string.IsNullOrWhiteSpace(intent.Error));
    }

    [Fact]
    public async Task ReconcileAsync_OrphanedIntent_DoesNotStopLaterIntentsFromRecovering()
    {
        var orphan = await CreateOrphanedPlannedIntentAsync("delete-recovery-orphan-first");
        await BackdateIntentAsync(orphan.IntentId, TimeSpan.FromMinutes(5));
        var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithTitle("Delete Recovery After Orphan")
            .WithBasePath(FileService.GetTempDirectory("delete-recovery-after-orphan"))
            .Build());
        var store = _provider.GetRequiredService<IAudiobookDeletionIntentStore>();
        var healthy = await store.GetOrCreateAsync(audiobook.Id, deleteFolder: true);
        var filesystem = new Mock<IAudiobookFilesystemDeleteService>(MockBehavior.Strict);
        filesystem.Setup(service => service.DeleteAsync(
                It.Is<Audiobook>(candidate => candidate.Id == audiobook.Id),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AudiobookFilesystemDeleteResult
            {
                TrackedFileCleanupComplete = true
            });
        var reconciler = CreateReconciler(filesystem.Object);

        await reconciler.ReconcileAsync();

        filesystem.VerifyAll();
        Assert.Equal(
            AudiobookDeletionIntentState.NeedsAttention,
            await GetIntentStateAsync(orphan.IntentId));
        Assert.Equal(
            AudiobookDeletionIntentState.Completed,
            await GetIntentStateAsync(healthy.Id));
        Assert.Null(await _audiobookRepository.GetByIdAsync(audiobook.Id));
    }

    [Fact]
    public async Task ReconcileAsync_SecondPassOverParkedIntent_StillDoesNotFailRecovery()
    {
        var orphan = await CreateOrphanedPlannedIntentAsync("delete-recovery-orphan-restart");
        var filesystem = new Mock<IAudiobookFilesystemDeleteService>(MockBehavior.Strict);
        var reconciler = CreateReconciler(filesystem.Object);

        await reconciler.ReconcileAsync();
        Assert.Equal(
            AudiobookDeletionIntentState.NeedsAttention,
            await GetIntentStateAsync(orphan.IntentId));

        await reconciler.ReconcileAsync();

        filesystem.VerifyNoOtherCalls();
        Assert.Equal(
            AudiobookDeletionIntentState.NeedsAttention,
            await GetIntentStateAsync(orphan.IntentId));
    }

    [Fact]
    public async Task ReconcileAsync_ParkedIntent_StaysDiscoverableAndIsLoggedWithItsIdentity()
    {
        var orphan = await CreateOrphanedPlannedIntentAsync("delete-recovery-orphan-visible");
        var filesystem = new Mock<IAudiobookFilesystemDeleteService>(MockBehavior.Strict);
        var logger = new CapturingLogger<AudiobookDeletionIntentReconciler>();
        var reconciler = CreateReconciler(filesystem.Object, logger);

        await reconciler.ReconcileAsync();

        var store = _provider.GetRequiredService<IAudiobookDeletionIntentStore>();
        var parked = Assert.Single(
            await store.GetActiveAsync(),
            candidate => candidate.Id == orphan.IntentId);
        Assert.Equal(AudiobookDeletionIntentState.NeedsAttention, parked.State);
        Assert.False(string.IsNullOrWhiteSpace(parked.Error));

        var reported = Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Error);
        Assert.Equal(orphan.IntentId, Assert.IsType<Guid>(reported.State["IntentId"]));
        Assert.Equal(orphan.AudiobookId, Assert.IsType<int>(reported.State["AudiobookId"]));
        Assert.Equal(
            "The audiobook row disappeared before its durable filesystem cleanup completed.",
            Assert.IsType<string>(reported.State["Reason"]));
    }

    [Fact]
    public async Task ReconcileAsync_NonTransientFilesystemFailure_StillFailsRecovery()
    {
        var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithTitle("Delete Recovery Non Transient")
            .WithBasePath(FileService.GetTempDirectory("delete-recovery-non-transient"))
            .Build());
        var store = _provider.GetRequiredService<IAudiobookDeletionIntentStore>();
        var intent = await store.GetOrCreateAsync(audiobook.Id, deleteFolder: true);
        var filesystem = new Mock<IAudiobookFilesystemDeleteService>(MockBehavior.Strict);
        filesystem.Setup(service => service.DeleteAsync(
                It.IsAny<Audiobook>(),
                true,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Injected non-transient cleanup failure."));
        var reconciler = CreateReconciler(filesystem.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => reconciler.ReconcileAsync());

        Assert.Contains(
            "could not complete filesystem cleanup safely",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(
            AudiobookDeletionIntentState.Planned,
            await GetIntentStateAsync(intent.Id));
        Assert.NotNull(await _audiobookRepository.GetByIdAsync(audiobook.Id));
    }

    private AudiobookDeletionIntentReconciler CreateReconciler(
        IAudiobookFilesystemDeleteService filesystemDeleteService,
        ILogger<AudiobookDeletionIntentReconciler>? logger = null) =>
        new(
            _provider.GetRequiredService<IAudiobookDeletionIntentStore>(),
            _audiobookRepository,
            _provider.GetRequiredService<IAudiobookDeletionCommitService>(),
            logger ?? NullLogger<AudiobookDeletionIntentReconciler>.Instance);

    private async Task<OrphanedIntent> CreateOrphanedPlannedIntentAsync(string tempDirectoryName)
    {
        var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithTitle("Delete Recovery Orphan")
            .WithBasePath(FileService.GetTempDirectory(tempDirectoryName))
            .Build());
        var store = _provider.GetRequiredService<IAudiobookDeletionIntentStore>();
        var intent = await store.GetOrCreateAsync(audiobook.Id, deleteFolder: true);
        Assert.True(await _audiobookRepository.DeleteByIdAsync(audiobook.Id));
        Assert.Null(await _audiobookRepository.GetByIdAsync(audiobook.Id));
        return new OrphanedIntent(intent.Id, audiobook.Id);
    }

    private async Task BackdateIntentAsync(Guid intentId, TimeSpan offset)
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var intent = await db.AudiobookDeletionIntents
            .SingleAsync(candidate => candidate.Id == intentId);
        intent.CreatedAt -= offset;
        await db.SaveChangesAsync();
    }

    private sealed record OrphanedIntent(Guid IntentId, int AudiobookId);

    private sealed record CapturedLog(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> State);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<CapturedLog> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = state as IEnumerable<KeyValuePair<string, object?>>;
            Entries.Add(new CapturedLog(
                logLevel,
                formatter(state, exception),
                values?.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal)
                    ?? new Dictionary<string, object?>(StringComparer.Ordinal)));
        }
    }

    private async Task<AudiobookDeletionIntentState> GetIntentStateAsync(Guid intentId)
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await db.AudiobookDeletionIntents
            .Where(candidate => candidate.Id == intentId)
            .Select(candidate => candidate.State)
            .SingleAsync();
    }
}
