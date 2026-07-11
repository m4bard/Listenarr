using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public partial class AudiobookContentMoveServiceTests
{
    [Theory]
    [InlineData((int)OwnershipMarkerWriteFaultPoint.BeforeTemporaryFileCreation)]
    [InlineData((int)OwnershipMarkerWriteFaultPoint.DuringJsonWrite)]
    [InlineData((int)OwnershipMarkerWriteFaultPoint.DuringFlush)]
    [InlineData((int)OwnershipMarkerWriteFaultPoint.AfterTemporaryFileWritten)]
    [InlineData((int)OwnershipMarkerWriteFaultPoint.BeforePublication)]
    public async Task MoveContentsAsync_TempOwnershipPublicationFailure_RetriesCleanly(
        int faultPointValue)
    {
        var faultPoint = (OwnershipMarkerWriteFaultPoint)faultPointValue;
        var source = FileService.GetTempDirectory("content-move-temp-marker-fault-src");
        await FileService.GetFileAsync(source, "book.m4b", "verified audio");
        var target = Path.Join(
            FileService.GetTempPath(),
            $"content-move-temp-marker-fault-dst-{Guid.NewGuid():N}");
        var request = await CreateLeasedMoveRequestAsync(source, target);
        var faultingService = new AudiobookContentMoveService(
            _provider.GetRequiredService<ILogger<AudiobookContentMoveService>>(),
            _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>(),
            TimeProvider.System,
            new SingleOwnershipPublicationFaultInjector(
                OwnershipMarkerKind.TemporaryDirectory,
                faultPoint));

        await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            faultingService.MoveContentsAsync(request, CancellationToken.None));

        Assert.True(Directory.Exists(source));
        Assert.False(Directory.Exists(target));
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var result = await service.MoveContentsAsync(request, CancellationToken.None);

        Assert.True(result.SourceCleanupCompleted);
        Assert.False(Directory.Exists(source));
        Assert.True(File.Exists(Path.Join(target, "book.m4b")));
    }

    [Fact]
    public async Task MoveContentsAsync_InterruptedTempOwnershipPublication_RetriesWithoutManualCleanup()
    {
        var source = FileService.GetTempDirectory("content-move-temp-marker-recovery-src");
        await FileService.GetFileAsync(source, "book.m4b", "verified audio");
        var target = Path.Join(
            FileService.GetTempPath(),
            $"content-move-temp-marker-recovery-dst-{Guid.NewGuid():N}");
        var request = await CreateLeasedMoveRequestAsync(source, target);
        var faultingService = CreateOwnershipFaultingService(
            OwnershipMarkerKind.TemporaryDirectory);

        await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            faultingService.MoveContentsAsync(request, CancellationToken.None));

        Assert.True(Directory.Exists(source));
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var result = await service.MoveContentsAsync(request, CancellationToken.None);

        Assert.True(File.Exists(Path.Join(target, "book.m4b")));
        Assert.False(Directory.Exists(source));
        Assert.True(result.SourceCleanupCompleted);
    }

    [Fact]
    public async Task ResumeSourceCleanup_InterruptedQuarantineOwnershipPublication_RecoversOrphanWriteMarker()
    {
        var source = FileService.GetTempDirectory("content-move-quarantine-marker-recovery-src");
        await FileService.GetFileAsync(source, "book.m4b", "verified audio");
        var target = Path.Join(
            FileService.GetTempPath(),
            $"content-move-quarantine-marker-recovery-dst-{Guid.NewGuid():N}");
        var request = await CreateLeasedMoveRequestAsync(source, target);
        var faultingService = CreateOwnershipFaultingService(
            OwnershipMarkerKind.QuarantineDirectory);

        await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            faultingService.MoveContentsAsync(request, CancellationToken.None));

        var quarantineRoot = Path.Join(
            Path.GetDirectoryName(source)!,
            $".listenarr-quarantine-{request.JobId:N}");
        Assert.True(Directory.Exists(quarantineRoot));
        Assert.Single(Directory.EnumerateFiles(
            quarantineRoot,
            ".listenarr-quarantine-owner.json.writing-*"));

        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var recovered = await service.GetRecoverableMoveAsync(
            request,
            CancellationToken.None);
        Assert.NotNull(recovered);

        var completed = await service.ResumeSourceCleanupAsync(
            request,
            recovered!,
            CancellationToken.None);

        Assert.True(completed.SourceCleanupCompleted);
        Assert.False(Directory.Exists(source));
        Assert.False(Directory.Exists(quarantineRoot));
        Assert.True(File.Exists(Path.Join(target, "book.m4b")));
    }

    [Theory]
    [InlineData((int)OwnershipCleanupFaultPoint.BeforeOwnershipMarkerDelete)]
    [InlineData((int)OwnershipCleanupFaultPoint.BeforeDirectoryDelete)]
    [InlineData((int)OwnershipCleanupFaultPoint.BeforeTombstoneDelete)]
    public async Task ResumeSourceCleanup_InterruptedQuarantineCleanup_UsesCleanupTombstone(
        int faultPointValue)
    {
        var faultPoint = (OwnershipCleanupFaultPoint)faultPointValue;
        var source = FileService.GetTempDirectory("content-move-quarantine-tombstone-src");
        await FileService.GetFileAsync(source, "book.m4b", "verified audio");
        var target = Path.Join(
            FileService.GetTempPath(),
            $"content-move-quarantine-tombstone-dst-{Guid.NewGuid():N}");
        var request = await CreateLeasedMoveRequestAsync(source, target);
        var faultingService = new AudiobookContentMoveService(
            _provider.GetRequiredService<ILogger<AudiobookContentMoveService>>(),
            _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>(),
            TimeProvider.System,
            new OwnershipCleanupFaultInjector(faultPoint));

        await Assert.ThrowsAsync<IOException>(() =>
            faultingService.MoveContentsAsync(request, CancellationToken.None));

        var sourceParent = Path.GetDirectoryName(source)!;
        var quarantineRoot = Path.Join(
            sourceParent,
            $".listenarr-quarantine-{request.JobId:N}");
        var tombstonePath = Path.Join(
            sourceParent,
            $".listenarr-quarantine-directory-{request.JobId:N}.cleanup.json");
        Assert.Equal(
            faultPoint != OwnershipCleanupFaultPoint.BeforeTombstoneDelete,
            Directory.Exists(quarantineRoot));
        Assert.True(File.Exists(tombstonePath));
        Assert.False(Directory.Exists(source));
        Assert.True(File.Exists(Path.Join(target, "book.m4b")));

        var service = _provider.GetRequiredService<AudiobookContentMoveService>();
        var recovered = await service.GetRecoverableMoveAsync(
            request,
            CancellationToken.None);
        Assert.NotNull(recovered);

        var completed = await service.ResumeSourceCleanupAsync(
            request,
            recovered!,
            CancellationToken.None);

        Assert.True(completed.SourceCleanupCompleted);
        Assert.False(Directory.Exists(quarantineRoot));
        Assert.False(File.Exists(tombstonePath));
        Assert.False(Directory.Exists(source));
        Assert.True(File.Exists(Path.Join(target, "book.m4b")));
    }

    [Fact]
    public async Task GetRecoverableMoveAsync_TombstonedDirectoryRecreatedWithContent_IsPreserved()
    {
        var source = FileService.GetTempDirectory("content-move-tombstone-recreated-src");
        await FileService.GetFileAsync(source, "book.m4b", "verified audio");
        var target = Path.Join(
            FileService.GetTempPath(),
            $"content-move-tombstone-recreated-dst-{Guid.NewGuid():N}");
        var request = await CreateLeasedMoveRequestAsync(source, target);
        var faultingService = new AudiobookContentMoveService(
            _provider.GetRequiredService<ILogger<AudiobookContentMoveService>>(),
            _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>(),
            TimeProvider.System,
            new OwnershipCleanupFaultInjector(
                OwnershipCleanupFaultPoint.BeforeDirectoryDelete));

        await Assert.ThrowsAsync<IOException>(() =>
            faultingService.MoveContentsAsync(request, CancellationToken.None));

        var sourceParent = Path.GetDirectoryName(source)!;
        var quarantineRoot = Path.Join(
            sourceParent,
            $".listenarr-quarantine-{request.JobId:N}");
        var tombstonePath = Path.Join(
            sourceParent,
            $".listenarr-quarantine-directory-{request.JobId:N}.cleanup.json");
        var unexpectedFile = await FileService.GetFileAsync(
            quarantineRoot,
            "operator-note.txt",
            "preserve me");
        var service = _provider.GetRequiredService<AudiobookContentMoveService>();

        var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
            service.GetRecoverableMoveAsync(request, CancellationToken.None));

        Assert.Contains("unexpected content", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("preserve me", await File.ReadAllTextAsync(unexpectedFile));
        Assert.True(File.Exists(tombstonePath));
        Assert.True(Directory.Exists(quarantineRoot));
        Assert.True(File.Exists(Path.Join(target, "book.m4b")));
    }

    private AudiobookContentMoveService CreateOwnershipFaultingService(
        OwnershipMarkerKind markerKind) =>
        new(
            _provider.GetRequiredService<ILogger<AudiobookContentMoveService>>(),
            _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>(),
            TimeProvider.System,
            new OwnershipPublicationFaultInjector(markerKind));

    private sealed class OwnershipCleanupFaultInjector(
        OwnershipCleanupFaultPoint expectedFaultPoint) : IMoveFaultInjector
    {
        private bool _failed;

        public void OnOwnershipCleanup(
            Guid jobId,
            OwnershipMarkerKind markerKind,
            OwnershipCleanupFaultPoint faultPoint)
        {
            if (_failed
                || markerKind != OwnershipMarkerKind.QuarantineDirectory
                || faultPoint != expectedFaultPoint)
            {
                return;
            }

            _failed = true;
            throw new IOException("Simulated process stop before owned directory deletion.");
        }
    }

    private sealed class SingleOwnershipPublicationFaultInjector(
        OwnershipMarkerKind expectedMarkerKind,
        OwnershipMarkerWriteFaultPoint expectedFaultPoint) : IMoveFaultInjector
    {
        private bool _failed;

        public void OnOwnershipMarkerWrite(
            Guid jobId,
            OwnershipMarkerKind markerKind,
            OwnershipMarkerWriteFaultPoint faultPoint)
        {
            if (_failed
                || markerKind != expectedMarkerKind
                || faultPoint != expectedFaultPoint)
            {
                return;
            }

            _failed = true;
            throw new IOException($"Simulated ownership publication failure at {faultPoint}.");
        }
    }

    private sealed class OwnershipPublicationFaultInjector(
        OwnershipMarkerKind markerKind) : IMoveFaultInjector
    {
        public void OnOwnershipMarkerWrite(
            Guid jobId,
            OwnershipMarkerKind currentMarkerKind,
            OwnershipMarkerWriteFaultPoint faultPoint)
        {
            if (currentMarkerKind == markerKind
                && faultPoint is OwnershipMarkerWriteFaultPoint.BeforePublication
                    or OwnershipMarkerWriteFaultPoint.BeforeTemporaryFileDeletion)
            {
                throw new IOException($"Simulated ownership publication failure at {faultPoint}.");
            }
        }
    }
}
