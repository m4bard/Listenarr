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

using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Submission
{
    public enum RequestedBookMatch
    {
        Accepted,
        TitleMismatch,
        AuthorMismatch,
        AuthorNotCorroborated
    }

    public static class RequestedBookReleaseFilter
    {
        public static RequestedBookMatch Evaluate(Audiobook audiobook, SearchResult release)
        {
            return RequestedBookMatch.Accepted;
        }

        public static List<QualityScore> Exclude(Audiobook audiobook, List<QualityScore> scoredResults, ILogger logger)
        {
            return scoredResults;
        }
    }
}
