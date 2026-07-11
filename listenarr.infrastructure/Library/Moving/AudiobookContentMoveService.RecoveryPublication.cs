using System.Text.Json;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private async Task WriteRecoveryMarkerAsync(
        string markerDirectory,
        AudiobookContentMoveRequest request,
        string source,
        string target,
        string stage,
        CancellationToken cancellationToken)
    {
        ValidateExistingMoveDirectory(markerDirectory, "recovery-marker directory");
        var markerPath = GetRecoveryMarkerPath(markerDirectory, request.JobId);
        if (!FileSystemSafety.TryValidateMutationTarget(
                markerPath,
                [markerDirectory],
                out markerPath,
                out var markerReason))
        {
            throw new MoveNeedsAttentionException(markerReason);
        }

        var marker = new MoveRecoveryMarker(
            RecoveryMarkerVersion,
            request.JobId,
            Path.GetFullPath(source),
            Path.GetFullPath(target),
            stage);
        var payload = JsonSerializer.SerializeToUtf8Bytes(marker);
        var writePath = markerPath + $".writing-{Guid.NewGuid():N}";
        FileAttributes? previousMarkerAttributes = null;

        faultInjector?.OnRecoveryMarkerWrite(
            request.JobId,
            RecoveryMarkerWriteFaultPoint.BeforeTemporaryFileCreation);

        try
        {
            await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
            ValidateNewRecoveryMarkerWritePath(writePath, markerDirectory);
            using (var stream = new FileStream(
                writePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                var split = Math.Max(1, payload.Length / 2);
                stream.Write(payload.AsSpan(0, split));
                faultInjector?.OnRecoveryMarkerWrite(
                    request.JobId,
                    RecoveryMarkerWriteFaultPoint.DuringJsonWrite);
                await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
                stream.Write(payload.AsSpan(split));
                faultInjector?.OnRecoveryMarkerWrite(
                    request.JobId,
                    RecoveryMarkerWriteFaultPoint.DuringFlush);
                await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            faultInjector?.OnRecoveryMarkerWrite(
                request.JobId,
                RecoveryMarkerWriteFaultPoint.AfterTemporaryFileWritten);
            faultInjector?.OnRecoveryMarkerWrite(
                request.JobId,
                RecoveryMarkerWriteFaultPoint.BeforePublication);
            await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);

            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(
                    writePath,
                    File.GetAttributes(writePath) | FileAttributes.Hidden);
                if (File.Exists(markerPath))
                {
                    ValidateExistingRecoveryMarker(
                        markerDirectory,
                        markerPath,
                        request,
                        source,
                        target);
                    previousMarkerAttributes = File.GetAttributes(markerPath);
                    await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
                    File.SetAttributes(
                        markerPath,
                        previousMarkerAttributes.Value & ~FileAttributes.Hidden);
                }
            }

            ValidateRecoveryMarkerPublicationPaths(
                markerDirectory,
                writePath,
                markerPath);
            if (File.Exists(markerPath))
            {
                ValidateExistingRecoveryMarker(
                    markerDirectory,
                    markerPath,
                    request,
                    source,
                    target);
            }

            await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
            ValidateRecoveryMarkerPublicationPaths(
                markerDirectory,
                writePath,
                markerPath);
            File.Move(writePath, markerPath, overwrite: true);
        }
        catch (Exception exception) when (exception is MoveLeaseLostException or PersistenceException)
        {
            throw;
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            Exception? restorationException = null;
            if (OperatingSystem.IsWindows()
                && previousMarkerAttributes.HasValue
                && File.Exists(markerPath))
            {
                try
                {
                    await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
                    ValidateExistingRecoveryMarker(
                        markerDirectory,
                        markerPath,
                        request,
                        source,
                        target);
                    File.SetAttributes(markerPath, previousMarkerAttributes.Value);
                }
                catch (Exception restoreException) when (restoreException is
                    MoveLeaseLostException or PersistenceException)
                {
                    throw;
                }
                catch (Exception restoreException) when (WorkerExceptionClassifier.IsNonFatal(restoreException))
                {
                    restorationException = restoreException;
                }
            }

            Exception? cleanupException = null;
            try
            {
                faultInjector?.OnRecoveryMarkerWrite(
                    request.JobId,
                    RecoveryMarkerWriteFaultPoint.BeforeTemporaryFileDeletion);
                if (File.Exists(writePath))
                {
                    await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
                    ValidateRecoveryMarkerWritePath(writePath, markerDirectory);
                    File.Delete(writePath);
                }
            }
            catch (Exception temporaryCleanupException) when (temporaryCleanupException is
                MoveLeaseLostException or PersistenceException)
            {
                throw;
            }
            catch (Exception temporaryCleanupException) when (WorkerExceptionClassifier.IsNonFatal(temporaryCleanupException))
            {
                cleanupException = temporaryCleanupException;
            }

            if (restorationException != null || cleanupException != null)
            {
                throw new MoveNeedsAttentionException(
                    $"Recovery marker publication failed and recovery state could not be restored cleanly. "
                    + $"Publication error: {exception.Message}. "
                    + $"Attribute restoration error: {restorationException?.Message ?? "none"}. "
                    + $"Temporary cleanup error: {cleanupException?.Message ?? "none"}.");
            }

            throw;
        }
    }
}
