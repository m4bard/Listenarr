namespace Listenarr.Infrastructure.FileSystem;

/// <summary>
/// The physical path of an existing directory, with every symlinked component in the whole chain
/// replaced by what it ultimately points at, and the links that chain passed through.
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
/// deliberately narrow: the source capability walk resolves only for opening, and the publication
/// plan uses the resolved path only to find the root a source physically belongs to.
///
/// Readarr draws the same line, in src/NzbDrone.Mono/Disk/SymbolicLinkResolver.cs, resolving
/// the real path where physical identity matters and letting the OS follow links elsewhere.
/// Its GetCompleteRealPath walks every path component in order, not only the last one, and
/// bounds the walk at 32 hops, explicitly in the name of the same ELOOP the kernel enforces
/// (SymbolicLinkResolver.cs lines 22-50). This resolver does the same, component by component,
/// with a comparable cap.
/// </remarks>
internal static class SymlinkedAncestors
{
    /// <summary>
    /// How many links this resolver will follow in one chain before refusing to go further, in
    /// the spirit of the OS ELOOP limit (Linux defaults to 40; Readarr's own resolver caps at 32).
    /// Bounds both a direct cycle the walk's own visited-set already catches sooner and a very long
    /// non-repeating chain that never revisits a directory.
    /// </summary>
    private const int MaxChainHops = 40;

    /// <summary>
    /// The outcome of walking a directory's chain. <see cref="BlockingLink"/> is null when every
    /// link in the chain resolved to something real; otherwise it names the specific link where the
    /// walk broke: one already visited in this same walk (a cycle), one whose target could not be
    /// inspected (most often because it does not exist), or the one where the hop cap was reached.
    /// <see cref="Links"/> lists every link the walk followed, as spelled when it met them.
    /// </summary>
    internal readonly record struct ChainWalk(
        string ResolvedPath,
        string? BlockingLink,
        IReadOnlyList<string> Links);

    /// <summary>
    /// Walks a directory's own symlink chain, then its ancestors', substituting each link's target
    /// as it goes. Throws when a directory on the way cannot be inspected at all.
    /// </summary>
    internal static ChainWalk Walk(string directory)
    {
        var links = new List<string>();
        var (resolvedPath, blockingLink) = Walk(
            directory,
            new HashSet<string>(StringComparer.Ordinal),
            MaxChainHops,
            links);
        return new ChainWalk(resolvedPath, blockingLink, links);
    }

    /// <summary>
    /// The directory with its whole chain resolved, or the directory unchanged when the chain is
    /// broken or cannot be inspected.
    /// </summary>
    internal static string Resolve(string directory)
    {
        try
        {
            var walk = Walk(directory);

            // A blocked walk (a cycle, a dangling target, or the hop cap) hands back the original,
            // unresolved directory rather than whatever partial progress it made. A pinned walk
            // then fails naturally on the first link it cannot follow.
            return walk.BlockingLink == null ? walk.ResolvedPath : directory;
        }
        catch (Exception exception) when (IsInspectionFailure(exception))
        {
            // Unreadable, or the directory does not exist at all (unrelated to any link). Hand
            // back what we were given and let the pinned walk report it.
            return directory;
        }
    }

    /// <summary>
    /// A file path with its parent directory's chain resolved; the file name itself is kept.
    /// </summary>
    internal static string ResolveFileParent(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var parent = Path.GetDirectoryName(fullPath);
        return string.IsNullOrEmpty(parent)
            ? fullPath
            : Path.Join(Resolve(parent), Path.GetFileName(fullPath));
    }

    /// <summary>
    /// The physical directory a link itself sits in: its parent, resolved. Where the link leads
    /// does not enter into it.
    /// </summary>
    internal static string PhysicalParentOf(string link)
    {
        var parent = Path.GetDirectoryName(link);
        return string.IsNullOrEmpty(parent) ? link : Resolve(parent);
    }

    internal static bool IsInspectionFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or NotSupportedException
            or System.Security.SecurityException;

    private static (string ResolvedPath, string? BlockingLink) Walk(
        string directory,
        HashSet<string> visited,
        int hopsRemaining,
        List<string> links)
    {
        var resolved = Directory.ResolveLinkTarget(directory, returnFinalTarget: false);
        if (resolved != null)
        {
            if (hopsRemaining <= 0 || !visited.Add(directory))
            {
                return (directory, directory);
            }

            links.Add(directory);
            try
            {
                var inner = Walk(resolved.FullName, visited, hopsRemaining - 1, links);
                return inner.BlockingLink == null
                    ? inner
                    : (directory, inner.BlockingLink);
            }
            catch (Exception exception) when (IsInspectionFailure(exception))
            {
                // What this link points at cannot even be inspected, most often because it does
                // not exist. This link is where the chain breaks, not whatever it names.
                return (directory, directory);
            }
        }

        var parent = Path.GetDirectoryName(directory);
        if (string.IsNullOrEmpty(parent) || parent == directory)
        {
            return (directory, null);
        }

        var parentWalk = Walk(parent, visited, hopsRemaining, links);
        if (parentWalk.BlockingLink != null)
        {
            return (directory, parentWalk.BlockingLink);
        }

        var resolvedPath = parentWalk.ResolvedPath == parent
            ? directory
            : Path.Join(parentWalk.ResolvedPath, Path.GetFileName(directory));
        return (resolvedPath, null);
    }
}
