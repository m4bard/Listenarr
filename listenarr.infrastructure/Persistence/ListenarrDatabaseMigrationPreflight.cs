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

        // Each repair carries its own gate. The move-job repair used to own this method and its
        // gate sat here, which would have made the quality profile backfill depend on a migration
        // it has nothing to do with: a later squash or rename of that one would have stopped this
        // one running, silently and with no test to notice.
        var upgradeFlagsRepaired = RepairQualityProfileUpgradeFlags(context, applied);
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
            // when it was queued rather than from when it finished, so a job that sat queued a
            // long time and finished recently can become eligible the day the install upgrades.
            // Nothing prevents that: an ordinary single-audiobook move has no relocation and a
            // Succeeded handoff, so neither of the sweep's other clauses holds it back. The cost
            // is losing a recent move job history row early, the population is one-time, and the
            // preview default means an operator sees the count before anything is deleted, which
            // is why this is accepted rather than guarded against.
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
            terminalTimestampsBackfilled,
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
    ///
    /// "Blank" is decided in C#, by the same string.IsNullOrWhiteSpace the two readers use
    /// (listenarr.domain/Common/QualityMatcher.cs:290 and
    /// listenarr.domain/Audiobooks/ValidCutoffAttribute.cs:94). Expressing it in SQL does not
    /// work: SQLite's one-argument trim() strips U+0020 and nothing else, so a cutoff of a single
    /// tab or newline reads as blank to every part of the engine and as non-blank to the backfill,
    /// and that row would come out of the upgrade with upgrades switched ON, which is the exact
    /// inversion this is here to prevent. The table holds a handful of rows and only the two
    /// columns this needs are read.
    ///
    /// Note that rolling this migration back and reapplying it re-derives the flag from the
    /// cutoff, so a profile saved as upgrades-off while still naming a cutoff comes back as
    /// upgrades-on. Down() cannot carry the flag into the cutoff itself, because post-canary
    /// migrations have to stay direct EF scaffolds
    /// (tests/Features/Architecture/MigrationProvenanceArchitectureTests.cs:60-66).
    /// </remarks>
    private static int RepairQualityProfileUpgradeFlags(
        ListenArrDbContext context,
        HashSet<string> appliedMigrations)
    {
        if (!appliedMigrations.Contains(QualityProfileUpgradeAllowedMigrationId))
        {
            return 0;
        }

        // Projected rather than materialised: the entity's Qualities column goes through a JSON
        // value converter, and one malformed row must not turn a backfill into a failed start.
        var contradictoryIds = context.QualityProfiles
            .Where(profile => profile.UpgradeAllowed)
            .Select(profile => new { profile.Id, profile.CutoffQuality })
            .ToList()
            .Where(row => string.IsNullOrWhiteSpace(row.CutoffQuality))
            .Select(row => row.Id)
            .ToList();

        if (contradictoryIds.Count == 0)
        {
            return 0;
        }

        return context.QualityProfiles
            .Where(profile => contradictoryIds.Contains(profile.Id))
            .ExecuteUpdate(setters => setters.SetProperty(
                profile => profile.UpgradeAllowed,
                false));
    }

    /// <summary>
    /// Clears author ASINs that more than one distinct monitored author name claims within the
    /// same region.
    /// </summary>
    /// <remarks>
    /// Name-to-ASIN resolution used to fall back on a book's author ASIN bag, which carries no
    /// association between a credited name and an identifier, so every co-author on a book could
    /// end up stamped with one of them. The resolution path no longer does that, but the stored
    /// values survive: the sync path only ever overwrites an ASIN with a newly resolved one and
    /// never clears a stale value, and the catalog cache hands its stored ASIN back without
    /// re-asking the metadata source, so a bad identifier would be written straight back.
    ///
    /// Which of the colliding names actually owns the ASIN is not decidable from local data, so
    /// the value is dropped for all of them and each name is resolved again on its own. Two names
    /// for one person (a pen name credited alongside a legal name, for instance) legitimately
    /// share an ASIN and will be cleared and re-resolved here as well. That is deliberate: a
    /// re-resolved identifier costs one lookup, whereas keeping an identifier that is wrong for
    /// every row but one silently misattributes an author.
    /// </remarks>
    public static ListenarrAmbiguousAuthorAsinRepairResult RepairAmbiguousAuthorAsins(
        ListenArrDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var monitoredAuthors = context.MonitoredAuthors
            .Where(author => author.AuthorAsin != null && author.AuthorAsin != string.Empty)
            .ToList();

        var ambiguousAsins = monitoredAuthors
            .GroupBy(BuildAuthorAsinScope)
            .Where(scope => scope
                .Select(author => NormalizeScopeValue(author.AuthorNameNormalized))
                .Distinct(StringComparer.Ordinal)
                .Count() > 1)
            .Select(scope => scope.Key)
            .ToHashSet();

        if (ambiguousAsins.Count == 0)
        {
            return default;
        }

        var now = DateTime.UtcNow;
        var monitoredAuthorsRepaired = 0;
        foreach (var author in monitoredAuthors)
        {
            if (!ambiguousAsins.Contains(BuildAuthorAsinScope(author)))
            {
                continue;
            }

            author.AuthorAsin = null;
            author.UpdatedAt = now;
            monitoredAuthorsRepaired++;
        }

        var cachedAuthorsRepaired = 0;
        var cachedAuthors = context.AuthorCacheEntries
            .Where(entry => entry.AuthorAsin != null && entry.AuthorAsin != string.Empty)
            .ToList();
        foreach (var entry in cachedAuthors)
        {
            var scope = new AuthorAsinScope(
                NormalizeScopeValue(entry.Region),
                NormalizeAsinScopeValue(entry.AuthorAsin));
            if (!ambiguousAsins.Contains(scope))
            {
                continue;
            }

            entry.AuthorAsin = null;
            entry.UpdatedAt = now;
            cachedAuthorsRepaired++;
        }

        context.SaveChanges();

        return new ListenarrAmbiguousAuthorAsinRepairResult(
            monitoredAuthorsRepaired,
            cachedAuthorsRepaired);
    }

    private static AuthorAsinScope BuildAuthorAsinScope(MonitoredAuthor author) =>
        new(NormalizeScopeValue(author.Region), NormalizeAsinScopeValue(author.AuthorAsin));

    private static string NormalizeScopeValue(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string NormalizeAsinScopeValue(string? value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();
}

internal readonly record struct AuthorAsinScope(string Region, string Asin);

internal readonly record struct ListenarrDatabaseMigrationPreflightResult(
    int DefaultRootsNormalized);

internal readonly record struct ListenarrDatabasePostMigrationRepairResult(
    int MoveJobsRepaired,
    int MoveJobTerminalTimestampsBackfilled,
    int QualityProfileUpgradeFlagsRepaired);

internal readonly record struct ListenarrAmbiguousAuthorAsinRepairResult(
    int MonitoredAuthorsRepaired,
    int CachedAuthorsRepaired);
