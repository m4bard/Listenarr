using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence;

internal static class ListenarrDatabaseMigrationPreflight
{
    internal const string DurableFilesystemRecoveryMigrationId =
        "20260810160602_AddDurableFilesystemRecovery";
    internal const string RootFoldersMigrationId =
        "20260101172733_AddRootFolders";
    internal const string HousekeepingRetentionMigrationId =
        "20260922220833_AddHousekeepingRetention";

    public static ListenarrDatabaseMigrationPreflightResult RepairLegacyData(
        ListenArrDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var applied = context.Database.GetAppliedMigrations()
            .ToHashSet(StringComparer.Ordinal);
        var normalizeDefaultRoots =
            applied.Contains(RootFoldersMigrationId)
            && !applied.Contains(DurableFilesystemRecoveryMigrationId);
        if (!normalizeDefaultRoots)
        {
            return default;
        }

        using var transaction = context.Database.BeginTransaction();
        var defaultRootsNormalized = context.Database.ExecuteSqlRaw(
            """
            UPDATE "RootFolders"
            SET "IsDefault" = 0
            WHERE "IsDefault" = 1
              AND "Id" <> (
                  SELECT MIN("Id")
                  FROM "RootFolders"
                  WHERE "IsDefault" = 1
              );
            """);
        transaction.Commit();

        return new ListenarrDatabaseMigrationPreflightResult(defaultRootsNormalized);
    }

    public static ListenarrDatabasePostMigrationRepairResult RepairPostMigrationData(
        ListenArrDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var applied = context.Database.GetAppliedMigrations()
            .ToHashSet(StringComparer.Ordinal);
        var moveJobsRepaired = 0;
        var terminalTimestampsBackfilled = 0;

        if (applied.Contains(DurableFilesystemRecoveryMigrationId))
        {
            using var transaction = context.Database.BeginTransaction();
            moveJobsRepaired = context.Database.ExecuteSqlRaw(
                """
                UPDATE "MoveJobs"
                SET
                    "Status" = 'NeedsAttention',
                    "Error" = 'This move job was created by a pre-durable released version and cannot be resumed safely after upgrade.',
                    "FailureKind" = 'Verification',
                    "ActiveDeduplicationKey" = NULL,
                    "UpdatedAt" = CURRENT_TIMESTAMP
                WHERE "ExecutionProtocolVersion" = 0
                  AND "Status" NOT IN ('Completed', 'Failed')
                  AND (
                      "Status" <> 'NeedsAttention'
                      OR "ActiveDeduplicationKey" IS NOT NULL
                      OR "FailureKind" <> 'Verification'
                      OR "Error" IS NULL
                  );
                """);
            transaction.Commit();
        }

        if (applied.Contains(HousekeepingRetentionMigrationId))
        {
            // Without this, every move job that was already terminal when the upgrade ran would
            // carry a null CompletedAt, the retention sweep would never match one of them, and it
            // would do nothing at all for exactly the installs that need it. UpdatedAt is the
            // better estimate and is what almost every terminal transition stamps; EnqueuedAt is
            // the fallback for the two reconciliation paths that reach a terminal status without
            // stamping it, and it is non-nullable. Falling back to EnqueuedAt ages a row from
            // when it was queued rather than from when it finished, so such a row becomes
            // eligible slightly early; what keeps it safe is the sweep's relocation and handoff
            // clauses rather than this timestamp.
            //
            // It runs on every start rather than once, and is idempotent by the IS NULL guard.
            // That is deliberate: a row written terminal by a build that predates the stamping
            // sites, on an install that upgraded through several versions, is repaired the next
            // time the application comes up instead of staying immortal.
            using var transaction = context.Database.BeginTransaction();
            terminalTimestampsBackfilled = context.Database.ExecuteSqlRaw(
                """
                UPDATE "MoveJobs"
                SET "CompletedAt" = COALESCE("UpdatedAt", "EnqueuedAt")
                WHERE "Status" IN ('Completed', 'Superseded')
                  AND "CompletedAt" IS NULL;
                """);
            transaction.Commit();
        }

        return new ListenarrDatabasePostMigrationRepairResult(
            moveJobsRepaired,
            terminalTimestampsBackfilled);
    }
}

internal readonly record struct ListenarrDatabaseMigrationPreflightResult(
    int DefaultRootsNormalized);

internal readonly record struct ListenarrDatabasePostMigrationRepairResult(
    int MoveJobsRepaired,
    int MoveJobTerminalTimestampsBackfilled);
