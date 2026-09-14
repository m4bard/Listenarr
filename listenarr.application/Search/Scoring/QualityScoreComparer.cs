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

namespace Listenarr.Application.Search.Scoring
{
    /// <summary>
    /// Orders scored search results for automatic-grab selection the way Readarr's
    /// DownloadDecisionComparer does: rank by the terms that determine whether a release is
    /// actually a good pick (here, <see cref="QualityScore.TotalScore"/>, which already folds in
    /// quality, format, language, seeders and age), and fall back to indexer priority only when
    /// two results are exactly tied on that score. Priority is deliberately never mixed into
    /// TotalScore as an additive term - doing so would let indexer choice outrank a release that
    /// is genuinely better, which is the bug this comparer exists to avoid.
    /// </summary>
    public sealed class QualityScoreComparer : IComparer<QualityScore>
    {
        public static readonly QualityScoreComparer Instance = new();

        public int Compare(QualityScore? x, QualityScore? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var byTotalScore = x.TotalScore.CompareTo(y.TotalScore);
            if (byTotalScore != 0) return byTotalScore;

            // Tie-break only: lower Priority value means higher priority ("lower = higher
            // priority" per Indexer.Priority). A result with no resolvable indexer priority
            // sorts as the lowest priority, so it never wins a tie against a result whose
            // priority is actually known.
            var xPriority = x.IndexerPriority ?? int.MaxValue;
            var yPriority = y.IndexerPriority ?? int.MaxValue;
            return yPriority.CompareTo(xPriority);
        }
    }
}
