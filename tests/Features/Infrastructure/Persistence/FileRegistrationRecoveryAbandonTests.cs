using System.Data.Common;
using System.Security.Cryptography;
using Listenarr.Application.Common.Exceptions;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

[Trait("Area", "Library")]
[Trait("Name", "FileRegistrationRecoveryAbandonTests")]
[Trait("Category", "Infrastructure")]
public sealed class FileRegistrationRecoveryAbandonTests : BaseTests
{
    private const string PublishedContent = "audio";

    [Fact]
    public async Task ParkedPublication_BlocksScopedReconcileAndRetryWritesNothing()
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-parked",
            FileMutationJournalState.NeedsAttention);
        var recovery = CreateService(seeded.Factory);
        var before = await LoadJournalAsync(seeded);

        var blocked = await Assert.ThrowsAsync<ApplicationConflictException>(
            () => recovery.ReconcileAudiobookWithReceiptsAsync(seeded.AudiobookId, []));
        var status = await recovery.RetryAsync(seeded.OperationId);
        await recovery.ReconcileAsync();

        Assert.Equal("registration_recovery_repair_required", blocked.Code);
        Assert.Equal(FileMutationJournalState.NeedsAttention, status.JournalState);
        Assert.Equal(FileRegistrationRecoveryDisposition.RequiresOperatorAttention, status.Disposition);
        Assert.False(status.CanRetry);
        var after = await LoadJournalAsync(seeded);
        Assert.Equal(before.State, after.State);
        Assert.Equal(before.Error, after.Error);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
    }

    [Fact]
    public async Task CommittedPublicationWithMissingDestinationParent_ParksNeedsAttention()
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-missing-parent",
            FileMutationJournalState.SourceDeletionAuthorized);
        var recovery = CreateService(seeded.Factory);
        Directory.Delete(Path.GetDirectoryName(seeded.Destination)!, recursive: true);

        var blocked = await Assert.ThrowsAsync<ApplicationConflictException>(
            () => recovery.ReconcileAudiobookWithReceiptsAsync(seeded.AudiobookId, []));

        Assert.Equal("registration_recovery_repair_required", blocked.Code);
        var parked = await LoadJournalAsync(seeded);
        Assert.Equal(FileMutationJournalState.NeedsAttention, parked.State);
        Assert.Contains("no longer matches", parked.Error);
        Assert.Equal(PublishedContent, await File.ReadAllTextAsync(seeded.Source));
    }

    [Fact]
    public async Task CommittedPublicationWithDestinationPresent_ControlClearsWithoutParking()
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-present-parent",
            FileMutationJournalState.SourceDeletionAuthorized);

        await CreateService(seeded.Factory)
            .ReconcileAudiobookWithReceiptsAsync(seeded.AudiobookId, []);

        Assert.Equal(
            FileMutationJournalState.CompletedSourceRetained,
            (await LoadJournalAsync(seeded)).State);
    }

    [Fact]
    public async Task AbandonAsync_ParkedPublication_ClearsBoundaryAsCompletedSourceRetained()
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-clears",
            FileMutationJournalState.NeedsAttention);
        var recovery = CreateService(seeded.Factory);
        var probe = new FileRegistrationRecoveryProbe(seeded.Factory);
        var parkedStatus = await recovery.RetryAsync(seeded.OperationId);
        Assert.True(parkedStatus.CanAbandon);
        Assert.True(await probe.HasBlockingAsync(seeded.AudiobookId));

        var status = await recovery.AbandonAsync(seeded.OperationId);

        Assert.Equal(FileMutationJournalState.CompletedSourceRetained, status.JournalState);
        Assert.Equal(FileRegistrationRecoveryDisposition.Cleared, status.Disposition);
        Assert.False(status.CanAbandon);
        Assert.False(status.CanRetry);
        Assert.Contains("abandoned", status.PublicReason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("retained", status.PublicReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(nameof(FileMutationJournalState.CompletedSourceRetained), await ReadRawStateAsync(seeded));
        var abandoned = await LoadJournalAsync(seeded);
        Assert.StartsWith("Abandoned by operator request", abandoned.Error);
        Assert.DoesNotContain("retained", abandoned.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no longer matches the published content proof", abandoned.Error);
        Assert.Equal(seeded.AudiobookId, abandoned.AudiobookId);
        Assert.Null(abandoned.AudiobookFileId);
        Assert.False(await probe.HasBlockingAsync(seeded.AudiobookId));
        Assert.Empty(await recovery.ReconcileAudiobookWithReceiptsAsync(seeded.AudiobookId, []));
    }

    [Fact]
    public async Task AbandonAsync_ParkedPublication_ChangesNoFile()
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-files",
            FileMutationJournalState.NeedsAttention);
        var before = await HashTreeAsync(seeded.Root);

        await CreateService(seeded.Factory).AbandonAsync(seeded.OperationId);

        Assert.Equal(before, await HashTreeAsync(seeded.Root));
        await File.AppendAllTextAsync(seeded.Source, "x");
        Assert.NotEqual(before, await HashTreeAsync(seeded.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonAsync_ParkedPublication_GrantsNoSourceRetirementAuthority(
        bool completedControl)
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-authority",
            FileMutationJournalState.NeedsAttention);
        var recovery = CreateService(seeded.Factory);
        if (completedControl)
        {
            await SetStateAsync(seeded, FileMutationJournalState.Completed);
        }
        else
        {
            await recovery.AbandonAsync(seeded.OperationId);
        }

        var receipt = Assert.Single(
            await recovery.ReconcileAudiobookWithReceiptsAsync(seeded.AudiobookId, [seeded.Source]));

        var state = (await LoadJournalAsync(seeded)).State;
        Assert.Equal(completedControl, FileMutationJournalLifecycle.MayRetireSource(state));
        Assert.Equal(!completedControl, receipt.SourceRetained);
        Assert.Equal(completedControl ? null : PublishedContent.Length, receipt.SourceLength);
        Assert.Equal(PublishedContent, await File.ReadAllTextAsync(seeded.Source));
    }

    [Fact]
    public async Task AbandonAsync_ParkedPublication_ChangesNoOtherRow()
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-rows",
            FileMutationJournalState.NeedsAttention);
        var downloadId = await SeedDownloadActivityAsync(seeded);
        var before = await SnapshotTablesExceptJournalsAsync(seeded.Factory);
        Assert.Contains(downloadId, before["Downloads"]);
        Assert.Contains(downloadId, before["DownloadProcessingJobs"]);
        Assert.Contains(downloadId, before["History"]);

        await CreateService(seeded.Factory).AbandonAsync(seeded.OperationId);

        Assert.Equal(before, await SnapshotTablesExceptJournalsAsync(seeded.Factory));
        await using (var db = await seeded.Factory.CreateDbContextAsync())
        {
            await db.Downloads.ExecuteUpdateAsync(
                setters => setters.SetProperty(download => download.Title, "changed"));
        }
        Assert.NotEqual(before, await SnapshotTablesExceptJournalsAsync(seeded.Factory));
    }

    [Fact]
    public async Task AbandonAsync_ParkedPublication_ChangesNoOtherJournal()
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-bystanders",
            FileMutationJournalState.NeedsAttention);
        var parkedBystander = await SeedBystanderJournalAsync(
            seeded,
            FileMutationJournalState.NeedsAttention);
        var completedBystander = await SeedBystanderJournalAsync(
            seeded,
            FileMutationJournalState.Completed);
        var before = await SnapshotJournalsExceptAsync(seeded);
        Assert.Contains(parkedBystander.ToString().ToUpperInvariant(), before);
        Assert.Contains(completedBystander.ToString().ToUpperInvariant(), before);
        var abandonedBefore = await LoadJournalAsync(seeded);

        await CreateService(seeded.Factory).AbandonAsync(seeded.OperationId);

        Assert.Equal(before, await SnapshotJournalsExceptAsync(seeded));
        Assert.True((await LoadJournalAsync(seeded)).UpdatedAt > abandonedBefore.UpdatedAt);
    }

    [Fact]
    public async Task AbandonAsync_Repeated_IsIdempotentAndKeepsFirstReason()
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-repeat",
            FileMutationJournalState.NeedsAttention);
        var logger = new CapturingLogger();
        var recovery = CreateService(seeded.Factory, logger);
        await recovery.AbandonAsync(seeded.OperationId);
        var first = await LoadJournalAsync(seeded);

        var repeated = await recovery.AbandonAsync(seeded.OperationId);

        Assert.Equal(FileRegistrationRecoveryDisposition.Cleared, repeated.Disposition);
        var second = await LoadJournalAsync(seeded);
        Assert.Equal(first.Error, second.Error);
        Assert.Equal(first.UpdatedAt, second.UpdatedAt);
        Assert.Single(logger.Warnings);
    }

    [Theory]
    [InlineData(FileMutationJournalState.RegistrationCommitted)]
    [InlineData(FileMutationJournalState.SourceDeletionAuthorized)]
    [InlineData(FileMutationJournalState.Completed)]
    [InlineData(FileMutationJournalState.RolledBack)]
    public async Task AbandonAsync_PublicationNotParked_WritesNothing(
        FileMutationJournalState state)
    {
        var seeded = await SeedOwnedPublicationAsync("registration-abandon-not-parked", state);
        var logger = new CapturingLogger();
        var before = await LoadJournalAsync(seeded);

        var status = await CreateService(seeded.Factory, logger).AbandonAsync(seeded.OperationId);

        Assert.Equal(state, status.JournalState);
        Assert.False(status.CanAbandon);
        Assert.Equal(
            FileMutationJournalLifecycle.ClearsRegistrationRecoveryBoundary(state),
            status.Disposition == FileRegistrationRecoveryDisposition.Cleared);
        var after = await LoadJournalAsync(seeded);
        Assert.Equal(before.State, after.State);
        Assert.Equal(before.Error, after.Error);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Empty(logger.Warnings);
    }

    [Theory]
    [InlineData(FileAction.Move, 5)]
    [InlineData(FileAction.Move, -2)]
    [InlineData(FileAction.None, null)]
    public async Task AbandonAsync_ParkedJournalThatIsNotARegistrationPublication_IsRefused(
        FileAction action,
        int? audiobookFileId)
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-foreign",
            FileMutationJournalState.NeedsAttention,
            journal =>
            {
                journal.Action = action;
                journal.AudiobookFileId = audiobookFileId;
            });
        var before = await LoadJournalAsync(seeded);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateService(seeded.Factory).AbandonAsync(seeded.OperationId));

        var after = await LoadJournalAsync(seeded);
        Assert.Equal(FileMutationJournalState.NeedsAttention, after.State);
        Assert.Equal(before.Error, after.Error);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
    }

    [Theory]
    [InlineData(FileAction.Copy)]
    [InlineData(FileAction.HardlinkCopy)]
    public async Task AbandonAsync_ParkedCopyPublication_ClearsBoundary(FileAction action)
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-copy",
            FileMutationJournalState.NeedsAttention,
            journal => journal.Action = action);

        var status = await CreateService(seeded.Factory).AbandonAsync(seeded.OperationId);

        Assert.Equal(FileMutationJournalState.CompletedSourceRetained, status.JournalState);
        Assert.Equal(action, (await LoadJournalAsync(seeded)).Action);
    }

    [Fact]
    public async Task AbandonAsync_UnknownOrEmptyOperation_IsRefusedWithoutWriting()
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-unknown",
            FileMutationJournalState.NeedsAttention);
        var recovery = CreateService(seeded.Factory);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => recovery.AbandonAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => recovery.AbandonAsync(Guid.Empty));

        Assert.Equal(FileMutationJournalState.NeedsAttention, (await LoadJournalAsync(seeded)).State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public async Task AbandonAsync_ParkedUnsupportedProtocolPublication_ClearsBoundary(
        int protocolVersion)
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-protocol",
            FileMutationJournalState.NeedsAttention);
        await using (var db = await seeded.Factory.CreateDbContextAsync())
        {
            await db.FileMutationJournals.ExecuteUpdateAsync(
                setters => setters.SetProperty(journal => journal.ProtocolVersion, protocolVersion));
        }

        var status = await CreateService(seeded.Factory).AbandonAsync(seeded.OperationId);

        Assert.Equal(FileMutationJournalState.CompletedSourceRetained, status.JournalState);
        var abandoned = await LoadJournalAsync(seeded);
        Assert.Equal(protocolVersion, abandoned.ProtocolVersion);
        Assert.Equal(FileMutationJournalState.CompletedSourceRetained, abandoned.State);
    }

    [Fact]
    public async Task AbandonAsync_ParkedAnonymousPublication_ClearsBoundary()
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-anonymous",
            FileMutationJournalState.NeedsAttention,
            journal => journal.AudiobookId = null);
        var probe = new FileRegistrationRecoveryProbe(seeded.Factory);
        var semantics = FileSystemPathSemantics.CurrentHostDefault;
        Assert.True(await probe.HasBlockingBoundaryAsync(seeded.Destination, semantics));

        var status = await CreateService(seeded.Factory).AbandonAsync(seeded.OperationId);

        Assert.Equal(FileMutationJournalState.CompletedSourceRetained, status.JournalState);
        Assert.Null(status.AudiobookId);
        Assert.Null((await LoadJournalAsync(seeded)).AudiobookId);
        Assert.False(await probe.HasBlockingBoundaryAsync(seeded.Destination, semantics));
    }

    [Fact]
    public async Task AbandonAsync_StateChangedBeforeWrite_DoesNotOverwriteConcurrentState()
    {
        var root = FileService.GetTempDirectory("registration-abandon-race-db");
        var databasePath = Path.Join(root, "race.db");
        var plain = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-race",
            FileMutationJournalState.NeedsAttention,
            databaseOptions: plain);
        var interceptor = new ChangeStateBeforeJournalUpdateInterceptor(
            plain,
            seeded.OperationId,
            FileMutationJournalState.RegistrationCommitted);
        var racing = new TestDbContextFactory(
            new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .AddInterceptors(interceptor)
                .Options);
        var logger = new CapturingLogger();

        var status = await CreateService(racing, logger).AbandonAsync(seeded.OperationId);

        Assert.True(interceptor.Fired);
        Assert.Equal(FileMutationJournalState.RegistrationCommitted, status.JournalState);
        var persisted = await LoadJournalAsync(seeded);
        Assert.Equal(FileMutationJournalState.RegistrationCommitted, persisted.State);
        Assert.DoesNotContain("Abandoned", persisted.Error ?? string.Empty);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public async Task AbandonAsync_ParkedPublication_WarnsWithPriorStateAndReason()
    {
        var seeded = await SeedOwnedPublicationAsync(
            "registration-abandon-log",
            FileMutationJournalState.NeedsAttention);
        var logger = new CapturingLogger();

        await CreateService(seeded.Factory, logger).AbandonAsync(seeded.OperationId);

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains(seeded.OperationId.ToString(), warning);
        Assert.Contains(seeded.AudiobookId.ToString(System.Globalization.CultureInfo.InvariantCulture), warning);
        Assert.Contains(nameof(FileMutationJournalState.NeedsAttention), warning);
        Assert.Contains("no longer matches the published content proof", warning);
        Assert.DoesNotContain("retained", warning, StringComparison.OrdinalIgnoreCase);
    }

    private static FileRegistrationRecoveryService CreateService(
        TestDbContextFactory factory,
        ILogger<FileRegistrationRecoveryService>? logger = null) =>
        new(
            factory,
            new FileMover(NullLogger<FileMover>.Instance, dbContextFactory: factory,
                timeProvider: TimeProvider.System),
            TimeProvider.System,
            logger ?? NullLogger<FileRegistrationRecoveryService>.Instance);

    private async Task<SeededPublication> SeedOwnedPublicationAsync(
        string name,
        FileMutationJournalState state,
        Action<FileMutationJournal>? configure = null,
        DbContextOptions<ListenArrDbContext>? databaseOptions = null)
    {
        var root = FileService.GetTempDirectory(name);
        var sourceDirectory = Path.Join(root, "source");
        var destinationDirectory = Path.Join(root, "destination");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(destinationDirectory);
        var source = Path.Join(sourceDirectory, "book.m4b");
        var destination = Path.Join(destinationDirectory, "book.m4b");
        await File.WriteAllTextAsync(source, PublishedContent);
        await File.WriteAllTextAsync(destination, PublishedContent);

        var audiobook = new AudiobookBuilder()
            .WithTitle("A Princess of Mars")
            .WithBasePath(destinationDirectory)
            .Build();
        var identity = await _provider
            .GetRequiredService<IAudiobookFilePathIdentityResolver>()
            .ResolveAsync(audiobook, destination);
        Assert.Equal(PathIdentityState.Valid, identity.State);
        var file = AudiobookFile.CreateUnresolved(destination);
        file.ApplyPathIdentity(destination, identity);
        audiobook.Files = [file];

        var databaseDirectory = FileService.GetTempDirectory(name + "-db");
        var factory = new TestDbContextFactory(
            databaseOptions ?? new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseSqlite($"Data Source={Path.Join(databaseDirectory, "recovery.db")};Pooling=False")
                .Options);
        var operationId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        db.Audiobooks.Add(audiobook);
        await db.SaveChangesAsync();
        var journal = new FileMutationJournal
        {
            OperationId = operationId,
            ProtocolVersion = FileMutationProtocol.Current,
            Action = FileAction.Move,
            SourcePath = source,
            DestinationPath = destination,
            SourcePhysicalObjectIdentity = string.Empty,
            SourceLength = PublishedContent.Length,
            SourceSha256 = Convert.ToHexString(
                SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(PublishedContent))),
            AudiobookId = audiobook.Id,
            State = state,
            Error = state == FileMutationJournalState.NeedsAttention
                ? "The committed file-registration destination no longer matches the published content proof."
                : null,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        configure?.Invoke(journal);
        db.FileMutationJournals.Add(journal);
        await db.SaveChangesAsync();
        return new SeededPublication(
            factory, root, operationId, audiobook.Id, source, destination);
    }

    private static async Task<string> SeedDownloadActivityAsync(SeededPublication seeded)
    {
        var downloadId = Guid.NewGuid().ToString();
        await using var db = await seeded.Factory.CreateDbContextAsync();
        db.Downloads.Add(new Download
        {
            Id = downloadId,
            AudiobookId = seeded.AudiobookId,
            Title = "A Princess of Mars",
            DownloadPath = Path.GetDirectoryName(seeded.Source)!,
            FinalPath = Path.GetDirectoryName(seeded.Source)!
        });
        db.DownloadProcessingJobs.Add(new DownloadProcessingJob
        {
            Id = Guid.NewGuid().ToString(),
            DownloadId = downloadId,
            SourcePath = seeded.Source
        });
        db.History.Add(new History
        {
            AudiobookId = seeded.AudiobookId,
            AudiobookTitle = "A Princess of Mars",
            EventType = "Imported",
            DownloadId = downloadId
        });
        await db.SaveChangesAsync();
        return downloadId;
    }

    private static async Task<Guid> SeedBystanderJournalAsync(
        SeededPublication seeded,
        FileMutationJournalState state)
    {
        var operationId = Guid.NewGuid();
        await using var db = await seeded.Factory.CreateDbContextAsync();
        db.FileMutationJournals.Add(new FileMutationJournal
        {
            OperationId = operationId,
            ProtocolVersion = FileMutationProtocol.Current,
            Action = FileAction.Move,
            SourcePath = Path.Join(seeded.Root, "source", $"{operationId:N}.m4b"),
            DestinationPath = Path.Join(seeded.Root, "destination", $"{operationId:N}.m4b"),
            SourcePhysicalObjectIdentity = string.Empty,
            AudiobookId = seeded.AudiobookId,
            State = state,
            Error = state == FileMutationJournalState.NeedsAttention
                ? "The committed file-registration destination no longer matches the published content proof."
                : null,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();
        return operationId;
    }

    private static async Task<string> SnapshotJournalsExceptAsync(SeededPublication seeded)
    {
        await using var db = await seeded.Factory.CreateDbContextAsync();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var select = connection.CreateCommand();
        select.CommandText =
            "SELECT * FROM FileMutationJournals WHERE OperationId <> $operationId ORDER BY OperationId";
        var parameter = select.CreateParameter();
        parameter.ParameterName = "$operationId";
        parameter.Value = seeded.OperationId.ToString().ToUpperInvariant();
        select.Parameters.Add(parameter);
        await using var reader = await select.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Add(string.Join("|", values.Select(value => Convert.ToString(
                value, System.Globalization.CultureInfo.InvariantCulture))));
        }

        return string.Join("\n", rows);
    }

    private static async Task<IReadOnlyDictionary<string, string>> SnapshotTablesExceptJournalsAsync(
        TestDbContextFactory factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name <> 'FileMutationJournals' ORDER BY name";
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            await using var select = connection.CreateCommand();
            select.CommandText = $"SELECT * FROM \"{table}\"";
            await using var reader = await select.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(string.Join("|", values.Select(value => Convert.ToString(
                    value, System.Globalization.CultureInfo.InvariantCulture))));
            }
            rows.Sort(StringComparer.Ordinal);
            snapshot[table] = string.Join("\n", rows);
        }

        return snapshot;
    }

    private static async Task<string> HashTreeAsync(string root)
    {
        var entries = new List<string>();
        foreach (var path in Directory
            .EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path);
            entries.Add(Directory.Exists(path)
                ? $"{relative}/"
                : $"{relative}:{new FileInfo(path).Length}:"
                    + Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))));
        }

        return string.Join("\n", entries);
    }

    private static async Task SetStateAsync(
        SeededPublication seeded,
        FileMutationJournalState state)
    {
        await using var db = await seeded.Factory.CreateDbContextAsync();
        await db.FileMutationJournals.ExecuteUpdateAsync(
            setters => setters.SetProperty(journal => journal.State, state));
    }

    private static async Task<string> ReadRawStateAsync(SeededPublication seeded)
    {
        await using var db = await seeded.Factory.CreateDbContextAsync();
        return await db.Database
            .SqlQuery<string>(
                $"SELECT State AS Value FROM FileMutationJournals WHERE OperationId = {seeded.OperationId.ToString().ToUpperInvariant()}")
            .SingleAsync();
    }

    private static async Task<FileMutationJournal> LoadJournalAsync(SeededPublication seeded)
    {
        await using var db = await seeded.Factory.CreateDbContextAsync();
        return await db.FileMutationJournals
            .AsNoTracking()
            .SingleAsync(journal => journal.OperationId == seeded.OperationId);
    }

    private sealed record SeededPublication(
        TestDbContextFactory Factory,
        string Root,
        Guid OperationId,
        int AudiobookId,
        string Source,
        string Destination);

    private sealed class ChangeStateBeforeJournalUpdateInterceptor(
        DbContextOptions<ListenArrDbContext> plain,
        Guid operationId,
        FileMutationJournalState concurrentState) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired
                && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("FileMutationJournals"))
            {
                Fired = true;
                await using var other = new ListenArrDbContext(plain);
                await other.FileMutationJournals
                    .Where(journal => journal.OperationId == operationId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(journal => journal.State, concurrentState),
                        cancellationToken);
            }
            return result;
        }
    }

    private sealed class CapturingLogger : ILogger<FileRegistrationRecoveryService>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<string> Warnings =>
            _entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToList();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class TestDbContextFactory(
        DbContextOptions<ListenArrDbContext> options)
        : IDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext() => new(options);

        public Task<ListenArrDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ListenArrDbContext(options));
    }
}
