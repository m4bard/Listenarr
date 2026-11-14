/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using Listenarr.Application.Search.Discovery;
using Listenarr.Domain.Search.Discovery;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories;

public sealed class EfDiscoveryStore(IDbContextFactory<ListenArrDbContext> dbFactory) : IDiscoveryStore
{
    private static readonly DiscoveredReleaseState[] PrunableStates =
        [DiscoveredReleaseState.Handled, DiscoveredReleaseState.Ignored];

    public async Task<int> CommitPollAsync(
        int indexerId,
        IReadOnlyList<DiscoveredReleaseCandidate> releases,
        DiscoveryCheckpointAdvance? advance,
        DiscoveryCoverageGapSpan? gap,
        int futureDatedDelta,
        int undatedDelta,
        DateTime pollUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(releases);
        await using var db = await CreateRelationalContextAsync(cancellationToken);
        // Disposing the transaction without a commit rolls back, so a failure or a cancellation
        // anywhere below leaves the checkpoint, the inbox and the gaps exactly as they were.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var pending = nameof(DiscoveredReleaseState.Pending);
        var inserted = 0;
        foreach (var release in releases)
        {
            inserted += await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "DiscoveredReleases" ("IndexerId", "ReleaseId", "PublishedUtc", "Title", "Payload", "State", "FirstSeenUtc")
                VALUES ({indexerId}, {release.ReleaseId}, {release.PublishedUtc}, {release.Title}, {release.Payload}, {pending}, {pollUtc})
                ON CONFLICT ("IndexerId", "ReleaseId") DO NOTHING
                """,
                cancellationToken);
        }

        if (advance is null)
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "IndexerDiscoveryCheckpoints" ("IndexerId", "LastPollUtc", "FutureDatedItemCount", "UndatedItemCount")
                VALUES ({indexerId}, {pollUtc}, {futureDatedDelta}, {undatedDelta})
                ON CONFLICT ("IndexerId") DO UPDATE SET
                    "LastPollUtc" = excluded."LastPollUtc",
                    "FutureDatedItemCount" = "FutureDatedItemCount" + excluded."FutureDatedItemCount",
                    "UndatedItemCount" = "UndatedItemCount" + excluded."UndatedItemCount"
                """,
                cancellationToken);
        }
        else
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "IndexerDiscoveryCheckpoints" ("IndexerId", "NewestPublishedUtc", "NewestReleaseId", "LastPollUtc", "LastSuccessUtc", "FutureDatedItemCount", "UndatedItemCount")
                VALUES ({indexerId}, {advance.NewestPublishedUtc}, {advance.NewestReleaseId}, {pollUtc}, {pollUtc}, {futureDatedDelta}, {undatedDelta})
                ON CONFLICT ("IndexerId") DO UPDATE SET
                    "NewestPublishedUtc" = excluded."NewestPublishedUtc",
                    "NewestReleaseId" = excluded."NewestReleaseId",
                    "LastPollUtc" = excluded."LastPollUtc",
                    "LastSuccessUtc" = excluded."LastSuccessUtc",
                    "FutureDatedItemCount" = "FutureDatedItemCount" + excluded."FutureDatedItemCount",
                    "UndatedItemCount" = "UndatedItemCount" + excluded."UndatedItemCount"
                """,
                cancellationToken);
        }

        if (gap is not null)
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "IndexerCoverageGaps" ("IndexerId", "GapStartUtc", "GapEndUtc", "DetectedUtc")
                VALUES ({indexerId}, {gap.GapStartUtc}, {gap.GapEndUtc}, {pollUtc})
                """,
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Every statement has succeeded; a cancellation arriving now must not leave the caller
        // unsure whether the poll was stored.
        await transaction.CommitAsync(CancellationToken.None);
        return inserted;
    }

    public async Task<IReadOnlyList<DiscoveredRelease>> GetBatchAsync(
        DiscoveredReleaseState state,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        await using var db = await CreateRelationalContextAsync(cancellationToken);
        return await db.DiscoveredReleases
            .AsNoTracking()
            .Where(release => release.State == state)
            .OrderBy(release => release.FirstSeenUtc)
            .ThenBy(release => release.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryTransitionAsync(
        int releaseId,
        DiscoveredReleaseState expected,
        DiscoveredReleaseState next,
        string? outcome,
        int? audiobookId,
        DateTime utc,
        CancellationToken cancellationToken)
    {
        await using var db = await CreateRelationalContextAsync(cancellationToken);
        var changed = await db.DiscoveredReleases
            .Where(release => release.Id == releaseId && release.State == expected)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(release => release.State, next)
                    .SetProperty(release => release.Outcome, outcome)
                    .SetProperty(release => release.MatchedAudiobookId, audiobookId)
                    .SetProperty(release => release.HandledUtc, utc),
                cancellationToken);
        return changed == 1;
    }

    public async Task<int> PruneAsync(
        DateTime olderThanUtc,
        int limitPerState,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limitPerState);
        await using var db = await CreateRelationalContextAsync(cancellationToken);
        var deleted = 0;
        foreach (var state in PrunableStates)
        {
            deleted += await db.DiscoveredReleases
                .Where(release => release.State == state && release.FirstSeenUtc < olderThanUtc)
                .OrderBy(release => release.FirstSeenUtc)
                .ThenBy(release => release.Id)
                .Take(limitPerState)
                .ExecuteDeleteAsync(cancellationToken);
        }

        return deleted;
    }

    public async Task<DiscoveryIndexerStatus> GetStatusAsync(
        int indexerId,
        int gapLimit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gapLimit);
        await using var db = await CreateRelationalContextAsync(cancellationToken);
        var checkpoint = await db.IndexerDiscoveryCheckpoints
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.IndexerId == indexerId, cancellationToken);
        if (gapLimit == 0)
        {
            return new DiscoveryIndexerStatus(checkpoint, []);
        }

        var openGaps = await db.IndexerCoverageGaps
            .AsNoTracking()
            .Where(gap => gap.IndexerId == indexerId && gap.AcknowledgedUtc == null)
            .OrderByDescending(gap => gap.DetectedUtc)
            .ThenByDescending(gap => gap.Id)
            .Take(gapLimit)
            .ToListAsync(cancellationToken);
        return new DiscoveryIndexerStatus(checkpoint, openGaps);
    }

    public async Task<bool> AcknowledgeGapAsync(
        int gapId,
        DateTime utc,
        CancellationToken cancellationToken)
    {
        await using var db = await CreateRelationalContextAsync(cancellationToken);
        var acknowledged = await db.IndexerCoverageGaps
            .Where(gap => gap.Id == gapId && gap.AcknowledgedUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(gap => gap.AcknowledgedUtc, utc), cancellationToken);
        return acknowledged > 0
            || await db.IndexerCoverageGaps.AnyAsync(gap => gap.Id == gapId, cancellationToken);
    }

    private async Task<ListenArrDbContext> CreateRelationalContextAsync(CancellationToken cancellationToken)
    {
        var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (db.Database.IsRelational())
        {
            return db;
        }

        await db.DisposeAsync();
        throw new InvalidOperationException(
            "Recent-release discovery storage needs a relational database; the configured database provider is not relational.");
    }
}
