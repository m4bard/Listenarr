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
using Listenarr.Application.Metadata.Refresh;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Authors
{
    /// <summary>
    /// Re-resolves the author identities already written to the database and corrects the ones
    /// that belong to somebody else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stopping the binding that wrote them does nothing about the rows already there, and on an
    /// install that has been running a while those are the larger half. Two stores hold them:
    /// AuthorCacheEntries, written whenever a name lookup fell through, and MonitoredAuthors,
    /// written from the same resolution when an author is monitored. The stranger's ASIN brings
    /// their biography and their portrait with it, so all three are corrected or cleared
    /// together.
    /// </para>
    /// <para>
    /// <b>It must not read its own output.</b> This is the thing most likely to make the pass
    /// useless, and it is not hypothetical: the normal lookup path seeds itself from the
    /// persisted row before it asks anybody anything, and it does so whether or not a refresh was
    /// requested, so a wrong ASIN confirms itself forever and no amount of refreshing dislodges
    /// it. The pass therefore goes to the providers directly. It never calls the author lookup,
    /// the author catalogue, or any cache read, and it never offers the stored ASIN to the
    /// matcher as an identifier it holds, because "accept a row carrying the ASIN we already
    /// have" is precisely how a stranger would confirm itself here. The only thing carried from
    /// the stored row into the resolution is the name.
    /// </para>
    /// <para>
    /// Clearing is a correct outcome, not a failure. Callers already treat an author with no ASIN
    /// as one to skip, and no identity beats somebody else's: the name still displays, the books
    /// are still there, and the next thing that resolves that author properly fills it in.
    /// </para>
    /// <para>
    /// What the family does with stored data that has gone wrong is Readarr's Housekeeping, a
    /// daily sweep of small repairs, one of which (UpdateCleanTitleForAuthor) rewrites a derived
    /// author field in place with no preview and no switch. The difference here, and the reason
    /// for the preview, is that every Readarr housekeeper is a local recomputation from data
    /// already held, and this one asks a provider who somebody is.
    /// </para>
    /// </remarks>
    public sealed class AuthorIdentityRepairService : IAuthorIdentityRepairService
    {
        private readonly IAudiobookRepository _audiobookRepository;
        private readonly IMonitoredAuthorRepository _monitoredAuthorRepository;
        private readonly AudibleService _audibleService;
        private readonly IAudnexusService _audnexusService;
        private readonly IMetadataRefreshCoordinator _refreshCoordinator;
        private readonly AuthorIdentityRepairOptionsHolder _optionsHolder;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<AuthorIdentityRepairService> _logger;

        private const string AuthorCacheStore = "AuthorCacheEntries";
        private const string MonitoredAuthorStore = "MonitoredAuthors";

        public AuthorIdentityRepairService(
            IAudiobookRepository audiobookRepository,
            IMonitoredAuthorRepository monitoredAuthorRepository,
            AudibleService audibleService,
            IAudnexusService audnexusService,
            IMetadataRefreshCoordinator refreshCoordinator,
            AuthorIdentityRepairOptionsHolder optionsHolder,
            TimeProvider timeProvider,
            ILogger<AuthorIdentityRepairService> logger)
        {
            _audiobookRepository = audiobookRepository;
            _monitoredAuthorRepository = monitoredAuthorRepository;
            _audibleService = audibleService;
            _audnexusService = audnexusService;
            _refreshCoordinator = refreshCoordinator;
            _optionsHolder = optionsHolder;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task<AuthorIdentityRepairReport> RunAsync(CancellationToken cancellationToken)
        {
            var options = _optionsHolder.Current;
            if (!options.Enabled)
            {
                return AuthorIdentityRepairReport.Nothing(options.DryRun);
            }

            var ceiling = Math.Max(1, options.MaxRowsPerRun);

            // The window this run may keep waiting for a provider slot in. One interval, so a run
            // that cannot finish inside its own period gives up rather than still holding budget
            // when the next one is due.
            var budget = _refreshCoordinator.LeaseBudget(TimeSpan.FromHours(Math.Max(1, options.IntervalHours)));
            var checkedAt = _timeProvider.GetUtcNow().UtcDateTime;

            var decisions = new List<AuthorIdentityDecision>();
            var budgetExhausted = false;

            // A row is due again once its last check is this old. The cutoff is what makes the
            // queue finite; without it a library with more cached authors than the ceiling would
            // re-ask about the same rows every run and never reach the rest.
            var checkedBefore = checkedAt.AddDays(-Math.Max(0, options.RecheckAfterDays));

            // The two stores share one budget, so they share one ceiling. They do not share it
            // first-come: the cached table is the larger of the two by orders of magnitude and
            // would take the whole ceiling on every run, and the monitored rows -- the ones an
            // operator actually looks at -- would never be examined at all. So the monitored
            // store is guaranteed its share, and whatever it does not use goes to the cache.
            var monitoredShare = Math.Max(1, ceiling / 4);
            var monitored = await MonitoredDueForCheckAsync(checkedBefore, monitoredShare, cancellationToken);
            var cached = await _audiobookRepository.GetAuthorCacheEntriesDueForIdentityCheckAsync(
                checkedBefore,
                ceiling - monitored.Count,
                cancellationToken);

            foreach (var row in cached)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var resolution = await ResolveAsync(row.AuthorName, row.Region, budget, cancellationToken);
                if (!resolution.Asked)
                {
                    budgetExhausted = true;
                    break;
                }

                var verdict = Decide(row.AuthorAsin, resolution);
                decisions.Add(new AuthorIdentityDecision(
                    AuthorCacheStore,
                    row.Id,
                    row.AuthorName,
                    row.Region,
                    row.AuthorAsin,
                    resolution.Asin,
                    verdict));

                if (options.DryRun)
                {
                    continue;
                }

                // A row that was already right, and a row nothing could be concluded about, are
                // both stamped and not written. The first because it needs no repair; the second
                // because the pass has to keep moving, and the dangerous operation is the write
                // rather than the cursor.
                if (verdict is AuthorIdentityVerdict.AlreadyCorrect or AuthorIdentityVerdict.Unresolved)
                {
                    await _audiobookRepository.StampAuthorCacheIdentityCheckedAsync(
                        row.Id,
                        checkedAt,
                        cancellationToken);
                    continue;
                }

                // A correction carries the biography and the portrait with it. They arrived with
                // the wrong ASIN and they describe the wrong person, so leaving them behind would
                // fix the identifier and keep the visible half of the damage.
                await _audiobookRepository.ApplyAuthorCacheIdentityAsync(
                    row.Id,
                    resolution.Asin,
                    resolution.Description,
                    resolution.Image,
                    checkedAt,
                    cancellationToken);
            }

            if (!budgetExhausted)
            {
                foreach (var row in monitored)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var resolution = await ResolveAsync(row.AuthorName, row.Region, budget, cancellationToken);
                    if (!resolution.Asked)
                    {
                        budgetExhausted = true;
                        break;
                    }

                    var verdict = Decide(row.AuthorAsin, resolution);
                    decisions.Add(new AuthorIdentityDecision(
                        MonitoredAuthorStore,
                        row.Id,
                        row.AuthorName,
                        row.Region,
                        row.AuthorAsin,
                        resolution.Asin,
                        verdict));

                    if (options.DryRun)
                    {
                        continue;
                    }

                    if (verdict is AuthorIdentityVerdict.Corrected or AuthorIdentityVerdict.Cleared)
                    {
                        row.AuthorAsin = resolution.Asin;
                        row.UpdatedAt = checkedAt;
                    }

                    row.AuthorIdentityCheckedAt = checkedAt;
                    await _monitoredAuthorRepository.UpsertAsync(row, cancellationToken);
                }
            }

            // The stored credits, which is a different repair sharing this pass's schedule and
            // its preview switch and nothing else. It asks the provider nothing, so it is not
            // bounded by the budget and does not stop when that runs out: a run that could not
            // afford to check a single identity can still spell the credits it already holds
            // correctly. It keeps the same per-run ceiling anyway, so one cycle cannot rewrite
            // the whole library's bylines in a single transaction.
            var credits = await _audiobookRepository.CleanRoleSuffixesFromStoredAuthorsAsync(
                ceiling,
                apply: !options.DryRun,
                cancellationToken);

            foreach (var change in credits.Changes)
            {
                _logger.LogInformation(
                    "{Mode} audiobook {AudiobookId} credits: [{Before}] becomes [{After}]",
                    options.DryRun ? "Would clean" : "Cleaned",
                    change.AudiobookId,
                    LogRedaction.SanitizeText(string.Join(" // ", change.Before)),
                    LogRedaction.SanitizeText(string.Join(" // ", change.After)));
            }

            var report = new AuthorIdentityRepairReport(
                options.DryRun,
                decisions.Count,
                decisions.Count(decision => decision.Verdict == AuthorIdentityVerdict.AlreadyCorrect),
                decisions.Count(decision => decision.Verdict == AuthorIdentityVerdict.Corrected),
                decisions.Count(decision => decision.Verdict == AuthorIdentityVerdict.Cleared),
                decisions.Count(decision => decision.Verdict == AuthorIdentityVerdict.Unresolved),
                budgetExhausted,
                decisions)
            {
                CreditsCleaned = credits.Changes.Count
            };

            Report(report);
            return report;
        }

        /// <summary>
        /// Monitored rows carrying an ASIN, least recently checked first. Read whole and ordered
        /// here rather than in SQL: this table holds one row per author an operator has chosen to
        /// follow, so it is small by construction, and a second bespoke query is not worth the
        /// two repositories drifting apart.
        /// </summary>
        private async Task<List<MonitoredAuthor>> MonitoredDueForCheckAsync(
            DateTime checkedBefore,
            int limit,
            CancellationToken cancellationToken)
        {
            var all = await _monitoredAuthorRepository.GetAllAsync(cancellationToken);
            return all
                .Where(row => !string.IsNullOrWhiteSpace(row.AuthorAsin))
                .Where(row => row.AuthorIdentityCheckedAt == null
                    || row.AuthorIdentityCheckedAt < checkedBefore)
                .OrderBy(row => row.AuthorIdentityCheckedAt.HasValue)
                .ThenBy(row => row.AuthorIdentityCheckedAt)
                .ThenBy(row => row.Id)
                .Take(limit)
                .ToList();
        }

        private static AuthorIdentityVerdict Decide(string? storedAsin, AuthorIdentityResolution resolution)
        {
            if (!resolution.Answered)
            {
                return AuthorIdentityVerdict.Unresolved;
            }

            if (string.IsNullOrWhiteSpace(resolution.Asin))
            {
                // The provider answered, named this person, and carries no identifier for them,
                // so whatever is stored under the name did not come from asking about them.
                return AuthorIdentityVerdict.Cleared;
            }

            return string.Equals(storedAsin, resolution.Asin, StringComparison.OrdinalIgnoreCase)
                ? AuthorIdentityVerdict.AlreadyCorrect
                : AuthorIdentityVerdict.Corrected;
        }

        /// <summary>
        /// What the providers say this name's identity is, asked from scratch.
        /// </summary>
        /// <remarks>
        /// Audible first, because a contributor record with an ASIN on it is the provider naming
        /// the person outright. Audnexus second and only on a name match, which is the same rule
        /// the live lookup now applies. The stored ASIN is deliberately not passed as a held
        /// identifier: here it is the thing under suspicion, and offering it to the matcher would
        /// let a stranger's row vouch for itself.
        /// </remarks>
        private async Task<AuthorIdentityResolution> ResolveAsync(
            string name,
            string region,
            IMetadataRefreshBudget budget,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return AuthorIdentityResolution.Inconclusive;
            }

            if (!await budget.ChargeAsync(cancellationToken))
            {
                return AuthorIdentityResolution.NotAsked;
            }

            AuthorLookupItem? audible;
            try
            {
                audible = await _audibleService.LookupAuthorAsync(name, region);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(
                    ex,
                    "Author identity repair could not ask Audible about '{Author}'",
                    LogRedaction.SanitizeText(name));
                return AuthorIdentityResolution.NotAsked;
            }

            if (!string.IsNullOrWhiteSpace(audible?.Asin))
            {
                return AuthorIdentityResolution.Is(audible!.Asin, audible.Description, audible.Image);
            }

            // Null is not the same answer as an item with no ASIN, and telling them apart is what
            // stops this pass doing damage during a provider outage.
            //
            // An item naming the author with no ASIN is Audible saying, positively, that it
            // credits this person and holds no identifier for them. A null is the ambiguous one:
            // the lookup returns null when the search found no products at all AND when it could
            // not reach the provider, because the failure is swallowed below this call and an
            // unreachable Audible yields an empty candidate list. Clearing on that would mean an
            // Audible outage wiping the ASIN off every correct row the pass reached, on the
            // strength of a question nobody answered.
            //
            // So a null ends the examination here, inconclusively. The row is stamped so the pass
            // still makes progress, and nothing on it is written.
            if (audible == null)
            {
                _logger.LogDebug(
                    "Audible returned nothing for '{Author}', which does not distinguish an unknown author from an unreachable provider; leaving the row alone",
                    LogRedaction.SanitizeText(name));
                return AuthorIdentityResolution.Inconclusive;
            }

            if (!await budget.ChargeAsync(cancellationToken))
            {
                return AuthorIdentityResolution.NotAsked;
            }

            try
            {
                var candidates = await _audnexusService.SearchAuthorsAsync(name, region);
                if (candidates == null)
                {
                    _logger.LogDebug(
                        "Audnexus did not answer for '{Author}'; leaving the row alone",
                        LogRedaction.SanitizeText(name));
                    return AuthorIdentityResolution.Inconclusive;
                }

                var matched = AudnexusAuthorIdentity.Select(candidates, name);

                // Both providers answered and neither names an identifier for this author, which
                // is an answer rather than a failure to find one.
                return AuthorIdentityResolution.Is(matched?.Asin, matched?.Description, matched?.Image);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(
                    ex,
                    "Author identity repair could not ask Audnexus about '{Author}'",
                    LogRedaction.SanitizeText(name));
                return AuthorIdentityResolution.NotAsked;
            }
        }

        private void Report(AuthorIdentityRepairReport report)
        {
            foreach (var decision in report.Decisions.Where(d => d.Verdict != AuthorIdentityVerdict.AlreadyCorrect))
            {
                _logger.LogInformation(
                    "{Mode} {Store} row {RowId}: '{Author}' ({Region}) holds {StoredAsin}, the provider says {ResolvedAsin} ({Verdict})",
                    report.DryRun ? "Would change" : "Changed",
                    decision.Store,
                    decision.RowId,
                    LogRedaction.SanitizeText(decision.AuthorName),
                    LogRedaction.SanitizeText(decision.Region),
                    LogRedaction.SanitizeText(decision.StoredAsin ?? "no ASIN"),
                    LogRedaction.SanitizeText(decision.ResolvedAsin ?? "no ASIN"),
                    decision.Verdict);
            }

            _logger.LogInformation(
                "Author identity repair {Mode}: examined {Examined}, already correct {Correct}, corrected {Corrected}, cleared {Cleared}{Truncated}",
                report.DryRun ? "preview (nothing was written)" : "pass",
                report.Examined,
                report.AlreadyCorrect,
                report.Corrected,
                report.Cleared,
                report.BudgetExhausted ? ", stopped early on the request budget" : string.Empty);

            if (report.CreditsCleaned > 0)
            {
                _logger.LogInformation(
                    "Author identity repair {Mode} contributor roles from {Count} book(s) stored credits",
                    report.DryRun ? "would remove" : "removed",
                    report.CreditsCleaned);
            }
        }

        /// <summary>
        /// What one resolution attempt came back with, in three states rather than two.
        /// </summary>
        /// <remarks>
        /// <see cref="Asked"/> false means the attempt did not happen: out of budget, or a
        /// provider threw. The run stops, and the row is not stamped, so it stays at the head of
        /// the queue.
        ///
        /// <see cref="Answered"/> false with <see cref="Asked"/> true means it happened and
        /// settled nothing. The row is stamped so the pass keeps moving and nothing on it is
        /// written; it comes round again on the next sweep.
        ///
        /// Both true is a conclusion, and a null <see cref="Asin"/> is a real one: this author
        /// has no identifier.
        /// </remarks>
        private readonly record struct AuthorIdentityResolution(
            bool Asked,
            bool Answered,
            string? Asin,
            string? Description,
            string? Image)
        {
            public static AuthorIdentityResolution NotAsked { get; } = new(false, false, null, null, null);

            public static AuthorIdentityResolution Inconclusive { get; } = new(true, false, null, null, null);

            public static AuthorIdentityResolution Is(string? asin, string? description, string? image) =>
                new(true, true, asin, description, image);
        }
    }
}
