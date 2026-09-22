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

namespace Listenarr.Application.Common.Housekeeping
{
    /// <summary>The operator's housekeeping settings, as one value.</summary>
    /// <remarks>
    /// <para>
    /// <b>Why a window at all, and why this one.</b> Exactly one operator-configurable window
    /// bounds a database table anywhere in the family: Prowlarr's <c>HistoryCleanupDays</c>,
    /// declared with a default of 30 at <c>src/NzbDrone.Core/Configuration/ConfigService.cs:80</c>
    /// and pinned by <c>src/NzbDrone.Core.Test/Configuration/ConfigServiceFixture.cs:34</c>. Its
    /// zero disables cleanup outright, with the early return at
    /// <c>src/NzbDrone.Core/History/HistoryService.cs:105-110</c> and the shipped help text "Set
    /// to 0 to disable automatic cleanup" at <c>src/NzbDrone.Core/Localization/Core/en.json:322</c>.
    /// This codebase already reads zero the same way: <c>DownloadHistoryService</c> returns zero
    /// and logs "Download history retention is unlimited" when its retention is zero. So the
    /// default is 30 and zero means unlimited, because that is what both the family and this
    /// codebase already mean by those numbers.
    /// </para>
    /// <para>
    /// <b>Why the preview ships on.</b> The family gives no precedent either way. It has no dry
    /// run, no batch cap and no chunking anywhere: its delete primitive at
    /// <c>src/NzbDrone.Core/Datastore/BasicRepository.cs:260-273</c> is one statement over the
    /// whole matching set. But what the family prunes is logs, command rows and orphans, and
    /// what this prunes is durable journals that the startup reconcilers read. That is a
    /// different risk, so an upgraded install lands in the state that writes nothing and an
    /// operator turns it off once they have read a cycle's counts.
    /// </para>
    /// </remarks>
    public sealed record HousekeepingOptions(int RetentionDays, bool DryRun)
    {
        /// <summary>The retention value that means "keep everything".</summary>
        public const int UnlimitedRetentionDays = 0;

        public static HousekeepingOptions Shipped { get; } =
            new(RetentionDays: 30, DryRun: true);

        /// <summary>True when no row is old enough to be eligible, whatever its age.</summary>
        public bool RetentionIsUnlimited => RetentionDays <= UnlimitedRetentionDays;
    }

    /// <summary>
    /// The options the sweep is using right now, swapped wholesale as settings are reloaded.
    /// </summary>
    /// <remarks>
    /// The same holder shape the other periodic passes use. The value is replaced rather than
    /// mutated so a cycle already in flight keeps reading a consistent pair, and in particular
    /// cannot observe the window from one save beside the preview switch from another.
    /// </remarks>
    public sealed class HousekeepingOptionsHolder
    {
        private HousekeepingOptions _current = HousekeepingOptions.Shipped;

        public HousekeepingOptions Current
        {
            get => Volatile.Read(ref _current);
            set => Volatile.Write(ref _current, value);
        }
    }

    /// <summary>One housekeeper's instructions for one sweep.</summary>
    /// <param name="CutoffUtc">
    /// Rows whose own last movement is strictly before this instant are eligible. Age is
    /// measured from the row's own timestamp rather than from when the sweep reached this
    /// table, so a long sweep cannot prune a row that moved while it ran.
    /// </param>
    /// <param name="EffectiveRetentionDays">
    /// The window this cutoff came from, for the log line. It is the operator's setting or this
    /// housekeeper's own floor, whichever is longer.
    /// </param>
    /// <param name="DryRun">When true a housekeeper counts and reports but writes nothing.</param>
    /// <param name="MaxRowsPerTask">The per-housekeeper ceiling for this one cycle.</param>
    public sealed record HousekeepingCycle(
        DateTime CutoffUtc,
        int EffectiveRetentionDays,
        bool DryRun,
        int MaxRowsPerTask);

    /// <summary>What one housekeeper did, or would have done.</summary>
    /// <param name="Matched">Rows the predicate selected, before the ceiling was applied.</param>
    /// <param name="Deleted">
    /// Rows removed, or in a preview the rows that would have been removed. Never larger than
    /// the cycle's ceiling.
    /// </param>
    /// <param name="CeilingReached">
    /// True when more rows matched than the ceiling allowed, so a later cycle has more to do.
    /// </param>
    public sealed record HousekeepingTaskOutcome(int Matched, int Deleted, bool CeilingReached)
    {
        public static HousekeepingTaskOutcome Nothing { get; } =
            new(Matched: 0, Deleted: 0, CeilingReached: false);
    }

    /// <summary>Retention for one table.</summary>
    /// <remarks>
    /// One implementation per table, each registered by name in the container rather than found
    /// by an assembly scan. The family scans (<c>src/NzbDrone.Common/Composition/Extensions.cs:27-29</c>),
    /// which leaves the execution order of its thirty-odd housekeepers undeclared; this container
    /// registers every other worker explicitly, and a list a reviewer can read is worth more here
    /// than the convenience of not writing one.
    /// </remarks>
    public interface IHousekeepingTask
    {
        /// <summary>What this housekeeper is called in the log lines it produces.</summary>
        string Name { get; }

        /// <summary>
        /// The shortest window this table can safely be swept on, whatever the operator set.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The effective window is the larger of this and the operator's setting, so raising
        /// the setting raises every table and lowering it cannot take a table below what its
        /// own readers need. Zero retention still disables the whole sweep, because that switch
        /// is read before any housekeeper is asked anything.
        /// </para>
        /// <para>
        /// A per-table window is the family's shape rather than a departure from it. Readarr,
        /// Sonarr and Prowlarr hardcode one window per housekeeper (seven days for log rows at
        /// <c>src/NzbDrone.Core/Instrumentation/LogRepository.cs:21</c>, one day for command rows
        /// at <c>src/NzbDrone.Core/Messaging/Commands/CommandRepository.cs:27</c>, fourteen for
        /// pending releases at
        /// <c>src/NzbDrone.Core/Housekeeping/Housekeepers/CleanupDownloadClientUnavailablePendingReleases.cs:28</c>)
        /// and expose exactly one configurable window between the three of them. This keeps the
        /// one setting and treats the per-table numbers as floors rather than as the answer.
        /// </para>
        /// </remarks>
        int MinimumRetentionDays { get; }

        /// <summary>
        /// Runs this housekeeper for one cycle. It reports rather than returning void because
        /// the counts are the point: Readarr logs none of this, and an operator deciding whether
        /// to switch the preview off has nothing else to go on.
        /// </summary>
        Task<HousekeepingTaskOutcome> RunAsync(
            HousekeepingCycle cycle,
            CancellationToken cancellationToken);
    }

    /// <summary>The sweep's cycle body, kept out of the hosted service so it can be tested without a host.</summary>
    public interface IHousekeepingProcessor
    {
        Task RunCycleAsync(CancellationToken cancellationToken = default);
    }
}
