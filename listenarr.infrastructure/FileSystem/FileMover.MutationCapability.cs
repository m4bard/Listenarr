using Listenarr.Domain.Audiobooks.Enumerations;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.FileSystem;

public partial class FileMover
{
    private readonly record struct ManagedRootPathResolution(
        RootFolder? Root,
        FileSystemPathSemantics? Semantics,
        bool HasUnavailableOverlap);

    private async Task<bool> IsNewMutationBlockedByCapabilityAsync(
        FileAction action,
        string source,
        string destination,
        Guid operationId)
    {
        if (_fileMutationJournalStore != null
            && await _fileMutationJournalStore.GetAsync(
                operationId,
                CancellationToken.None) != null)
        {
            return false;
        }

        if (await IsKnownManagedRootMutationBlockedAsync(
                action,
                source,
                destination))
        {
            return true;
        }

        return IsKnownReadOnlyMutationEndpoint(action, source, destination);
    }

    private async Task<bool> IsKnownManagedRootMutationBlockedAsync(
        FileAction action,
        string source,
        string destination)
    {
        if (_rootFolderRepository == null
            || _rootFolderStorageHealthResolver == null)
        {
            return false;
        }

        if (await IsManagedRootMutationBlockedAsync(
                destination,
                action,
                source,
                destination,
                "destination"))
        {
            return true;
        }

        if (action != FileAction.Move)
        {
            return false;
        }

        // A source reported through a link outside every root can physically sit in a root, and
        // that root's storage decides whether it may be mutated, exactly as when it is reported there.
        var physicalSource = SymlinkedAncestors.ResolveFileParent(source);
        return await IsManagedRootMutationBlockedAsync(
                source,
                action,
                source,
                destination,
                "source")
            || (physicalSource != Path.GetFullPath(source)
                && await IsManagedRootMutationBlockedAsync(
                    physicalSource,
                    action,
                    source,
                    destination,
                    "source"));
    }

    private async Task<bool> IsManagedRootMutationBlockedAsync(
        string path,
        FileAction action,
        string source,
        string destination,
        string endpoint)
    {
        var managedRoot = await ResolveManagedRootPathAsync(path);
        if (managedRoot.HasUnavailableOverlap)
        {
            LogMutation(
                FileMutationOutcome.Blocked,
                action,
                source,
                destination,
                $"The {endpoint} overlaps a configured root whose filesystem identity is unavailable");
            return true;
        }
        if (managedRoot.Root == null)
        {
            return false;
        }

        var storage = await _rootFolderStorageHealthResolver!.ResolveAsync(
            managedRoot.Root);
        if (storage.CanMutateFilesystem)
        {
            return false;
        }

        LogMutation(
            FileMutationOutcome.Blocked,
            action,
            source,
            destination,
            storage.Message
                ?? $"The managed {endpoint} root does not authorize filesystem mutations");
        return true;
    }

    private async Task<ManagedRootPathResolution> ResolveManagedRootPathAsync(
        string path)
    {
        if (_rootFolderRepository == null)
        {
            return default;
        }

        string fullPath;
        FileSystemPathSyntax syntax;
        try
        {
            fullPath = Path.GetFullPath(path);
            if (!FileSystemPathIdentity.TryDetectAbsoluteSyntaxForHost(
                    fullPath,
                    out syntax))
            {
                return default;
            }
        }
        catch (Exception exception) when (exception is not (
            OperationCanceledException or OutOfMemoryException
                or StackOverflowException))
        {
            _logger.LogDebug(
                exception,
                "Could not resolve a file-mutation path against configured roots");
            return default;
        }

        RootFolder? bestRoot = null;
        FileSystemPathSemantics? bestSemantics = null;
        var bestRootLength = -1;
        var unavailableRootLength = -1;
        foreach (var root in await _rootFolderRepository.GetAllAsync())
        {
            if (string.IsNullOrWhiteSpace(root.Path))
            {
                continue;
            }

            if (!FileSystemPathIdentity.TryCanonicalizeUnambiguousStoredAbsolutePathForHost(
                    root.Path,
                    out var canonicalRoot,
                    out _))
            {
                if (FileSystemPathIdentity.StoredBoundaryMayContainPath(
                        root.Path,
                        fullPath,
                        syntax,
                        root.CaseSensitivityMode))
                {
                    unavailableRootLength = Math.Max(
                        unavailableRootLength,
                        root.Path.Length);
                }
                continue;
            }

            var persisted = RootFolderPathSemantics.ResolvePersisted(root);
            if (!persisted.HasValue
                || persisted.Value.DetectAmbiguousCaseMatches
                || persisted.Value.Semantics.Syntax != syntax)
            {
                if (FileSystemPathIdentity.StoredBoundaryMayContainPath(
                        canonicalRoot,
                        fullPath,
                        syntax,
                        root.CaseSensitivityMode))
                {
                    unavailableRootLength = Math.Max(
                        unavailableRootLength,
                        canonicalRoot.Length);
                }
                continue;
            }

            if (!FileSystemPathIdentity.IsSameOrInside(
                    fullPath,
                    canonicalRoot,
                    persisted.Value.Semantics))
            {
                continue;
            }

            if (canonicalRoot.Length > bestRootLength)
            {
                bestRoot = root;
                bestSemantics = persisted.Value.Semantics;
                bestRootLength = canonicalRoot.Length;
            }
        }

        return new ManagedRootPathResolution(
            bestRoot,
            bestSemantics,
            unavailableRootLength >= bestRootLength
                && unavailableRootLength >= 0);
    }

    private bool IsKnownReadOnlyMutationEndpoint(
        FileAction action,
        string source,
        string destination)
    {
        if (IsKnownReadOnlyParent(destination, "destination"))
        {
            LogMutation(
                FileMutationOutcome.Blocked,
                action,
                source,
                destination,
                "The destination filesystem is mounted read-only");
            return true;
        }

        if (action == FileAction.Move
            && IsKnownReadOnlyParent(source, "source"))
        {
            LogMutation(
                FileMutationOutcome.Blocked,
                action,
                source,
                destination,
                "The source filesystem is mounted read-only");
            return true;
        }

        return false;
    }

    private bool IsKnownReadOnlyParent(string path, string endpoint)
    {
        try
        {
            var current = Path.GetDirectoryName(Path.GetFullPath(path));
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (Directory.Exists(current))
                {
                    return _readOnlyFileSystemProbe(current) == true;
                }

                current = Path.GetDirectoryName(current);
            }
        }
        catch (Exception exception) when (exception is not (
            OperationCanceledException or OutOfMemoryException
                or StackOverflowException))
        {
            _logger.LogDebug(
                exception,
                "Could not determine whether the {Endpoint} filesystem is read-only before file mutation",
                endpoint);
        }

        // This probe can deny mutation when read-only status is proven. Failure to
        // prove read-only does not grant authority; the existing generation-bound
        // mutation/recovery checks remain authoritative and fail closed independently.
        return false;
    }

}
