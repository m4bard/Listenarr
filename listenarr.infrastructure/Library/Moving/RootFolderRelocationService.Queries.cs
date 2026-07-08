using Listenarr.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Library.Moving;

public sealed partial class RootFolderRelocationService
{
    public async Task<RootFolderPathChangeResult?> GetAsync(
        Guid relocationId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var relocation = await db.RootFolderRelocations
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == relocationId, cancellationToken);
        if (relocation == null) return null;
        var fallbackPath = ResolveCurrentPathFallback(relocation);
        string? rootPath = null;
        if (relocation.RootFolderId is int rootFolderId)
        {
            rootPath = await db.RootFolders
                .Where(root => root.Id == rootFolderId)
                .Select(root => root.Path)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return Map(relocation, rootPath ?? fallbackPath);
    }

    public async Task<RootFolderRelocation?> GetActiveForRootAsync(
        int rootFolderId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.RootFolderRelocations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                relocation => relocation.ActiveRootFolderId == rootFolderId,
                cancellationToken);
    }

    public async Task<bool> IsBoundaryProtectedAsync(
        string path,
        FileSystemPathSemantics semantics,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var boundaries = await db.RootFolderRelocations
            .Where(relocation => relocation.ActiveRootFolderId != null)
            .AsNoTracking()
            .Select(relocation => new
            {
                relocation.SourcePath,
                relocation.SourceCaseSensitivityMode,
                relocation.TargetPath,
                relocation.TargetCaseSensitivityMode
            })
            .ToListAsync(cancellationToken);
        foreach (var boundary in boundaries)
        {
            if (await ActiveBoundaryConflictsWithTargetAsync(
                    path,
                    semantics,
                    boundary.SourcePath,
                    boundary.SourceCaseSensitivityMode,
                    cancellationToken)
                || await ActiveBoundaryConflictsWithTargetAsync(
                    path,
                    semantics,
                    boundary.TargetPath,
                    boundary.TargetCaseSensitivityMode,
                    cancellationToken))
            {
                return true;
            }
        }

        return false;
    }
}
