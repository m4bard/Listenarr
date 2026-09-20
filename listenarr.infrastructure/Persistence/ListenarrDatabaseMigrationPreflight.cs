using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence;

internal static class ListenarrDatabaseMigrationPreflight
{
    internal const string DurableFilesystemRecoveryMigrationId =
        "20260810160602_AddDurableFilesystemRecovery";
    internal const string RootFoldersMigrationId =
        "20260101172733_AddRootFolders";
    internal const string QualityProfileUpgradeAllowedMigrationId =
        "20260920025621_AddQualityProfileUpgradeAllowed";

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
        if (!applied.Contains(DurableFilesystemRecoveryMigrationId))
        {
            return default;
        }

        using var transaction = context.Database.BeginTransaction();
        var upgradeFlagsRepaired = RepairQualityProfileUpgradeFlags(context, applied);
        var moveJobsRepaired = context.Database.ExecuteSqlRaw(
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

        return new ListenarrDatabasePostMigrationRepairResult(
            moveJobsRepaired,
            upgradeFlagsRepaired);
    }

    /// <summary>
    /// Turns upgrades off on every profile that was recording "do not upgrade" the only way the
    /// old schema allowed, by leaving the cutoff blank.
    /// </summary>
    /// <remarks>
    /// The column arrives defaulted to 1 so that profiles which DO name a cutoff keep upgrading,
    /// which is what they have always done, and this turns it back off for the blank ones. That
    /// direction is the one that can be run repeatedly without doing harm: it only ever touches a
    /// profile whose cutoff is blank, so a profile saved with upgrades off and a real cutoff, a
    /// state that only became expressible with this column, is never disturbed. The reverse
    /// backfill would have had to switch upgrades ON from a blank column, and rerunning that would
    /// undo the user's own choice.
    ///
    /// A profile with upgrades on and a blank cutoff cannot be saved through the API, because
    /// ValidCutoffAttribute refuses it, and every reader in the engine already treats a blank
    /// cutoff as "not upgrading" (QualityMatcher.ResolveCutoff). Writing that agreement into the
    /// row rather than leaving the two fields contradicting each other is the point.
    /// </remarks>
    private static int RepairQualityProfileUpgradeFlags(
        ListenArrDbContext context,
        HashSet<string> appliedMigrations)
    {
        if (!appliedMigrations.Contains(QualityProfileUpgradeAllowedMigrationId))
        {
            return 0;
        }

        return context.Database.ExecuteSqlRaw(
            """
            UPDATE "QualityProfiles"
            SET "UpgradeAllowed" = 0
            WHERE "UpgradeAllowed" <> 0
              AND ("CutoffQuality" IS NULL OR trim("CutoffQuality") = '');
            """);
    }
}

internal readonly record struct ListenarrDatabaseMigrationPreflightResult(
    int DefaultRootsNormalized);

internal readonly record struct ListenarrDatabasePostMigrationRepairResult(
    int MoveJobsRepaired,
    int QualityProfileUpgradeFlagsRepaired);
