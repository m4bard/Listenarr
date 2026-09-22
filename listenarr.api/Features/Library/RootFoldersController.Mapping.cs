using Listenarr.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library;

public partial class RootFoldersController
{
    private ConflictObjectResult RegistrationRecoveryConflict(
        FileRegistrationRecoveryBlocker blocker)
    {
        var canRetry = blocker.Recoverability is
            FileRegistrationRecoveryDisposition.AutomaticRecovery
            or FileRegistrationRecoveryDisposition.WaitingForOwnerRetry;
        return Conflict(new
        {
            message = blocker.PublicReason,
            code = "registration_recovery_pending",
            operationId = blocker.OperationId,
            audiobookId = blocker.AudiobookId,
            journalState = blocker.JournalState.ToString(),
            ownerKind = blocker.OwnerKind,
            recoverability = blocker.Recoverability.ToString(),
            canRetry,
            canAbandon = false,
            retryOperationId = canRetry ? blocker.OperationId : (Guid?)null
        });
    }

    private async Task<RootFolderDto> MapAsync(RootFolder root)
    {
        RootFolderPathChangeResult? active = null;
        var relocation = await _relocationService.GetActiveForRootAsync(root.Id);
        if (relocation != null)
        {
            var relocationResult = await _relocationService.GetAsync(relocation.Id);
            active = relocationResult == null
                ? null
                : RootFolderRelocationPublicProjection.Sanitize(relocationResult);
        }

        var filesystem = _filesystemReadiness.Current;
        if (!filesystem.IsReady)
        {
            var failed = filesystem.Status == LibraryFilesystemInitializationStatus.Failed;
            return new RootFolderDto(
                root.Id,
                root.Name,
                root.Path,
                FileSystemPathIdentity.TryDetectAbsoluteSyntax(
                    root.Path,
                    out var initializingPathSyntax)
                        ? initializingPathSyntax.ToString()
                        : null,
                root.IsDefault,
                root.CaseSensitivityMode.ToString(),
                root.ResolvedCaseSensitivity.ToString(),
                root.PathIdentityState.ToString(),
                failed ? "InitializationFailed" : "Initializing",
                failed ? "InitializationFailed" : "Initializing",
                failed
                    ? filesystem.ErrorMessage
                        ?? "Library filesystem initialization failed. Filesystem operations are disabled."
                    : "Library filesystem initialization is in progress.",
                StorageDetail: null,
                CanConfirmCurrentFolder: false,
                CanChangePath: false,
                CanReadFilesystem: false,
                CanScanFilesystem: false,
                CanPublishNewFiles: false,
                CanMutateFilesystem: false,
                CanRetireSource: false,
                CanRetireAfterVerifiedCopy: false,
                root.WeakStorageSourceCleanupPolicy.ToString(),
                root.WeakStoragePolicyRevision,
                ConfirmationToken: null,
                root.CreatedAt,
                root.UpdatedAt,
                active);
        }

        var storage = await _storageHealthResolver.ResolveAsync(root);

        // Populated on read, same as Readarr's RootFolderService.GetDetails
        // (src/NzbDrone.Core/RootFolders/RootFolderService.cs:178-188): measured directly on
        // the root's own path, not its parent, because the root folder itself is expected to
        // already exist (unlike an audiobook's own not-yet-created folder, which is why the
        // import-time free-space guard measures one level up instead).
        long? freeSpaceBytes = null;
        long? totalSpaceBytes = null;
        if (_diskSpaceProbe.TryGetDiskSpace(root.Path, out var totalBytes, out var freeBytes))
        {
            freeSpaceBytes = freeBytes;
            totalSpaceBytes = totalBytes;
        }

        return new RootFolderDto(
            root.Id,
            root.Name,
            root.Path,
            FileSystemPathIdentity.TryDetectAbsoluteSyntax(
                root.Path,
                out var pathSyntax)
                    ? pathSyntax.ToString()
                    : null,
            root.IsDefault,
            root.CaseSensitivityMode.ToString(),
            root.ResolvedCaseSensitivity.ToString(),
            root.PathIdentityState.ToString(),
            storage.State.ToString(),
            storage.Reason.ToString(),
            storage.Message,
            storage.Detail,
            storage.CanConfirmCurrentFolder,
            storage.CanChangePath && active == null,
            storage.CanReadFilesystem,
            storage.CanScanFilesystem,
            storage.CanPublishNewFiles,
            storage.CanMutateFilesystem,
            storage.CanRetireSource,
            storage.CanRetireAfterVerifiedCopy,
            root.WeakStorageSourceCleanupPolicy.ToString(),
            root.WeakStoragePolicyRevision,
            storage.ConfirmationToken,
            root.CreatedAt,
            root.UpdatedAt,
            active,
            freeSpaceBytes,
            totalSpaceBytes);
    }
}
