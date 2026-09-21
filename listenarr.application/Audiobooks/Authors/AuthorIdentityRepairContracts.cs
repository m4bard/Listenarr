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

namespace Listenarr.Application.Audiobooks.Authors
{
    /// <summary>The operator's settings for the identity repair pass, as one value.</summary>
    public sealed record AuthorIdentityRepairOptions(
        bool Enabled,
        bool DryRun,
        int IntervalHours,
        int MaxRowsPerRun,
        int RecheckAfterDays)
    {
        public static AuthorIdentityRepairOptions Shipped { get; } =
            new(Enabled: false, DryRun: true, IntervalHours: 24, MaxRowsPerRun: 25, RecheckAfterDays: 30);
    }

    /// <summary>
    /// The options the pass is using right now, swapped wholesale as settings are reloaded.
    /// </summary>
    /// <remarks>
    /// The same holder shape the metadata walk uses, and for the same reason: the interval is
    /// read by the scheduler on every tick while the rest is read by a cycle already running, so
    /// the value has to be replaceable without tearing.
    /// </remarks>
    public sealed class AuthorIdentityRepairOptionsHolder
    {
        private AuthorIdentityRepairOptions _current = AuthorIdentityRepairOptions.Shipped;

        public AuthorIdentityRepairOptions Current
        {
            get => Volatile.Read(ref _current);
            set => Volatile.Write(ref _current, value);
        }
    }

    /// <summary>What the pass decided about one stored row.</summary>
    public enum AuthorIdentityVerdict
    {
        /// <summary>The stored ASIN is this author's. Nothing to do.</summary>
        AlreadyCorrect = 0,

        /// <summary>The provider names a different ASIN for this author.</summary>
        Corrected = 1,

        /// <summary>
        /// The provider has no ASIN for this author, so the stored one is somebody else's and
        /// the row is better off with none.
        /// </summary>
        Cleared = 2,

        /// <summary>The provider could not be asked, so nothing was concluded.</summary>
        Unresolved = 3
    }

    /// <summary>One row's decision, whether or not it was written.</summary>
    public sealed record AuthorIdentityDecision(
        string Store,
        int RowId,
        string AuthorName,
        string Region,
        string? StoredAsin,
        string? ResolvedAsin,
        AuthorIdentityVerdict Verdict);

    /// <summary>What one pass did, or would have done.</summary>
    public sealed record AuthorIdentityRepairReport(
        bool DryRun,
        int Examined,
        int AlreadyCorrect,
        int Corrected,
        int Cleared,
        int Unresolved,
        bool BudgetExhausted,
        IReadOnlyList<AuthorIdentityDecision> Decisions)
    {
        /// <summary>
        /// Books whose stored author credits named a contributor role and had it removed, or
        /// would have. Counted apart from the ASIN work because it is a different repair with a
        /// different cost: it asks the provider nothing and it cannot be wrong about who
        /// somebody is, only about how their credit was spelled.
        /// </summary>
        public int CreditsCleaned { get; init; }

        public static AuthorIdentityRepairReport Nothing(bool dryRun) =>
            new(dryRun, 0, 0, 0, 0, 0, false, Array.Empty<AuthorIdentityDecision>());

        /// <summary>Rows a repair run wrote, or a preview run would have written.</summary>
        public int Changed => Corrected + Cleared + CreditsCleaned;
    }

    public interface IAuthorIdentityRepairService
    {
        /// <summary>
        /// Examines one bounded batch of stored author rows and, unless previewing, corrects
        /// them. Safe to call again at any point: the work is a function of what is stored, and
        /// a row that is already right is stamped rather than rewritten.
        /// </summary>
        Task<AuthorIdentityRepairReport> RunAsync(CancellationToken cancellationToken);
    }
}
