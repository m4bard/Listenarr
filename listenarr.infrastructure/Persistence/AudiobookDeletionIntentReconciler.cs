using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Persistence;

/// <summary>
/// Replays durable audiobook deletion intents during startup recovery.
/// <para>
/// Any intent in NeedsAttention is reported and stepped over rather than thrown
/// on. It is a row waiting for a human, which says nothing about whether the
/// library filesystem is usable, so it must not fail the recovery phase and
/// disable filesystem mutations across the whole install. The reconciler parks an
/// intent that way itself when the audiobook row it names has already gone: the
/// paths that intent was going to clean went with that row, so no later pass can
/// do better.
/// </para>
/// </summary>
public sealed class AudiobookDeletionIntentReconciler(
    IAudiobookDeletionIntentStore intentStore,
    IAudiobookRepository audiobookRepository,
    IAudiobookDeletionCommitService deletionCommitService,
    ILogger<AudiobookDeletionIntentReconciler> logger) : IAudiobookDeletionIntentReconciler
{
    private const string MissingAudiobookReason =
        "The audiobook row disappeared before explicit filesystem cleanup completed.";

    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var intents = await intentStore.GetActiveAsync(cancellationToken);
        foreach (var intent in intents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (intent.State == AudiobookDeletionIntentState.NeedsAttention)
            {
                ReportParkedIntent(intent, intent.Error);
                continue;
            }

            if (intent.State == AudiobookDeletionIntentState.Planned)
            {
                var audiobook = await audiobookRepository.GetByIdSnapshotAsync(
                    intent.AudiobookId,
                    cancellationToken);
                if (audiobook == null)
                {
                    await intentStore.MarkNeedsAttentionAsync(
                        intent.Id,
                        MissingAudiobookReason,
                        CancellationToken.None);
                    ReportParkedIntent(intent, MissingAudiobookReason);
                    continue;
                }

                // A Planned intent proves user intent existed, but the pinned
                // filesystem proof that authorized destructive mutation was
                // process-local. Startup recovery must not reacquire that proof
                // and delete on the user's behalf. Preserve the source and let a
                // new explicit delete request reacquire live proof.
                await intentStore.RecordErrorAsync(
                    intent.Id,
                    "Filesystem deletion was not resumed after restart because live delete proof expires at the process boundary. Retry the explicit delete to reacquire live proof.",
                    CancellationToken.None);
                logger.LogWarning(
                    "Audiobook deletion intent {IntentId} retained filesystem content after restart; explicit retry is required",
                    intent.Id);
                continue;
            }

            var commit = await deletionCommitService.DeleteAsync(
                intent.AudiobookId,
                includeFiles: false,
                CancellationToken.None);
            if (commit.Outcome == AudiobookDeletionCommitOutcome.Failed)
            {
                await intentStore.RecordErrorAsync(
                    intent.Id,
                    "Filesystem cleanup completed, but the database deletion could not be committed during recovery.",
                    CancellationToken.None);
                logger.LogWarning(
                    "Audiobook deletion intent {IntentId} remains pending because database deletion could not be committed",
                    intent.Id);
                continue;
            }

            await intentStore.MarkCompletedAsync(
                intent.Id,
                CancellationToken.None);
            logger.LogInformation(
                "Recovered durable audiobook deletion {IntentId} for audiobook {AudiobookId}",
                intent.Id,
                intent.AudiobookId);
        }
    }

    private void ReportParkedIntent(AudiobookDeletionIntent intent, string? reason) =>
        logger.LogError(
            "Audiobook deletion intent {IntentId} for audiobook {AudiobookId} cannot be recovered and was skipped so the rest of library filesystem startup can continue. Files belonging to that audiobook may still be on disk and need clearing by hand. Reason: {Reason}",
            intent.Id,
            intent.AudiobookId,
            string.IsNullOrWhiteSpace(reason)
                ? "No reason was recorded against the intent."
                : reason);
}
