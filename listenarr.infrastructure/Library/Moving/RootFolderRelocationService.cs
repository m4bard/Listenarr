using Listenarr.Domain.Common;
using Listenarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Listenarr.Application.Common.Exceptions;

namespace Listenarr.Infrastructure.Library.Moving;

public sealed partial class RootFolderRelocationService(
    IDbContextFactory<ListenArrDbContext> dbContextFactory,
    IFileSystemSemanticsResolver semanticsResolver,
    IHubBroadcaster hubBroadcaster,
    TimeProvider timeProvider) : IRootFolderRelocationService
{
    private readonly SemaphoreSlim _rootIdentityGate = new(1, 1);
    private bool _rootIdentitiesReconciled;
    public async Task<RootFolderPathChangeResult> StartAsync(
        int rootFolderId,
        RootFolderPathChangeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.DesiredName))
        {
            throw new ArgumentException("Root folder name is required.", nameof(command));
        }

        var targetPath = FileSystemPathIdentity.ResolveNativeAbsolutePath(command.TargetPath);
        var targetResolution = await semanticsResolver.ResolveAsync(
            targetPath,
            command.TargetCaseSensitivityMode,
            cancellationToken);
        if (targetResolution.State != PathIdentityState.Valid)
        {
            throw new InvalidOperationException(
                targetResolution.Reason ?? "Target filesystem semantics are unavailable; select an explicit override.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var root = await db.RootFolders.SingleOrDefaultAsync(
            candidate => candidate.Id == rootFolderId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Root folder not found");
        if (await db.RootFolderRelocations.AnyAsync(
            relocation => relocation.ActiveRootFolderId == rootFolderId,
            cancellationToken))
        {
            throw new InvalidOperationException("The root folder already has an active relocation.");
        }

        var sourceResolution = await semanticsResolver.ResolveAsync(
            root.Path,
            root.CaseSensitivityMode,
            cancellationToken);
        if (sourceResolution.State != PathIdentityState.Valid)
        {
            throw new InvalidOperationException(
                "The current root filesystem semantics are unavailable; select an explicit override before moving it.");
        }

        var targetIdentityKey = FileSystemPathIdentity.CreateKey(
            "root",
            targetPath,
            targetResolution.Semantics);
        var otherRoots = await db.RootFolders
            .Where(candidate => candidate.Id != rootFolderId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var activeBoundaries = await db.RootFolderRelocations
            .Where(relocation => relocation.ActiveRootFolderId != null)
            .AsNoTracking()
            .Select(relocation => new { relocation.SourcePath, relocation.TargetPath })
            .ToListAsync(cancellationToken);
        var targetConflict = otherRoots.Any(candidate =>
                candidate.PathIdentityKey == targetIdentityKey
                || FileSystemPathIdentity.IsSameOrInside(
                    targetPath,
                    candidate.Path,
                    targetResolution.Semantics)
                || FileSystemPathIdentity.IsSameOrInside(
                    candidate.Path,
                    targetPath,
                    targetResolution.Semantics))
            || activeBoundaries.Any(boundary =>
                BoundariesOverlap(targetPath, boundary.SourcePath, targetResolution.Semantics)
                || BoundariesOverlap(targetPath, boundary.TargetPath, targetResolution.Semantics));
        if (targetConflict)
        {
            throw new InvalidOperationException("A root folder with that filesystem identity already exists.");
        }

        var audiobooks = await db.Audiobooks
            .Include(audiobook => audiobook.Files)
            .Where(audiobook => audiobook.BasePath != null)
            .ToListAsync(cancellationToken);
        var affected = audiobooks
            .Where(audiobook => FileSystemPathIdentity.IsSameOrInside(
                audiobook.BasePath!,
                root.Path,
                sourceResolution.Semantics))
            .ToList();
        var now = timeProvider.GetUtcNow();
        var nowUtc = now.UtcDateTime;

        if (command.Mode == RootFolderRelocationMode.MetadataOnly)
        {
            var sourcePath = root.Path;
            var sourceCaseSensitivityMode = root.CaseSensitivityMode;
            var skipped = new List<RootFolderRelocationSkippedItem>();
            var completed = 0;
            foreach (var audiobook in affected)
            {
                var sourceBasePath = audiobook.BasePath!;
                try
                {
                    var destinationBasePath = MapTargetPath(
                        sourcePath,
                        targetPath,
                        sourceBasePath,
                        sourceResolution.Semantics,
                        targetResolution.Semantics);
                    AudiobookPathReferenceRewriter.Rewrite(
                        audiobook,
                        sourceBasePath,
                        destinationBasePath,
                        sourceResolution.Semantics,
                        targetResolution.Semantics);
                    completed++;
                }
                catch (InvalidOperationException ex)
                {
                    skipped.Add(new RootFolderRelocationSkippedItem
                    {
                        AudiobookId = audiobook.Id,
                        Reason = ex.Message,
                        CreatedAt = now
                    });
                }
            }

            ApplyRootMetadata(root, command, targetPath, targetResolution, targetIdentityKey);
            if (command.DesiredIsDefault)
            {
                await ClearOtherDefaultsAsync(db, rootFolderId, cancellationToken);
            }

            RootFolderRelocation? metadataRelocation = null;
            if (skipped.Count > 0)
            {
                metadataRelocation = new RootFolderRelocation
                {
                    RootFolderId = root.Id,
                    ActiveRootFolderId = root.Id,
                    SourcePath = sourcePath,
                    SourceCaseSensitivityMode = sourceCaseSensitivityMode,
                    TargetPath = targetPath,
                    Mode = command.Mode,
                    Status = RootFolderRelocationStatus.NeedsAttention,
                    DeleteEmptySource = command.DeleteEmptySource,
                    DesiredName = command.DesiredName.Trim(),
                    DesiredIsDefault = command.DesiredIsDefault,
                    TargetCaseSensitivityMode = command.TargetCaseSensitivityMode,
                    TotalJobs = affected.Count,
                    CompletedJobs = completed,
                    Error = BuildSkippedMetadataError(skipped.Count),
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                };
                foreach (var skippedItem in skipped)
                {
                    metadataRelocation.SkippedItems.Add(skippedItem);
                }

                db.RootFolderRelocations.Add(metadataRelocation);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var metadataResult = new RootFolderPathChangeResult(
                metadataRelocation?.Id,
                root.Id,
                root.Path,
                targetPath,
                metadataRelocation?.Status ?? RootFolderRelocationStatus.Completed,
                affected.Count,
                completed,
                metadataRelocation?.Error);
            if (metadataRelocation != null)
            {
                await BroadcastAsync(metadataResult, cancellationToken);
            }

            return metadataResult;
        }

        var relocation = new RootFolderRelocation
        {
            RootFolderId = root.Id,
            ActiveRootFolderId = root.Id,
            SourcePath = root.Path,
            SourceCaseSensitivityMode = root.CaseSensitivityMode,
            TargetPath = targetPath,
            Mode = command.Mode,
            Status = RootFolderRelocationStatus.Pending,
            DeleteEmptySource = command.DeleteEmptySource,
            DesiredName = command.DesiredName.Trim(),
            DesiredIsDefault = command.DesiredIsDefault,
            TargetCaseSensitivityMode = command.TargetCaseSensitivityMode,
            TotalJobs = affected.Count,
            CreatedAt = nowUtc
        };
        db.RootFolderRelocations.Add(relocation);

        foreach (var audiobook in affected)
        {
            var requestedPath = MapTargetPath(
                root.Path,
                targetPath,
                audiobook.BasePath!,
                sourceResolution.Semantics,
                targetResolution.Semantics);
            db.MoveJobs.Add(new MoveJob
            {
                AudiobookId = audiobook.Id,
                RequestedPath = requestedPath,
                SourcePath = audiobook.BasePath,
                DeleteEmptySource = command.DeleteEmptySource,
                Status = MoveJobStatus.Queued,
                Phase = MoveJobPhase.None,
                EnqueuedAt = nowUtc,
                RelocationId = relocation.Id,
                IdentityKeyVersion = 2,
                ActiveDeduplicationKey = FileSystemPathIdentity.CreateKey(
                    $"move:{audiobook.Id}",
                    requestedPath,
                    targetResolution.Semantics)
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        if (affected.Count == 0)
        {
            ApplyRootMetadata(root, command, targetPath, targetResolution, targetIdentityKey);
            relocation.Status = RootFolderRelocationStatus.Completed;
            relocation.ActiveRootFolderId = null;
            relocation.CompletedAt = nowUtc;
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        var result = Map(relocation, root.Path);
        await BroadcastAsync(result, cancellationToken);
        return result;
    }
    public async Task<RootFolderPathChangeResult?> GetAsync(
        Guid relocationId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var relocation = await db.RootFolderRelocations
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == relocationId, cancellationToken);
        if (relocation == null) return null;
        var rootPath = await db.RootFolders
            .Where(root => root.Id == relocation.RootFolderId)
            .Select(root => root.Path)
            .SingleAsync(cancellationToken);
        return Map(relocation, rootPath);
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
            .Select(relocation => new { relocation.SourcePath, relocation.TargetPath })
            .ToListAsync(cancellationToken);
        return boundaries.Any(boundary =>
            BoundariesOverlap(path, boundary.SourcePath, semantics)
            || BoundariesOverlap(path, boundary.TargetPath, semantics));
    }

    public async Task<RootFolderPathChangeResult> RetryAsync(
        Guid relocationId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var relocation = await db.RootFolderRelocations
            .Include(candidate => candidate.MoveJobs)
            .Include(candidate => candidate.SkippedItems)
            .SingleOrDefaultAsync(candidate => candidate.Id == relocationId, cancellationToken)
            ?? throw new KeyNotFoundException("Root folder relocation not found");
        if (relocation.Status != RootFolderRelocationStatus.NeedsAttention)
        {
            throw new InvalidOperationException("Only relocations needing attention can be retried.");
        }

        var targetResolution = await semanticsResolver.ResolveAsync(
            relocation.TargetPath,
            relocation.TargetCaseSensitivityMode,
            cancellationToken);
        if (targetResolution.State != PathIdentityState.Valid)
        {
            throw new InvalidOperationException(
                targetResolution.Reason ?? "Target filesystem identity is unavailable.");
        }

        var skippedSupersededJobs = 0;
        foreach (var job in relocation.MoveJobs.Where(job => job.Status is
            MoveJobStatus.NeedsAttention or MoveJobStatus.Failed or MoveJobStatus.Superseded))
        {
            var deduplicationKey = FileSystemPathIdentity.CreateKey(
                $"move:{job.AudiobookId}",
                job.RequestedPath!,
                targetResolution.Semantics);
            var conflictingJob = await db.MoveJobs.AsNoTracking().FirstOrDefaultAsync(
                candidate => candidate.Id != job.Id
                    && candidate.ActiveDeduplicationKey == deduplicationKey,
                cancellationToken);
            if (conflictingJob != null)
            {
                if (job.Status == MoveJobStatus.Superseded)
                {
                    skippedSupersededJobs++;
                    continue;
                }

                throw new ApplicationConflictException(
                    "move_job_retry_conflict",
                    "A newer move for this audiobook is already active.");
            }

            job.Status = MoveJobStatus.Queued;
            job.Error = null;
            job.FailureKind = MoveFailureKind.None;
            job.NextAttemptAt = null;
            job.ActiveDeduplicationKey = deduplicationKey;
        }

        if (relocation.SkippedItems.Count > 0)
        {
            await RetrySkippedMetadataReferencesAsync(
                db,
                relocation,
                targetResolution.Semantics,
                cancellationToken);
        }

        var remainingSkippedItems = relocation.SkippedItems.Count;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (remainingSkippedItems > 0 || skippedSupersededJobs > 0)
        {
            relocation.Status = RootFolderRelocationStatus.NeedsAttention;
            relocation.Error = BuildRetryAttentionError(remainingSkippedItems, skippedSupersededJobs);
        }
        else if (relocation.MoveJobs.Count == 0
            || relocation.MoveJobs.All(job => job.Status == MoveJobStatus.Completed))
        {
            relocation.Status = RootFolderRelocationStatus.Completed;
            relocation.ActiveRootFolderId = null;
            relocation.CompletedJobs = relocation.TotalJobs;
            relocation.CompletedAt = now;
            relocation.Error = null;
        }
        else
        {
            relocation.Status = RootFolderRelocationStatus.Running;
            relocation.Error = null;
        }

        relocation.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var rootPath = await db.RootFolders
            .Where(root => root.Id == relocation.RootFolderId)
            .Select(root => root.Path)
            .SingleAsync(cancellationToken);
        var result = Map(relocation, rootPath);
        await BroadcastAsync(result, cancellationToken);
        return result;
    }

    public async Task OnMoveJobStateChangedAsync(
        Guid moveJobId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var job = await db.MoveJobs.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == moveJobId,
            cancellationToken);
        if (job?.RelocationId == null) return;
        var relocation = await db.RootFolderRelocations
            .Include(candidate => candidate.MoveJobs)
            .SingleAsync(candidate => candidate.Id == job.RelocationId, cancellationToken);
        var root = await db.RootFolders.SingleAsync(
            candidate => candidate.Id == relocation.RootFolderId,
            cancellationToken);
        relocation.CompletedJobs = relocation.MoveJobs.Count(candidate => candidate.Status == MoveJobStatus.Completed);
        relocation.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (relocation.MoveJobs.Any(candidate => candidate.Status is
            MoveJobStatus.NeedsAttention or MoveJobStatus.Failed or MoveJobStatus.Superseded))
        {
            relocation.Status = RootFolderRelocationStatus.NeedsAttention;
            relocation.Error = relocation.MoveJobs
                .First(candidate => candidate.Status is
                    MoveJobStatus.NeedsAttention or MoveJobStatus.Failed or MoveJobStatus.Superseded)
                .Error
                ?? "A relocation move job was superseded during queue reconciliation.";
        }
        else if (relocation.MoveJobs.All(candidate => candidate.Status == MoveJobStatus.Completed))
        {
            var resolution = await semanticsResolver.ResolveAsync(
                relocation.TargetPath,
                relocation.TargetCaseSensitivityMode,
                cancellationToken);
            if (resolution.State != PathIdentityState.Valid)
            {
                relocation.Status = RootFolderRelocationStatus.NeedsAttention;
                relocation.Error = "Target filesystem identity became unavailable during finalization.";
            }
            else
            {
                var command = new RootFolderPathChangeCommand(
                    relocation.TargetPath,
                    relocation.Mode,
                    relocation.DeleteEmptySource,
                    relocation.DesiredName,
                    relocation.DesiredIsDefault,
                    relocation.TargetCaseSensitivityMode);
                ApplyRootMetadata(
                    root,
                    command,
                    relocation.TargetPath,
                    resolution,
                    FileSystemPathIdentity.CreateKey("root", relocation.TargetPath, resolution.Semantics));
                if (relocation.DesiredIsDefault)
                {
                    await ClearOtherDefaultsAsync(db, root.Id, cancellationToken);
                }

                relocation.Status = RootFolderRelocationStatus.Completed;
                relocation.ActiveRootFolderId = null;
                relocation.CompletedAt = relocation.UpdatedAt;
                relocation.Error = null;
            }
        }
        else
        {
            relocation.Status = RootFolderRelocationStatus.Running;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await BroadcastAsync(Map(relocation, root.Path), cancellationToken);
    }

    public async Task ReconcileActiveAsync(CancellationToken cancellationToken = default)
    {
        await ReconcileRootIdentitiesAsync(cancellationToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var activeRelocationIds = await db.RootFolderRelocations
            .Where(relocation => relocation.ActiveRootFolderId != null)
            .Select(relocation => relocation.Id)
            .ToListAsync(cancellationToken);
        var terminalJobs = await db.MoveJobs
            .Where(job => job.RelocationId != null
                && activeRelocationIds.Contains(job.RelocationId.Value))
            .Where(job => job.Status == MoveJobStatus.Completed
                || job.Status == MoveJobStatus.NeedsAttention
                || job.Status == MoveJobStatus.Failed
                || job.Status == MoveJobStatus.Superseded)
            .OrderByDescending(job => job.UpdatedAt)
            .ToListAsync(cancellationToken);
        var terminalJobIds = terminalJobs
            .GroupBy(job => job.RelocationId)
            .Select(group => group.First().Id);
        foreach (var jobId in terminalJobIds)
        {
            await OnMoveJobStateChangedAsync(jobId, cancellationToken);
        }
    }

    private async Task ReconcileRootIdentitiesAsync(CancellationToken cancellationToken)
    {
        if (_rootIdentitiesReconciled) return;
        await _rootIdentityGate.WaitAsync(cancellationToken);
        try
        {
            if (_rootIdentitiesReconciled) return;
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var roots = await db.RootFolders.ToListAsync(cancellationToken);
            foreach (var root in roots) root.PathIdentityKey = null;
            await db.SaveChangesAsync(cancellationToken);

            var resolvedRoots = new List<(RootFolder Root, string Key)>();
            foreach (var root in roots)
            {
                var resolution = await semanticsResolver.ResolveAsync(
                    root.Path,
                    root.CaseSensitivityMode,
                    cancellationToken);
                root.ResolvedCaseSensitivity = resolution.Semantics.CaseSensitivity;
                root.PathIdentityState = resolution.State;
                if (resolution.State == PathIdentityState.Valid)
                {
                    resolvedRoots.Add((
                        root,
                        FileSystemPathIdentity.CreateKey("root", root.Path, resolution.Semantics)));
                }
            }

            foreach (var group in resolvedRoots.GroupBy(item => item.Key, StringComparer.Ordinal))
            {
                if (group.Count() == 1)
                {
                    var item = group.Single();
                    item.Root.PathIdentityKey = item.Key;
                    item.Root.PathIdentityState = PathIdentityState.Valid;
                    continue;
                }

                foreach (var item in group)
                {
                    item.Root.PathIdentityState = PathIdentityState.Conflict;
                    item.Root.PathIdentityKey = null;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _rootIdentitiesReconciled = true;
        }
        finally
        {
            _rootIdentityGate.Release();
        }
    }

}
