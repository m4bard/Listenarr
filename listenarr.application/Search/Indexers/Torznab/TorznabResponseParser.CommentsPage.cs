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

namespace Listenarr.Application.Search.Indexers.Torznab
{
    internal sealed partial class TorznabResponseParser
    {
        // Sends one comments-page request through the gate. Returns the page, or the deferral that kept
        // the request from being sent; both are null when the page did not load.
        private async Task<(string? Html, RequestDeferral? Deferral)> FetchCommentsPageAsync(
            Uri commentsPageUrl,
            Indexer indexer,
            CancellationToken cancellationToken)
        {
            var requestContext = new IndexerRequestContext(indexer.Id, SearchOrigin.Interactive, IndexerRequestKind.Enrichment);
            var sendResult = await IndexerHttpDispatch.SendFollowingRedirectsAsync(
                _requestGate, requestContext, _httpClient, uri => new HttpRequestMessage(HttpMethod.Get, uri),
                commentsPageUrl, _logger, onDispatch: null, cancellationToken);
            if (sendResult is IndexerRequestResult.Deferred deferred)
            {
                _logger.LogDebug("Comments page request for {IndexerName} deferred: {Reason}", indexer.Name, deferred.Deferral.Reason);
                return (null, deferred.Deferral);
            }

            using var response = ((IndexerRequestResult.Sent)sendResult).Response;
            var html = response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : null;
            return (html, null);
        }
    }
}
