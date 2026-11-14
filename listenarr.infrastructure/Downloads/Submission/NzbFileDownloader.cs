/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Downloads.Submission;

public sealed class NzbFileDownloader(
    IHttpClientFactory httpClientFactory,
    IIndexerRepository indexerRepository,
    IIndexerRequestGate requestGate,
    ILogger<NzbFileDownloader> logger) : INzbFileDownloader
{
    public async Task<byte[]> DownloadAsync(
        string url,
        int? indexerId,
        CancellationToken cancellationToken = default)
    {
        var resolvedUrl = url;
        if (indexerId is int id)
        {
            var indexer = await indexerRepository.GetByIdAsync(id, cancellationToken);
            if (indexer != null &&
                !string.IsNullOrWhiteSpace(indexer.ApiKey) &&
                !url.Contains("apikey=", StringComparison.OrdinalIgnoreCase))
            {
                resolvedUrl = QueryHelpers.AddQueryString(url, "apikey", indexer.ApiKey);
            }
        }

        if (!OutboundRequestSecurity.TryValidateExternalHttpUrl(
                resolvedUrl,
                out var reason,
                allowPrivateTargets: true))
        {
            throw new DownloadClientSubmissionException($"The NZB URL was rejected: {reason}");
        }

        // Every hop is one gate call and one wire request: the Indexer client neither retries nor
        // follows redirects itself. A grab has no search origin; it is sent as an attended request.
        var context = new IndexerRequestContext(
            indexerId ?? 0,
            SearchOrigin.Interactive,
            IndexerRequestKind.DownloadFetch);
        var sendResult = await IndexerHttpDispatch.SendFollowingRedirectsAsync(
            requestGate,
            context,
            httpClientFactory.CreateClient(IndexerHttpDispatch.ClientName),
            uri =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("Listenarr/1.0");
                return request;
            },
            new Uri(resolvedUrl),
            logger,
            onDispatch: null,
            cancellationToken,
            redirectKind: IndexerRequestKind.DownloadFetch);
        if (sendResult is IndexerRequestResult.Deferred deferred)
        {
            throw new IndexerRequestDeferredException(deferred.Deferral);
        }

        using var response = ((IndexerRequestResult.Sent)sendResult).Response;
        if (!response.IsSuccessStatusCode)
        {
            throw new DownloadClientSubmissionException(
                $"NZB metadata download failed with HTTP {(int)response.StatusCode}.");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
        {
            throw new DownloadClientSubmissionException("The NZB metadata was empty.");
        }

        return bytes;
    }
}
