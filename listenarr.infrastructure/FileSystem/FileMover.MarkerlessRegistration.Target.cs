using System.ComponentModel;
using Listenarr.Domain.Audiobooks.Enumerations;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.FileSystem;

public partial class FileMover
{
    /// <summary>
    /// Creates the pinned hardlink, translating the races that <see cref="FileAction.HardlinkCopy"/>
    /// should survive into the exception type the copy fallback already recognises.
    /// </summary>
    /// <remarks>
    /// PinnedFileEntry.CreateHardLinkTo raises InvalidOperationException both before the link
    /// exists (an endpoint changed) and after it exists (the created link does not identify the
    /// pinned source generation, or the entry changed while it was being opened). Only the first
    /// can be answered with a copy. In the others the destination name is already occupied, the
    /// cleanup inside CreateHardLinkTo deliberately does not remove an entry it cannot prove is
    /// its own, and the copy path would fail on the existing name anyway, so the original failure
    /// is the more useful one to report. The destination is therefore probed to tell them apart
    /// rather than assumed.
    /// </remarks>
    private async Task<PinnedDirectoryCreation.PinnedFileEntry>
        CreateHardLinkOrTranslateRaceAsync(
            FileAction action,
            PinnedDirectoryCreation.PinnedFileEntry sourceEntry,
            FileMoveGateLease gate)
    {
        try
        {
            if (BeforePinnedHardlinkCreationForTestAsync != null)
            {
                await BeforePinnedHardlinkCreationForTestAsync();
            }
            return sourceEntry.CreateHardLinkTo(
                gate.DestinationParent,
                gate.DestinationName,
                AfterPinnedHardlinkCreatedForTest);
        }
        catch (InvalidOperationException exception)
            when (action == FileAction.HardlinkCopy)
        {
            using var strayTarget = gate.DestinationParent.TryOpenExistingFile(
                gate.DestinationName,
                requireDeleteAccess: false);
            if (strayTarget != null)
            {
                throw;
            }

            throw new IOException(
                "A pinned hardlink endpoint raced its own publication before the link was created.",
                exception);
        }
    }

    private async Task<FileMutationJournal> PublishMarkerlessRegistrationTargetAsync(
        FileAction action,
        FileMoveGateLease gate,
        FileMutationJournal journal,
        CancellationToken cancellationToken)
    {
        using var sourceEntry = gate.SourceParent.TryOpenExistingFile(
            gate.SourceName,
            requireDeleteAccess: false);
        using var existingTarget = gate.DestinationParent.TryOpenExistingFile(
            gate.DestinationName,
            requireDeleteAccess: false);

        var sourceSharesDestinationVolume = sourceEntry != null
            && !ForceCrossVolumeForTest
            && sourceEntry.IsOnSameVolume(gate.DestinationParent);
        var requiresGenerationPreservingLink =
            action == FileAction.HardlinkCopy
            || (action == FileAction.Move
                && !OperatingSystem.IsWindows()
                && sourceSharesDestinationVolume);

        if (existingTarget != null)
        {
            if (requiresGenerationPreservingLink
                && sourceEntry != null
                && VisiblePathMatchesOrThrowUnavailable(
                    sourceEntry,
                    "The hardlink registration source is temporarily unavailable while interrupted publication is being verified.")
                && VisiblePathMatchesOrThrowUnavailable(
                    existingTarget,
                    "The hardlink registration destination is temporarily unavailable while interrupted publication is being verified.")
                && sourceEntry.IdentifiesSameEntry(existingTarget)
                && await MatchesMarkerlessSourceProofAsync(
                    sourceEntry,
                    journal,
                    cancellationToken)
                && await MatchesMarkerlessTargetContentAsync(
                    existingTarget,
                    journal,
                    cancellationToken))
            {
                return await _fileMutationJournalStore!.AdvanceAsync(
                    journal.OperationId,
                    FileMutationJournalState.TargetIdentityPersisted,
                    existingTarget.GetObjectIdentity(),
                    audiobookId: null,
                    error: null,
                    cancellationToken);
            }

            await MarkMarkerlessRegistrationNeedsAttentionAsync(
                journal,
                "A registration destination appeared before its physical identity was persisted.",
                cancellationToken);
            return await _fileMutationJournalStore!.GetAsync(
                journal.OperationId,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "The markerless registration journal disappeared.");
        }

        if (sourceEntry == null
            || !await MatchesMarkerlessSourceProofAsync(
                sourceEntry,
                journal,
                cancellationToken))
        {
            await MarkMarkerlessRegistrationNeedsAttentionAsync(
                journal,
                "The registration source changed before destination publication.",
                cancellationToken);
            return await _fileMutationJournalStore!.GetAsync(
                journal.OperationId,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "The markerless registration journal disappeared.");
        }

        string targetIdentity;
        PinnedDirectoryCreation.PinnedFileEntry? publishedHardlink = null;
        if (requiresGenerationPreservingLink
            && sourceSharesDestinationVolume)
        {
            try
            {
                publishedHardlink = await CreateHardLinkOrTranslateRaceAsync(
                    action,
                    sourceEntry,
                    gate);
                targetIdentity = publishedHardlink.GetObjectIdentity();
                if (AfterMarkerlessRegistrationTargetCreatedBeforeStateForTestAsync != null)
                {
                    await AfterMarkerlessRegistrationTargetCreatedBeforeStateForTestAsync();
                }
                journal = await _fileMutationJournalStore!.AdvanceAsync(
                    journal.OperationId,
                    FileMutationJournalState.TargetIdentityPersisted,
                    targetIdentity,
                    audiobookId: null,
                    error: null,
                    cancellationToken);
                if (AfterMarkerlessRegistrationTargetStateForTestAsync != null)
                {
                    await AfterMarkerlessRegistrationTargetStateForTestAsync();
                }
                return journal;
            }
            catch (Exception exception) when (exception is
                IOException or Win32Exception or PlatformNotSupportedException)
            {
                if (action == FileAction.Move && !OperatingSystem.IsWindows())
                {
                    _logger.LogWarning(
                        exception,
                        "Markerless Unix move publication could not preserve the exact source generation with a hardlink: {Source} -> {Destination}",
                        LogRedaction.SanitizeFilePath(gate.SourcePath),
                        LogRedaction.SanitizeFilePath(gate.DestinationPath));
                    throw new IOException(
                        "The move source generation could not be published safely on this filesystem.",
                        exception);
                }

                _logger.LogWarning(
                    exception,
                    "Markerless hardlink publication was unavailable; falling back to a direct final-name copy, so the destination will be an independent copy rather than a link: {Source} -> {Destination}",
                    LogRedaction.SanitizeFilePath(gate.SourcePath),
                    LogRedaction.SanitizeFilePath(gate.DestinationPath));
            }
            finally
            {
                publishedHardlink?.Dispose();
            }
        }

        journal = await EnsureMarkerlessSourceHashAsync(
            sourceEntry,
            journal,
            cancellationToken);
        using var created = gate.DestinationParent.CreateNewFile(
            gate.DestinationName);
        targetIdentity = created.GetObjectIdentity();
        if (AfterMarkerlessRegistrationTargetCreatedBeforeStateForTestAsync != null)
        {
            await AfterMarkerlessRegistrationTargetCreatedBeforeStateForTestAsync();
        }
        journal = await _fileMutationJournalStore!.AdvanceAsync(
            journal.OperationId,
            FileMutationJournalState.TargetIdentityPersisted,
            targetIdentity,
            audiobookId: null,
            error: null,
            cancellationToken);
        if (AfterMarkerlessRegistrationTargetStateForTestAsync != null)
        {
            await AfterMarkerlessRegistrationTargetStateForTestAsync();
        }
        return journal;
    }

    private async Task<FileMutationJournal> VerifyMarkerlessRegistrationTargetAsync(
        FileMoveGateLease gate,
        FileMutationJournal journal,
        CancellationToken cancellationToken)
    {
        using var targetEntry = gate.DestinationParent.TryOpenExistingFile(
            gate.DestinationName,
            requireDeleteAccess: false);
        if (targetEntry == null
            || !TargetMatchesMarkerlessJournal(targetEntry, journal))
        {
            await MarkMarkerlessRegistrationNeedsAttentionAsync(
                journal,
                "The registration destination changed before content verification.",
                cancellationToken);
            return await _fileMutationJournalStore!.GetAsync(
                journal.OperationId,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "The markerless registration journal disappeared.");
        }

        if (!await MatchesMarkerlessTargetContentAsync(
                targetEntry,
                journal,
                cancellationToken))
        {
            using var sourceEntry = gate.SourceParent.TryOpenExistingFile(
                gate.SourceName,
                requireDeleteAccess: false);
            if (sourceEntry == null
                || !await MatchesMarkerlessSourceProofAsync(
                    sourceEntry,
                    journal,
                    cancellationToken))
            {
                await MarkMarkerlessRegistrationNeedsAttentionAsync(
                    journal,
                    "The registration source is unavailable before destination content was verified.",
                    cancellationToken);
                return await _fileMutationJournalStore!.GetAsync(
                    journal.OperationId,
                    cancellationToken)
                    ?? throw new InvalidOperationException(
                        "The markerless registration journal disappeared.");
            }

            await CopyMarkerlessFileAsync(
                sourceEntry,
                targetEntry,
                cancellationToken);
            sourceEntry.PreserveMarkerlessMetadataTo(targetEntry);
            if (AfterMarkerlessRegistrationTargetWrittenBeforeVerifiedStateForTestAsync != null)
            {
                await AfterMarkerlessRegistrationTargetWrittenBeforeVerifiedStateForTestAsync();
            }
            if (!TargetMatchesMarkerlessJournal(targetEntry, journal)
                || !await MatchesMarkerlessTargetContentAsync(
                    targetEntry,
                    journal,
                    cancellationToken))
            {
                throw new IOException(
                    "The markerless registration destination failed content verification.");
            }
        }

        return await _fileMutationJournalStore!.AdvanceAsync(
            journal.OperationId,
            FileMutationJournalState.TargetVerified,
            journal.TargetPhysicalObjectIdentity,
            audiobookId: null,
            error: null,
            cancellationToken);
    }

    private static async Task<bool> MarkerlessRegistrationTargetMatchesAsync(
        FileMoveGateLease gate,
        FileMutationJournal journal,
        CancellationToken cancellationToken)
    {
        using var targetEntry = gate.DestinationParent.TryOpenExistingFile(
            gate.DestinationName,
            requireDeleteAccess: false);
        return targetEntry != null
            && TargetMatchesMarkerlessJournal(targetEntry, journal)
            && await MatchesMarkerlessTargetContentAsync(
                targetEntry,
                journal,
                cancellationToken);
    }
}
