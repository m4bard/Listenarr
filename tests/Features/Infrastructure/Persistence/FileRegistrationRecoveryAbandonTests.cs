using System.Security.Cryptography;
using Listenarr.Application.Common.Exceptions;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
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

    private static FileRegistrationRecoveryService CreateService(
        TestDbContextFactory factory) =>
        new(
            factory,
            new FileMover(NullLogger<FileMover>.Instance, dbContextFactory: factory,
                timeProvider: TimeProvider.System),
            TimeProvider.System,
            NullLogger<FileRegistrationRecoveryService>.Instance);

    private async Task<SeededPublication> SeedOwnedPublicationAsync(
        string name,
        FileMutationJournalState state)
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

        var factory = new TestDbContextFactory(
            new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseSqlite($"Data Source={Path.Join(root, "recovery.db")};Pooling=False")
                .Options);
        var operationId = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        db.Audiobooks.Add(audiobook);
        await db.SaveChangesAsync();
        db.FileMutationJournals.Add(new FileMutationJournal
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
                : null
        });
        await db.SaveChangesAsync();
        return new SeededPublication(factory, operationId, audiobook.Id, source, destination);
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
        Guid OperationId,
        int AudiobookId,
        string Source,
        string Destination);

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
