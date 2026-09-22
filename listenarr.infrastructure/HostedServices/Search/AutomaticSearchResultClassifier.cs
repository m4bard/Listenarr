/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.HostedServices.Search
{
    internal sealed class AutomaticSearchResultClassifier
    {
        private readonly ILogger _logger;

        public AutomaticSearchResultClassifier(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// The ordered query forms for an audiobook the sweep is trying to find.
        /// </summary>
        /// <remarks>
        /// The series used to be appended to the one query this method produced, which narrowed
        /// every sweep query by a term the indexer's own title may well not carry. It is a rung of
        /// its own in the plan now, reached only when the title forms come back empty.
        /// </remarks>
        public SearchQueryPlan BuildSearchPlan(Audiobook audiobook)
        {
            return AudiobookSearchQueryBuilder.BuildPlan(audiobook);
        }

        /// <summary>
        /// Maps a search result onto the protocol that decides which download client carries it.
        /// Direct downloads have their own internal pipeline; everything else that is not a
        /// torrent is treated as usenet, which is what IsTorrentResult already assumed.
        /// </summary>
        public DownloadProtocol ResolveProtocol(SearchResult result)
        {
            if (string.Equals(result.DownloadType, DirectDownloadMetadataKeys.ClientId, StringComparison.OrdinalIgnoreCase))
            {
                return DownloadProtocol.DirectDownload;
            }

            return IsTorrentResult(result) ? DownloadProtocol.Torrent : DownloadProtocol.Usenet;
        }

        public bool IsTorrentResult(SearchResult result)
        {
            if (!string.IsNullOrEmpty(result.DownloadType))
            {
                if (string.Equals(result.DownloadType, DirectDownloadMetadataKeys.ClientId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                else if (result.DownloadType == "Torrent")
                {
                    return true;
                }
                else if (result.DownloadType == "Usenet")
                {
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(result.NzbUrl))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(result.MagnetLink) || !string.IsNullOrEmpty(result.TorrentUrl))
            {
                return true;
            }

            _logger.LogWarning("Unable to determine result type for '{Title}' from source '{Source}'. No MagnetLink, TorrentUrl, or NzbUrl found. Defaulting to NZB.",
                result.Title, result.Source);
            return false;
        }
    }
}
