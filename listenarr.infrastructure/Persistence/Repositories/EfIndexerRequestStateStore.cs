using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories;

/// <summary>
/// Stores the request gate's per-indexer state. The gate is a singleton, so every call opens
/// its own short-lived context from the factory.
/// </summary>
public sealed class EfIndexerRequestStateStore(
    IDbContextFactory<ListenArrDbContext> dbContextFactory) : IIndexerRequestStateStore
{
    public async Task<IReadOnlyList<IndexerRequestState>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await context.IndexerRequestStates
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(
        IndexerRequestState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        // One statement, so a first write for an indexer can never race another first write
        // into a duplicate-key failure the way a read followed by an insert could.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "IndexerRequestStates"
                ("IndexerId", "ConsecutiveFailures", "CooldownUntilUtc", "CooldownReason", "LastDispatchUtc", "UpdatedUtc")
            VALUES ({state.IndexerId}, {state.ConsecutiveFailures}, {state.CooldownUntilUtc}, {state.CooldownReason}, {state.LastDispatchUtc}, {state.UpdatedUtc})
            ON CONFLICT ("IndexerId") DO UPDATE SET
                "ConsecutiveFailures" = excluded."ConsecutiveFailures",
                "CooldownUntilUtc" = excluded."CooldownUntilUtc",
                "CooldownReason" = excluded."CooldownReason",
                "LastDispatchUtc" = excluded."LastDispatchUtc",
                "UpdatedUtc" = excluded."UpdatedUtc"
            """,
            cancellationToken);
    }

    public async Task ClearConsecutiveFailuresAsync(
        int indexerId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await context.IndexerRequestStates
            .Where(state => state.IndexerId == indexerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(state => state.ConsecutiveFailures, 0)
                    .SetProperty(state => state.UpdatedUtc, nowUtc),
                cancellationToken);
    }
}
