/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Application.Downloads.Queue
{
    internal static class DownloadSearchQueryBuilder
    {
        /// <summary>
        /// The ordered query forms for an audiobook the download path is trying to find.
        /// </summary>
        /// <remarks>
        /// Forwarded to <see cref="AudiobookSearchQueryBuilder"/> rather than built here, because
        /// this path and the automatic sweep used to build their own strings and disagreed about
        /// what belongs in one. Two entry points must not describe the same audiobook differently.
        /// </remarks>
        public static SearchQueryPlan BuildPlan(Audiobook audiobook)
        {
            return AudiobookSearchQueryBuilder.BuildPlan(audiobook);
        }
    }
}
