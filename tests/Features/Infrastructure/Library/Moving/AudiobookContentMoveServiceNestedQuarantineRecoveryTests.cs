using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public partial class AudiobookContentMoveServiceTests
{
    [Fact]
    public async Task ResumeSourceCleanup_SourceInsideTargetWithOwnedQuarantine_RecoversAfterCrash()
    {
        var target = FileService.GetTempDirectory("content-move-nested-quarantine-target");
        var source = Path.Join(target, "OldChild");
        Directory.CreateDirectory(source);
        await FileService.GetFileAsync(source, "book.m4b", "verified audio");
        var jobId = Guid.NewGuid();
        var request = await CreateLeasedMoveRequestAsync(source, target, jobId);
        var faultingService = new AudiobookContentMoveService(
            _provider.GetRequiredService<ILogger<AudiobookContentMoveService>>(),
            _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>(),
            TimeProvider.System,
            new StopBeforeFirstQuarantineDelete());

        await Assert.ThrowsAsync<IOException>(() =>
            faultingService.MoveContentsAsync(request, CancellationToken.None));

        var quarantineRoot = Path.Join(
            target,
            $".listenarr-quarantine-{jobId:N}");
        Assert.True(Directory.Exists(quarantineRoot));
        Assert.True(File.Exists(Path.Join(quarantineRoot, "book.m4b")));
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
        Assert.False(Directory.Exists(source));
        Assert.False(Directory.Exists(quarantineRoot));
        Assert.True(File.Exists(Path.Join(target, "book.m4b")));
    }

    private sealed class StopBeforeFirstQuarantineDelete : IMoveFaultInjector
    {
        private bool _stopped;

        public void OnSourceCleanupMutation(
            Guid jobId,
            SourceCleanupFaultPoint faultPoint)
        {
            if (_stopped || faultPoint != SourceCleanupFaultPoint.BeforeQuarantineFileDelete)
            {
                return;
            }

            _stopped = true;
            throw new IOException("Simulated process stop before quarantine deletion.");
        }
    }
}
