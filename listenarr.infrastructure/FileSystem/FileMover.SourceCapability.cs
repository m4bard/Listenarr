using System.ComponentModel;
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

            // Resolved for the purpose of opening the file, and for nothing else. The caller
            // still receives the path it passed in, so root containment, retirement policy and
            // every other decision downstream see exactly what they saw before.
            using var anchor = PinnedDirectoryCreation.OpenPinnedHierarchyNoFollow(
                ResolveSymlinkedAncestors(parent),
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
            var linkedAncestor = FindSymlinkedAncestor(sourcePath);
            var detail = linkedAncestor == null
                ? $"{exception.GetType().Name}: {exception.Message}"
                : $"the path is reached through a symbolic link at '{linkedAncestor}', which cannot be pinned; "
                  + $"configure the real path instead ({exception.GetType().Name}: {exception.Message})";

            _logger.LogWarning(
                exception,
                "Source publication capability unavailable for {Source}: {Detail} (native error {NativeError})",
                LogRedaction.SanitizeText(sourcePath),
                detail,
                (exception as Win32Exception)?.NativeErrorCode ?? 0);

            return FilePublicationSourceCapabilityResult.Unsupported(
                $"The source file cannot be pinned to a durable physical generation and content proof: {detail}",
                FilePublicationSourceCapabilityFailureKind.Unavailable);
        }
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

    /// <summary>
    /// The physical path of an existing directory, with any symlinked component replaced by what
    /// it points at.
    /// </summary>
    /// <remarks>
    /// The pinned walk opens every segment with O_NOFOLLOW so a component cannot be substituted
    /// between the check and the use. On a destination, or a lock directory, that is load bearing:
    /// a swapped link redirects a write outside the configured boundary, which
    /// FileOperation_LinkedLockDirectoryAncestor_DoesNotCreateOutsideBoundary pins. On a source it
    /// buys less. The operation is a read and a hash, the resulting proof is an inode and a content
    /// digest, and both describe the object rather than the route taken to it. Resolving here does
    /// not weaken that proof.
    ///
    /// What it does cost is the guarantee that the route itself cannot change, so this is
    /// deliberately narrow: only the source capability walk resolves, only for opening, and the
    /// resolved path is never returned or used for a policy decision.
    ///
    /// Readarr draws the same line, in
    /// src/NzbDrone.Mono/Disk/SymbolicLinkResolver.cs, resolving the real path where physical
    /// identity matters and letting the OS follow links elsewhere.
    /// </remarks>
    private static string ResolveSymlinkedAncestors(string directory)
    {
        try
        {
            var resolved = Directory.ResolveLinkTarget(directory, returnFinalTarget: true);
            if (resolved != null)
            {
                return resolved.FullName;
            }

            var parent = Path.GetDirectoryName(directory);
            if (string.IsNullOrEmpty(parent) || parent == directory)
            {
                return directory;
            }

            var resolvedParent = ResolveSymlinkedAncestors(parent);
            return resolvedParent == parent
                ? directory
                : Path.Join(resolvedParent, Path.GetFileName(directory));
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or NotSupportedException
                or System.Security.SecurityException)
        {
            // Unreadable or cyclic. Hand back what we were given and let the pinned walk report
            // it, which now names the exception rather than swallowing it.
            return directory;
        }
    }
}
