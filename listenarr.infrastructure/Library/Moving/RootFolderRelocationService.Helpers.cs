using Listenarr.Domain.Common;
using Listenarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Library.Moving;

public sealed partial class RootFolderRelocationService
{
    private static string MapTargetPath(
        string sourceRoot,
        string targetRoot,
        string sourcePath,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics)
    {
        if (!FileSystemPathIdentity.TryGetRelativePathWithinBase(
            sourceRoot,
            sourcePath,
            sourceSemantics,
            out var relativePath))
        {
            throw new InvalidOperationException("An audiobook path escaped its configured root.");
        }

        if (relativePath.Length == 0)
        {
            return targetRoot;
        }

        if (!FileSystemPathIdentity.TryResolveRelativePathWithinBase(
            targetRoot,
            FileSystemPathIdentity.ConvertRelativePathSyntax(
                relativePath,
                sourceSemantics.Syntax,
                targetSemantics.Syntax),
            targetSemantics,
            out var targetPath))
        {
            throw new InvalidOperationException("An audiobook path is invalid for the target root.");
        }

        return targetPath;
    }

    private static bool BoundariesOverlap(
        string first,
        string second,
        FileSystemPathSemantics semantics) =>
        FileSystemPathIdentity.IsSameOrInside(first, second, semantics)
        || FileSystemPathIdentity.IsSameOrInside(second, first, semantics);

    private static void ApplyRootMetadata(
        RootFolder root,
        RootFolderPathChangeCommand command,
        string targetPath,
        FileSystemSemanticsResolution resolution,
        string identityKey)
    {
        root.Path = targetPath;
        root.Name = command.DesiredName.Trim();
        root.IsDefault = command.DesiredIsDefault;
        root.CaseSensitivityMode = command.TargetCaseSensitivityMode;
        root.ResolvedCaseSensitivity = resolution.Semantics.CaseSensitivity;
        root.PathIdentityState = resolution.State;
        root.PathIdentityKey = identityKey;
        root.UpdatedAt = DateTime.UtcNow;
    }

    private static async Task ClearOtherDefaultsAsync(
        ListenArrDbContext db,
        int rootFolderId,
        CancellationToken cancellationToken)
    {
        var defaults = await db.RootFolders
            .Where(root => root.Id != rootFolderId && root.IsDefault)
            .ToListAsync(cancellationToken);
        foreach (var root in defaults) root.IsDefault = false;
    }

    private static RootFolderPathChangeResult Map(RootFolderRelocation relocation, string currentPath) => new(
        relocation.Id,
        relocation.RootFolderId,
        currentPath,
        relocation.TargetPath,
        relocation.Status,
        relocation.TotalJobs,
        relocation.CompletedJobs,
        relocation.Error);

    private async Task BroadcastAsync(
        RootFolderPathChangeResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await hubBroadcaster.BroadcastAsync(
                "RootFolderRelocationUpdate",
                result,
                cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Timed out broadcasting root relocation {0}: {1}",
                result.RelocationId,
                exception.Message);
        }
        catch (Exception exception) when (exception is not (
            OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            System.Diagnostics.Trace.TraceWarning(
                "Failed to broadcast root relocation {0}: {1}",
                result.RelocationId,
                exception.Message);
        }
    }
}
