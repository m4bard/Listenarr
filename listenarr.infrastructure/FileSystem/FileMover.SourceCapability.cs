using System.ComponentModel;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.FileSystem;

public partial class FileMover : IFilePublicationSourceCapability
{
    public async Task<FilePublicationSourceCapabilityResult> CheckAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        try
        {
            var fullPath = Path.GetFullPath(sourcePath);
            var parent = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(parent)
                || string.IsNullOrWhiteSpace(fileName))
            {
                return FilePublicationSourceCapabilityResult.Unsupported(
                    "The source path does not identify a file beneath a directory.");
            }

            using var anchor = PinnedDirectoryCreation.OpenPinnedHierarchyNoFollow(
                parent,
                createMissing: false);
            var openOutcome = anchor.TryOpenExistingFileWithOutcome(
                fileName,
                requireDeleteAccess: false,
                out var openedEntry);
            using var entry = openedEntry;
            if (openOutcome == PinnedFileOpenOutcome.NotFound)
            {
                return FilePublicationSourceCapabilityResult.Unsupported(
                    "The source file does not exist.",
                    FilePublicationSourceCapabilityFailureKind.Missing);
            }
            if (openOutcome == PinnedFileOpenOutcome.Unavailable)
            {
                return FilePublicationSourceCapabilityResult.Unsupported(
                    "The source file is temporarily unavailable.",
                    FilePublicationSourceCapabilityFailureKind.Unavailable);
            }
            if (entry == null || !entry.IsRegularFile())
            {
                return FilePublicationSourceCapabilityResult.Unsupported(
                    "The source path is not a regular file that can be published safely.");
            }
            if (!entry.VisiblePathMatches())
            {
                return FilePublicationSourceCapabilityResult.Unsupported(
                    "The source file changed while its publication capability was being verified.",
                    FilePublicationSourceCapabilityFailureKind.Unavailable);
            }

            FilePublicationSourceProof sourceProof;
            try
            {
                if (ForceContentOnlySourceProofForTest)
                {
                    throw new PlatformNotSupportedException(
                        "Durable source identity was disabled for this test.");
                }
                var proof = await CaptureMarkerlessSourceProofAsync(
                    entry,
                    cancellationToken,
                    includeSha256: true);
                sourceProof = new FilePublicationSourceProof(
                    proof.PhysicalObjectIdentity,
                    proof.Length,
                    proof.Sha256!);
            }
            catch (Exception exception) when (exception is
                PlatformNotSupportedException or NotSupportedException)
            {
                sourceProof = await CaptureContentOnlySourceProofAsync(
                    entry,
                    cancellationToken);
            }
            if (!anchor.VisiblePathMatches()
                || !entry.VisiblePathMatches())
            {
                return FilePublicationSourceCapabilityResult.Unsupported(
                    "The source file changed while its durable identity was being verified.",
                    FilePublicationSourceCapabilityFailureKind.Unavailable);
            }

            return FilePublicationSourceCapabilityResult.SupportedForProof(
                sourceProof);
        }
        catch (Exception exception) when (
            FileSystemSafety.IsProvenMissingPathException(exception))
        {
            return FilePublicationSourceCapabilityResult.Unsupported(
                "The source file does not exist.",
                FilePublicationSourceCapabilityFailureKind.Missing);
        }
        catch (PlatformNotSupportedException exception)
        {
            return FilePublicationSourceCapabilityResult.Unsupported(exception.Message);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or Win32Exception
                or InvalidOperationException or NotSupportedException
                or PathTooLongException or System.Security.SecurityException)
        {
            // Seven exception types reach here and used to leave through one sentence that named
            // none of them. A locked file, a permissions problem and an unreadable mount were
            // indistinguishable afterwards, and this is the gate that refuses the import, so the
            // one line an operator gets is the only thing they have to go on.
            var reason = ComposeUnsupportedReason(exception, FindSymlinkedAncestor(sourcePath));

            _logger.LogWarning(
                exception,
                "Source publication capability unavailable for {Source}: {Detail} (native error {NativeError})",
                LogRedaction.SanitizeFilePath(sourcePath),
                reason,
                // Nullable on purpose. Zero is a real errno meaning success, so reporting it for
                // the six exception types that carry no native code would be a false reading.
                (exception as Win32Exception)?.NativeErrorCode);

            return FilePublicationSourceCapabilityResult.Unsupported(
                reason,
                FilePublicationSourceCapabilityFailureKind.Unavailable);
        }
    }

    /// <summary>
    /// The refusal an operator reads, cause first.
    /// </summary>
    /// <remarks>
    /// Nothing downstream truncates or redacts this again: what this method returns is what
    /// reaches the operator and, through <c>ImportResult</c> and History, the activity API. The
    /// cause is formatted by <c>ExceptionCause</c> rather than here, because the import result
    /// that persists a failure into History has to say the same thing this does. It also gives
    /// this gate the inner chain it had no way to reach while it formatted the outer exception
    /// on its own. Leading with the cause means that if some other consumer still truncates the
    /// combined string, the half that survives is the half that says what went wrong.
    ///
    /// The linked-ancestor segment is a filesystem path, not free text, so it goes through
    /// <c>LogRedaction.SanitizeFilePath</c> like every other path in this directory
    /// (<c>FileMover.PathSafety.cs</c> among them) rather than through <c>SanitizeText</c> on the
    /// full path, which would have put the host's directory layout into that same API response.
    /// It is still attacker-influenced, since a download client names the directories under it,
    /// so the filename <c>SanitizeFilePath</c> keeps is also run through <c>SanitizeText</c>,
    /// which strips a newline a crafted directory name could use to forge a second log line.
    /// </remarks>
    internal static string ComposeUnsupportedReason(Exception exception, string? linkedAncestor)
    {
        var cause = LogRedaction.SanitizeText(ExceptionCause.Describe(exception));

        return linkedAncestor == null
            ? $"{cause} The source file could not be pinned to a durable physical generation and content proof."
            : $"{cause} The source file could not be pinned: it is reached through a symbolic link at "
              + $"'{LogRedaction.SanitizeText(LogRedaction.SanitizeFilePath(linkedAncestor))}', which cannot be pinned, so configure the real path instead.";
    }

    /// <summary>
    /// The first directory in the path that is a symbolic link, or null if there is none.
    /// </summary>
    /// <remarks>
    /// Refusing a linked ancestor is deliberate and covered by
    /// CheckPublicationSource_LinkedAncestor_ReturnsUnsupported, so this does not change the
    /// answer. It only says which segment caused it, because the raw failure is an ENOTDIR from
    /// openat and gives an operator nothing to act on.
    /// </remarks>
    private static string? FindSymlinkedAncestor(string sourcePath)
    {
        try
        {
            var current = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(current)
                    && Directory.ResolveLinkTarget(current, returnFinalTarget: false) != null)
                {
                    return current;
                }
                current = Path.GetDirectoryName(current);
            }
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or NotSupportedException
                or System.Security.SecurityException)
        {
            // Best effort only. The caller still reports the original failure.
        }

        return null;
    }
}
