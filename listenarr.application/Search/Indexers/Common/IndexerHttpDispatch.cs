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

namespace Listenarr.Application.Search.Indexers.Common;

/// <summary>
/// Sends indexer requests through the request gate. This is the one place on indexer paths that
/// hands a request to an <see cref="HttpClient"/>, so every wire request is one gate call.
/// </summary>
public static class IndexerHttpDispatch
{
    /// <summary>
    /// The named HTTP client for indexer requests. It never follows redirects itself, so each
    /// redirect hop reaches the gate as its own request.
    /// </summary>
    public const string ClientName = "Indexer";

    /// <summary>
    /// Sends one request through the gate, without following redirects.
    /// </summary>
    /// <param name="gate">The request gate.</param>
    /// <param name="context">The indexer, origin, kind and optional cycle allowance.</param>
    /// <param name="client">The client that puts the request on the wire.</param>
    /// <param name="request">The request to send.</param>
    /// <param name="onDispatch">Called when the gate dispatches the request, or null.</param>
    /// <param name="cancellationToken">The caller's own token.</param>
    /// <returns>The response, or the deferral that kept the request from being sent.</returns>
    public static Task<IndexerRequestResult> SendAsync(
        IIndexerRequestGate gate,
        IndexerRequestContext context,
        HttpClient client,
        HttpRequestMessage request,
        Action? onDispatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);

        return gate.SendAsync(
            context,
            token =>
            {
                onDispatch?.Invoke();
                return client.SendAsync(request, token);
            },
            cancellationToken);
    }

    /// <summary>
    /// Sends a request and follows its redirects one hop at a time. Every hop is one gate call:
    /// the first carries the context's kind, each followed hop is
    /// <see cref="IndexerRequestKind.Redirect"/>. Every target is validated before it is sent, and
    /// private targets are allowed, as they are for every indexer request.
    /// </summary>
    /// <param name="gate">The request gate.</param>
    /// <param name="context">The context of the first send.</param>
    /// <param name="client">A client that does not follow redirects itself.</param>
    /// <param name="requestFactory">Builds the request for each hop's target.</param>
    /// <param name="initialUri">The first target.</param>
    /// <param name="logger">Receives the address validation warnings.</param>
    /// <param name="onDispatch">Called each time the gate dispatches a hop, or null.</param>
    /// <param name="cancellationToken">The caller's own token.</param>
    /// <returns>The final response, or the deferral that kept a hop from being sent.</returns>
    /// <exception cref="InvalidOperationException">A target failed validation.</exception>
    /// <exception cref="HttpRequestException">A redirect had no target, or there were too many.</exception>
    public static async Task<IndexerRequestResult> SendFollowingRedirectsAsync(
        IIndexerRequestGate gate,
        IndexerRequestContext context,
        HttpClient client,
        Func<Uri, HttpRequestMessage> requestFactory,
        Uri initialUri,
        ILogger logger,
        Action? onDispatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        RequestDeferral? deferral = null;
        var (response, _) = await OutboundRequestSecurity.SendWithValidatedRedirectsAsync(
            requestFactory,
            initialUri,
            async (hopRequest, hopIndex, token) =>
            {
                var hopContext = hopIndex == 0 ? context : context with { Kind = IndexerRequestKind.Redirect };
                var result = await SendAsync(gate, hopContext, client, hopRequest, onDispatch, token);
                if (result is IndexerRequestResult.Deferred deferred)
                {
                    deferral = deferred.Deferral;
                    return null;
                }

                return ((IndexerRequestResult.Sent)result).Response;
            },
            logger,
            allowPrivateTargets: true,
            cancellationToken: cancellationToken);

        return response != null
            ? new IndexerRequestResult.Sent(response)
            : new IndexerRequestResult.Deferred(deferral!.Value);
    }
}
