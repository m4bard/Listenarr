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
namespace Listenarr.Domain.Common
{
    /// <summary>
    /// The order scored releases are considered in, so that every caller that picks one release
    /// out of a search picks the same one.
    /// </summary>
    /// <remarks>
    /// The operator's quality profile decides first, and the accumulated preference score decides
    /// between releases the profile ranks equally. Quality is never converted into points and
    /// added to that score: doing so would need a number of points per rung that nothing
    /// determines, and it would change what an operator's configured minimum score means.
    /// </remarks>
    public static class ReleaseRanking
    {
        /// <summary>A release the profile cannot rank sorts after every release it can.</summary>
        public const int Unrankable = int.MaxValue;

        /// <summary>The profile rung priority a release ranks on. Lower is better.</summary>
        public static int RungPriority(string? qualityLabel, QualityProfile? profile)
            => QualityMatcher.RankingRung(qualityLabel, profile)?.Priority ?? Unrankable;

        /// <summary>
        /// Scored releases in the order they should be considered: accepted before rejected, then
        /// by the profile's own quality ordering, then by the accumulated score.
        /// </summary>
        public static IOrderedEnumerable<QualityScore> InPreferenceOrder(
            this IEnumerable<QualityScore> scores,
            QualityProfile? profile)
            => scores
                .OrderBy(score => score.IsRejected)
                .ThenBy(score => RungPriority(score.SearchResult?.Quality, profile))
                .ThenByDescending(score => score.TotalScore);
    }
}
