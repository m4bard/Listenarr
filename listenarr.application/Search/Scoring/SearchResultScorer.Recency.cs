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
    // The profile's "Prefer newer releases" setting, which was saved and read by nothing.
    //
    // It is a ranking preference and nothing else. It is not related to the profile's
    // MaximumAge, which is a hard reject applied in SearchResultScorer.Gates.cs whatever this
    // setting says, and this file must not start reading MaximumAge either.
    public partial class SearchResultScorer
    {
        /// <summary>The bonus a release published today earns when the profile prefers newer releases.</summary>
        /// <remarks>
        /// Four is deliberately below the smallest explicit preference the operator can express:
        /// one preferred word, the format match bonus and the release shape match are each worth
        /// five. No difference in this bonus between two releases can make up one of those, so it
        /// only separates releases that are otherwise comparable. The unconditional age penalty
        /// in Score() is separate and applies whatever this setting says.
        /// </remarks>
        public int NewerReleaseMaxBonus { get; set; } = 4;

        /// <summary>The age in days at which the newer-release bonus has faded to nothing.</summary>
        public int NewerReleaseHorizonDays { get; set; } = 365;

        /// <summary>
        /// Adds the newer-release bonus to a release that has already been accepted.
        /// </summary>
        /// <remarks>
        /// Score() calls this after the MinimumScore check and the score &lt;= 0 rejection, so the
        /// bonus can reorder accepted releases but never turns a rejected one into an accept.
        /// </remarks>
        private void ApplyNewerReleaseBonus(SearchResult searchResult, QualityProfile profile, QualityScore score)
        {
            if (!profile.PreferNewerReleases)
            {
                return;
            }

            // Some gates record a rejection and keep scoring rather than returning, the disallowed
            // quality among them. Such a release is already lost; a bonus in its breakdown would
            // only mislead whoever reads it.
            if (score.RejectionReasons.Count > 0)
            {
                return;
            }

            var bonus = NewerReleaseBonus(searchResult.PublishedDate, DateTime.UtcNow);
            if (bonus > 0)
            {
                score.TotalScore += bonus;
                score.ScoreBreakdown["NewerRelease"] = bonus;
            }
        }

        /// <summary>
        /// The bonus for a release published at <paramref name="publishedDate"/>: the full
        /// <see cref="NewerReleaseMaxBonus"/> when it is new, falling in whole-point steps over
        /// <see cref="NewerReleaseHorizonDays"/> and reaching nothing exactly at the horizon.
        /// </summary>
        /// <remarks>
        /// A release with no published date, or one that does not parse, earns nothing. The gates
        /// report an age of zero for such a release, which is indistinguishable from one published
        /// this instant, so the date is parsed again here rather than trusting that zero.
        ///
        /// The age is counted in whole days, so a release published earlier today earns the full
        /// bonus. A date in the future is treated as today: it earns the full bonus and no more.
        ///
        /// The value is max * (horizon - age) / horizon rounded up, in integer arithmetic. Rounding
        /// up is what makes the last point last until the horizon itself; rounding down reached
        /// zero weeks early, and doing it in floating point lost a point at some ages.
        /// </remarks>
        internal int NewerReleaseBonus(string? publishedDate, DateTime nowUtc)
        {
            if (NewerReleaseMaxBonus <= 0 || NewerReleaseHorizonDays <= 0)
            {
                return 0;
            }

            if (!TryParsePublishedDateUtc(publishedDate, out var publishedUtc))
            {
                return 0;
            }

            var wholeDaysOld = (nowUtc - publishedUtc).Days;
            var age = Math.Clamp(wholeDaysOld, 0, NewerReleaseHorizonDays);
            var horizon = NewerReleaseHorizonDays;
            return ((NewerReleaseMaxBonus * (horizon - age)) + horizon - 1) / horizon;
        }
    }
}
