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

        SourceProofParent? proofParent = null;
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

            proofParent = await SelectSourceProofParentAsync(parent);
            using var anchor = await OpenSourceProofParentAsync(
                proofParent.Value.Path,
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
            var reason = ComposeUnsupportedReason(exception, FindRefusingLink(sourcePath, proofParent));

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
    /// The directory the source proof is opened from, decided on where the source physically is
    /// rather than on how its path is spelled.
    /// </summary>
    /// <remarks>
    /// The parent as given is kept, and the managed boundary walk runs on it exactly as upstream's
    /// does, when it is spelled inside a configured root, when its root containment cannot be
    /// decided, when its link chain is broken, or when any link the chain passes through
    /// physically sits inside a configured root, compared against both the root's configured
    /// spelling and its real path. A link inside a root therefore cannot carry a source out of
    /// it, whichever alias of the root the source was reported through: inside a root the walk
    /// opens every segment below the root with O_NOFOLLOW and refuses the first link it meets,
    /// and outside every root by spelling it is a plain O_NOFOLLOW open from the filesystem root
    /// that refuses the first linked ancestor.
    ///
    /// Only when every link in the chain sits outside every root is the parent resolved, which is
    /// the shape of a download directory reached through a symlinked mount. The resolved path is
    /// then opened by upstream's own rule for where it physically lands: through that root's
    /// authorized walk if it is inside a configured root, with a plain O_NOFOLLOW open otherwise.
    /// The publication plan likewise keys a Move source's retirement on the root it physically
    /// belongs to. The caller still receives the path it passed in.
    /// </remarks>
    private async Task<SourceProofParent> SelectSourceProofParentAsync(string parent)
    {
        var managed = await ResolveManagedRootPathAsync(parent);
        if (managed.Root != null || managed.HasUnavailableOverlap)
            return new SourceProofParent(parent, managed, LinkInsideRoot: false);

        SymlinkedAncestors.ChainWalk walk;
        try
        {
            walk = SymlinkedAncestors.Walk(parent);
        }
        catch (Exception exception) when (SymlinkedAncestors.IsInspectionFailure(exception))
        {
            return new SourceProofParent(parent, managed, LinkInsideRoot: false);
        }

        if (walk.BlockingLink != null || walk.Links.Count == 0)
            return new SourceProofParent(parent, managed, LinkInsideRoot: false);
        if (await AnyLinkSitsInsideConfiguredRootAsync(walk.Links))
            return new SourceProofParent(parent, managed, LinkInsideRoot: true);
        return new SourceProofParent(walk.ResolvedPath, managed, LinkInsideRoot: false);
    }

    /// <summary>
    /// The directory the source proof is opened from, the root containment of the parent as
    /// given, and whether a link physically inside a root kept it unresolved.
    /// </summary>
    private readonly record struct SourceProofParent(
        string Path,
        ManagedRootPathResolution Managed,
        bool LinkInsideRoot);

    /// <summary>
    /// Whether any of these links physically sits inside a configured root: the directory that
    /// holds the link, resolved, is the root or below it, by the root's configured spelling or by
    /// its real path. A root whose containment cannot be decided counts as containing it, and so
    /// does a root whose real path cannot be established: compared by spelling alone, a root
    /// reached through a link that is unreadable, deleted or dangling at check time would let a
    /// link inside its real directory resolve and carry the source out.
    /// </summary>
    private async Task<bool> AnyLinkSitsInsideConfiguredRootAsync(IReadOnlyList<string> links)
    {
        if (_rootFolderRepository == null)
            return false;

        var locations = links
            .Select(SymlinkedAncestors.PhysicalParentOf)
            .Select(location => FileSystemPathIdentity.TryDetectAbsoluteSyntaxForHost(location, out var syntax)
                ? (Path: location, Syntax: (FileSystemPathSyntax?)syntax)
                : (Path: location, Syntax: null))
            .ToList();
        if (locations.Any(location => location.Syntax == null))
            return true;

        foreach (var root in await _rootFolderRepository.GetAllAsync())
        {
            if (string.IsNullOrWhiteSpace(root.Path))
                continue;

            var persisted = RootFolderPathSemantics.ResolvePersisted(root);
            if (!FileSystemPathIdentity.TryCanonicalizeUnambiguousStoredAbsolutePathForHost(
                    root.Path, out var canonicalRoot, out _)
                || !persisted.HasValue
                || persisted.Value.DetectAmbiguousCaseMatches)
            {
                if (locations.Any(location => FileSystemPathIdentity.StoredBoundaryMayContainPath(
                        root.Path, location.Path, location.Syntax!.Value, root.CaseSensitivityMode)))
                    return true;
                continue;
            }

            var semantics = persisted.Value.Semantics;
            if (TryResolveRealRoot(canonicalRoot) is not { } realRoot)
                return true;
            if (locations.Any(location =>
                    FileSystemPathIdentity.IsSameOrInside(location.Path, canonicalRoot, semantics)
                    || FileSystemPathIdentity.IsSameOrInside(location.Path, realRoot, semantics)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A configured root's real path, or null when it cannot be established: the root's own chain
    /// cannot be inspected, is broken, or does not end at an existing directory.
    /// </summary>
    private static string? TryResolveRealRoot(string canonicalRoot)
    {
        try
        {
            var walk = SymlinkedAncestors.Walk(canonicalRoot);
            return walk.BlockingLink == null && Directory.Exists(walk.ResolvedPath)
                ? walk.ResolvedPath
                : null;
        }
        catch (Exception exception) when (SymlinkedAncestors.IsInspectionFailure(exception))
        {
            return null;
        }
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
    /// The link a refusal is reported against, or null if no link is to blame.
    /// </summary>
    /// <remarks>
    /// Which link that is depends on which walk refused. When the parent was kept as given, the
    /// walk met the first link on its way down and stopped there, whether or not that link would
    /// have resolved: below the root for a parent spelled inside one, from the filesystem root for
    /// a parent kept because a link in its chain sits inside a root. When the parent was resolved,
    /// only a link that resolution could not remove is to blame. A failure before the parent was
    /// chosen at all is reported the last way.
    /// </remarks>
    private static string? FindRefusingLink(string sourcePath, SourceProofParent? proofParent)
    {
        if (proofParent is { Managed: { Root: { } root, Semantics: { } semantics } } managedParent)
        {
            return FileSystemPathIdentity.TryGetRelativePathWithinBase(
                    root.Path, managedParent.Path, semantics, out var relative)
                ? FindFirstLinkBelow(root.Path, relative)
                : null;
        }

        if (proofParent is { LinkInsideRoot: true } keptParent)
        {
            var filesystemRoot = Path.GetPathRoot(keptParent.Path);
            return string.IsNullOrEmpty(filesystemRoot)
                ? null
                : FindFirstLinkBelow(filesystemRoot, keptParent.Path[filesystemRoot.Length..]);
        }

        return FindSymlinkedAncestor(sourcePath);
    }

    /// <summary>
    /// The first symbolic link a no-follow walk from <paramref name="basePath"/> would open on its
    /// way down through <paramref name="relative"/>, or null if there is none.
    /// </summary>
    private static string? FindFirstLinkBelow(string basePath, string relative)
    {
        try
        {
            var current = basePath;
            foreach (var segment in relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries).Where(segment => segment != "."))
            {
                current = Path.Join(current, segment);
                if (Directory.ResolveLinkTarget(current, returnFinalTarget: false) != null)
                {
                    return current;
                }
            }
        }
        catch (Exception exception) when (SymlinkedAncestors.IsInspectionFailure(exception))
        {
            // Best effort only. The caller still reports the original failure.
        }

        return null;
    }

    /// <summary>
    /// The link that actually blocked resolution, or null if none did.
    /// </summary>
    /// <remarks>
    /// This does not change the answer. It only says which link caused a refusal, because the
    /// raw failure is an ENOTDIR from openat and gives an operator nothing to act on. When every
    /// link in the chain sits outside every configured root the chain resolves and is published,
    /// so this names a link only when that resolution could not remove it from the walk: a cycle,
    /// a dangling target, or the hop cap. It walks the same chain SymlinkedAncestors.Resolve does,
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

            return SymlinkedAncestors.Walk(current).BlockingLink;
        }
        catch (Exception exception) when (SymlinkedAncestors.IsInspectionFailure(exception))
        {
            // Best effort only. The caller still reports the original failure.
        }

        return null;
    }
}
