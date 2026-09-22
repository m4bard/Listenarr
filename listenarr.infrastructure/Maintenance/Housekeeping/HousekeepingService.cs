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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Maintenance.Housekeeping
{
    /// <summary>
    /// Puts one daily retention sweep over the append-only tables on the task surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named for the family's concept, because a maintainer reading "Housekeeping" knows what it
    /// is without opening it. The interval is the family's too, and deliberately not
    /// configurable: <c>HousekeepingCommand</c> is scheduled at <c>24 * 60</c> minutes in
    /// Readarr (<c>src/NzbDrone.Core/Jobs/TaskManager.cs:107</c>), Sonarr (<c>:108</c>) and
    /// Prowlarr (<c>:86</c>), and <c>TaskManager.cs:147</c> rewrites the stored interval back
    /// from the compiled default on every start, so an operator there cannot change it even by
    /// editing the row.
    /// </para>
    /// <para>
    /// A scheduling adapter and nothing else, shaped on <c>DownloadProcessingJobCleanupService</c>
    /// beside it: the policy lives in the processor so it can be exercised without a host.
    /// </para>
    /// </remarks>
    public sealed class HousekeepingService(
        IHousekeepingProcessor processor,
        IWorkerCycleRunner cycleRunner,
        ILogger<HousekeepingService> logger) : BackgroundService
    {
        /// <summary>
        /// Long enough that a restart loop cannot turn the sweep into a delete loop, and long
        /// enough for the startup reconcilers to have read the journals this prunes before it
        /// considers pruning any of them.
        /// </summary>
        private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(15);

        private static readonly TimeSpan SweepInterval = TimeSpan.FromDays(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Housekeeping worker started");

            // Deliberately not on the manual-run allowlist. This cycle deletes stored rows, and
            // ScheduledTaskManualTrigger says in the doc comment on Allowed that a cycle which
            // deletes or prunes is not one of the ones to put there. Absence is the refusal:
            // RunPeriodicAsync defaults to ScheduledTaskManualTrigger.Denied.
            await cycleRunner.RunPeriodicAsync(
                nameof(HousekeepingService),
                InitialDelay,
                () => SweepInterval,
                processor.RunCycleAsync,
                stoppingToken);

            logger.LogInformation("Housekeeping worker stopped");
        }
    }

    /// <summary>
    /// Runs every registered housekeeper once per cycle, in registration order, and reports what
    /// each one did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Failure isolation.</b> The try/catch is inside the loop, as it is in
    /// <c>src/NzbDrone.Core/Housekeeping/HousekeepingService.cs:26-37</c>, so one housekeeper
    /// throwing costs its own table and not the rest of the sweep. It is logged at ERROR with
    /// the housekeeper's name, which is the part the family gets right and is worth copying
    /// verbatim.
    /// </para>
    /// <para>
    /// <b>Counts at INFO.</b> Readarr logs none of this: <c>HousekeepingService.cs:30</c> and
    /// <c>:32</c> are Debug and name only the housekeeper, and only two of its housekeepers take
    /// a logger at all. An operator deciding whether to turn the preview off has nothing to
    /// decide on unless the preview says how many rows it found, so the counts are INFO here.
    /// </para>
    /// </remarks>
    public sealed class HousekeepingProcessor(
        IEnumerable<IHousekeepingTask> housekeepers,
        HousekeepingOptionsHolder options,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<HousekeepingProcessor> logger) : IHousekeepingProcessor
    {
        /// <summary>
        /// The per-housekeeper ceiling for one cycle.
        /// </summary>
        /// <remarks>
        /// The family has no cap at all, and this is a stated divergence rather than an
        /// oversight: their delete primitive is one statement issued against a database server,
        /// while this one runs against a SQLite file on the same machine that is serving the
        /// application, where a single large write transaction is felt by every reader. The
        /// number is chosen rather than measured. It is high enough that an install which has
        /// never swept drains a six-figure backlog inside a month of daily cycles, and low
        /// enough that one cycle's change tracker and its one transaction stay small. When a
        /// cycle hits it the remainder is simply older next time, and the log line says so.
        /// </remarks>
        internal const int MaxRowsPerTaskPerCycle = 5000;

        private readonly IReadOnlyList<IHousekeepingTask> _housekeepers = [.. housekeepers];

        public async Task RunCycleAsync(CancellationToken cancellationToken = default)
        {
            using var scope = scopeFactory.CreateScope();

            // Every cycle rather than once at start, so an operator who changes the window or
            // turns the preview off does not have to restart the process for it to take effect.
            await HousekeepingOptionsLoader.LoadAsync(
                scope.ServiceProvider,
                options,
                logger,
                cancellationToken);

            var current = options.Current;
            if (current.RetentionIsUnlimited)
            {
                logger.LogInformation(
                    "Housekeeping retention is unlimited; this cycle deleted nothing");
                return;
            }

            if (_housekeepers.Count == 0)
            {
                logger.LogDebug("No housekeepers are registered; this cycle had nothing to sweep");
                return;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;

            logger.LogInformation(
                "Housekeeping sweep starting over {Housekeepers} tables at a configured retention of {RetentionDays} days. This cycle is {Mode}",
                _housekeepers.Count,
                current.RetentionDays,
                current.DryRun ? "a preview that writes nothing" : "deleting");

            foreach (var housekeeper in _housekeepers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // The larger of the operator's window and this table's own floor. One clock
                // reading for the whole sweep, so two housekeepers with the same floor cannot
                // disagree about where the cutoff was because one of them ran later.
                var effectiveRetentionDays = Math.Max(
                    current.RetentionDays,
                    housekeeper.MinimumRetentionDays);
                var cycle = new HousekeepingCycle(
                    now.AddDays(-effectiveRetentionDays),
                    effectiveRetentionDays,
                    current.DryRun,
                    MaxRowsPerTaskPerCycle);

                try
                {
                    var outcome = await housekeeper.RunAsync(cycle, cancellationToken);
                    logger.LogInformation(
                        "Housekeeper {Housekeeper} kept {RetentionDays} days, matched {Matched} rows and {Verb} {Deleted}. Per cycle ceiling reached: {CeilingReached}",
                        housekeeper.Name,
                        cycle.EffectiveRetentionDays,
                        outcome.Matched,
                        current.DryRun ? "would delete" : "deleted",
                        outcome.Deleted,
                        outcome.CeilingReached);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Housekeeper {Housekeeper} failed. The rest of the sweep continues and this table is retried on the next cycle",
                        housekeeper.Name);
                }
            }
        }
    }
}
