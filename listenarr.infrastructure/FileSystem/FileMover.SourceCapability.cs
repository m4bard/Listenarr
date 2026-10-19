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

            using var anchor = await OpenSourceProofParentAsync(
                await SelectSourceProofParentAsync(parent),
                cancellationToken);
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

            // Source capability exposes durable content evidence only. The pinned
            // entry here prevents path substitution while the proof is captured, but
            // its kernel identity is deliberately not exported as restart authority.
            var sourceProof = await CaptureContentOnlySourceProofAsync(
                entry,
                cancellationToken);
            if (!anchor.VisiblePathMatches()
                || !entry.VisiblePathMatches())
            {
                return FilePublicationSourceCapabilityResult.Unsupported(
                    "The source file changed while its content proof was being captured.",
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
                LogRedaction.SanitizeText(sourcePath),
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
    /// The directory the source proof is opened from: the parent as given when it names a place
    /// inside a configured root, otherwise the parent with its symlinked ancestors resolved.
    /// </summary>
    /// <remarks>
    /// Inside a configured root the managed boundary walk stays exactly as it is, so a link
    /// planted inside a root still cannot carry a source out of it: the walk opens every segment
    /// below the root with O_NOFOLLOW and refuses the first link it meets. Outside every root
    /// there is no boundary to escape, and the walk that would otherwise run is a plain
    /// O_NOFOLLOW open from the filesystem root, which refuses any linked ancestor at all, such
    /// as a download directory reached through a symlinked mount. Only that case resolves.
    ///
    /// The resolved path is used to open the file and for nothing else. The caller still
    /// receives the path it passed in, so root containment, retirement policy and every other
    /// decision downstream see exactly what they saw before. If the resolved path lands inside a
    /// configured root, it is opened through that root's authorized walk like any other path.
    /// A parent whose root containment cannot be decided is left unresolved, so the managed walk
    /// refuses it as it would have anyway.
    /// </remarks>
    private async Task<string> SelectSourceProofParentAsync(string parent)
    {
        var managed = await ResolveManagedRootPathAsync(parent);
        return managed.Root != null || managed.HasUnavailableOverlap
            ? parent
            : ResolveSymlinkedAncestors(parent);
    }

    private async Task<PinnedDirectoryCreation.PinnedDirectoryAnchor> OpenSourceProofParentAsync(
        string parent, CancellationToken cancellationToken)
    {
        var managed = await ResolveManagedRootPathAsync(parent);
        if (managed.HasUnavailableOverlap)
            throw new InvalidOperationException("The source overlaps an unresolved managed boundary.");
        if (managed.Root == null || managed.Semantics == null)
            return PinnedDirectoryCreation.OpenPinnedHierarchyNoFollow(parent, createMissing: false);
        if (_directoryBoundaryAuthorizer == null)
            throw new InvalidOperationException("The configured source boundary cannot be authorized.");
        using var authorization = await _directoryBoundaryAuthorizer.AuthorizeAsync(
            managed.Root.Path, managed.Semantics.Value, cancellationToken);
        var current = authorization.BoundaryAnchor.Duplicate();
        try
        {
            if (!FileSystemPathIdentity.TryGetRelativePathWithinBase(
                    managed.Root.Path, parent, managed.Semantics.Value, out var relative))
                throw new InvalidOperationException("The source parent escaped its configured boundary.");
            foreach (var segment in relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries).Where(segment => segment != "."))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var next = current.OpenExistingChild(segment);
                current.Dispose();
                current = next;
            }
            if (!authorization.BoundaryAnchor.VisiblePathMatches() || !current.VisiblePathMatches())
                throw new IOException("The configured source boundary changed during content capture.");
            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The refusal an operator reads, cause first.
    /// </summary>
    /// <remarks>
    /// Every consumer of <c>Reason</c> renders it through <c>LogRedaction.SanitizeText</c>, whose
    /// 200-character default used to cut the exception off the end and leave behind only the fixed
    /// sentence the operator already knew. Leading with the cause means the half that survives
    /// truncation is the half that says what went wrong.
    ///
    /// The cause is formatted by <c>ExceptionCause</c> rather than here, because the import result
    /// that persists a failure into History has to say the same thing this does. It also gives
    /// this gate the inner chain it had no way to reach while it formatted the outer exception
    /// on its own.
    ///
    /// Both interpolated values are attacker-influenced: a download client writes the file name,
    /// and the file name is what most of these exception messages quote. A newline in either would
    /// let a crafted name forge a second log record, so they go through the same SanitizeText
    /// every other call site in this directory uses.
    /// </remarks>
    internal static string ComposeUnsupportedReason(Exception exception, string? linkedAncestor)
    {
        var cause = LogRedaction.SanitizeText(ExceptionCause.Describe(exception));

        return linkedAncestor == null
            ? $"{cause} The source file could not be pinned long enough to capture a stable content proof."
            : $"{cause} The source file could not be pinned: it is reached through a symbolic link at "
              + $"'{LogRedaction.SanitizeText(linkedAncestor)}', which cannot be pinned, so configure the real path instead.";
    }

    /// <summary>
    /// The link that actually blocked resolution, or null if none did.
    /// </summary>
    /// <remarks>
    /// This does not change the answer. It only says which link caused a refusal, because the
    /// raw failure is an ENOTDIR from openat and gives an operator nothing to act on. Since
    /// ResolveSymlinkedAncestors, a linked source ancestor normally resolves and is published, so
    /// this names a link only when that resolution could not remove it from the walk: a cycle, a
    /// dangling target, or the hop cap. It walks the same chain ResolveSymlinkedAncestors does,
    /// because a chain of two or more links is not necessarily an ancestor of the original path at
    /// all; the second link only appears once the first has been substituted for its target.
    /// </remarks>
    private static string? FindSymlinkedAncestor(string sourcePath)
    {
        try
        {
            var current = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            if (string.IsNullOrEmpty(current))
            {
                return null;
            }

            return WalkSymlinkedAncestors(current, new HashSet<string>(StringComparer.Ordinal), MaxSymlinkChainHops)
                .BlockingLink;
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
    /// The physical path of an existing directory, with every symlinked component in the whole
    /// chain replaced by what it ultimately points at.
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
    /// Readarr draws the same line, in src/NzbDrone.Mono/Disk/SymbolicLinkResolver.cs, resolving
    /// the real path where physical identity matters and letting the OS follow links elsewhere.
    /// Its GetCompleteRealPath walks every path component in order, not only the last one, and
    /// bounds the walk at 32 hops, explicitly in the name of the same ELOOP the kernel enforces
    /// (SymbolicLinkResolver.cs lines 22-50). This resolver does the same, component by component,
    /// with a comparable cap.
    /// </remarks>
    private static string ResolveSymlinkedAncestors(string directory)
    {
        try
        {
            var walk = WalkSymlinkedAncestors(
                directory,
                new HashSet<string>(StringComparer.Ordinal),
                MaxSymlinkChainHops);

            // A blocked walk (a cycle, a dangling target, or the hop cap) hands back the original,
            // unresolved directory rather than whatever partial progress it made. The pinned walk
            // then fails naturally on the first link it cannot follow, and FindSymlinkedAncestor,
            // which performs the same walk, is what names the link actually responsible.
            return walk.BlockingLink == null ? walk.ResolvedPath : directory;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or NotSupportedException
                or System.Security.SecurityException)
        {
            // Unreadable, or the directory does not exist at all (unrelated to any link). Hand
            // back what we were given and let the pinned walk report it.
            return directory;
        }
    }

    /// <summary>
    /// How many links this resolver will follow in one chain before refusing to go further, in
    /// the spirit of the OS ELOOP limit (Linux defaults to 40; Readarr's own resolver caps at 32).
    /// Bounds both a direct cycle the walk's own visited-set already catches sooner and a very long
    /// non-repeating chain that never revisits a directory.
    /// </summary>
    private const int MaxSymlinkChainHops = 40;

    private readonly record struct SymlinkChainWalk(string ResolvedPath, string? BlockingLink);

    /// <summary>
    /// Walks a directory's own symlink chain, then its ancestors', substituting each link's target
    /// as it goes. <see cref="SymlinkChainWalk.BlockingLink"/> is null when every link in the chain
    /// resolved to something real; otherwise it names the specific link where the walk broke: one
    /// already visited in this same walk (a cycle), one whose target could not be inspected (most
    /// often because it does not exist), or the one where the hop cap was reached.
    /// </summary>
    private static SymlinkChainWalk WalkSymlinkedAncestors(
        string directory,
        HashSet<string> visited,
        int hopsRemaining)
    {
        var resolved = Directory.ResolveLinkTarget(directory, returnFinalTarget: false);
        if (resolved != null)
        {
            if (hopsRemaining <= 0 || !visited.Add(directory))
            {
                return new SymlinkChainWalk(directory, directory);
            }

            try
            {
                var inner = WalkSymlinkedAncestors(resolved.FullName, visited, hopsRemaining - 1);
                return inner.BlockingLink == null
                    ? inner
                    : new SymlinkChainWalk(directory, inner.BlockingLink);
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException or NotSupportedException
                    or System.Security.SecurityException)
            {
                // What this link points at cannot even be inspected, most often because it does
                // not exist. This link is where the chain breaks, not whatever it names.
                return new SymlinkChainWalk(directory, directory);
            }
        }

        var parent = Path.GetDirectoryName(directory);
        if (string.IsNullOrEmpty(parent) || parent == directory)
        {
            return new SymlinkChainWalk(directory, null);
        }

        var parentWalk = WalkSymlinkedAncestors(parent, visited, hopsRemaining);
        if (parentWalk.BlockingLink != null)
        {
            return new SymlinkChainWalk(directory, parentWalk.BlockingLink);
        }

        var resolvedPath = parentWalk.ResolvedPath == parent
            ? directory
            : Path.Join(parentWalk.ResolvedPath, Path.GetFileName(directory));
        return new SymlinkChainWalk(resolvedPath, null);
    }
}
