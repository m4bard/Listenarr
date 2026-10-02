/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.DownloadClients.Transmission
{
    internal sealed class TransmissionItemFetchWorkflow(
        TransmissionRpcClient rpcClient,
        ILogger<TransmissionAdapter> logger)
    {
        public async Task<List<DownloadClientItem>> GetItemsAsync(DownloadClientConfiguration client, CancellationToken ct = default)
        {
            var items = new List<DownloadClientItem>();
            if (client == null) return items;

            var configuredCategory = DownloadClientCategoryFilter.GetConfiguredCategory(client);
            var sessionConfig = await TransmissionSessionConfigReader.ReadAsync(rpcClient, client, logger, ct);

            var payload = new
            {
                method = "torrent-get",
                arguments = new
                {
                    fields = new[]
                    {
                        "id", "hashString", "name", "percentDone", "status", "totalSize", "rateDownload", "rateUpload",
                        "leftUntilDone", "eta", "downloadDir", "addedDate", "uploadedEver", "uploadRatio", "labels",
                        "seedRatioMode", "seedRatioLimit", "seedIdleMode", "seedIdleLimit", "secondsSeeding"
                    }
                },
                tag = 3
            };

            try
            {
                var response = await rpcClient.InvokeAsync(client, payload, ct);
                if (!response.TryGetProperty("arguments", out var args) || !args.TryGetProperty("torrents", out var torrents) || torrents.ValueKind != JsonValueKind.Array)
                {
                    return items;
                }

                foreach (var torrent in torrents.EnumerateArray())
                {
                    try
                    {
                        var labels = TransmissionResponseMapper.ExtractLabels(torrent);
                        if (!DownloadClientCategoryFilter.MatchesAny(configuredCategory, labels))
                        {
                            continue;
                        }

                        items.Add(TransmissionResponseMapper.MapDownloadClientItem(client, torrent, sessionConfig));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                    {
                        logger.LogDebug(ex, "Failed to map Transmission torrent entry (non-fatal)");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(ex, "Failed to retrieve Transmission items for client {ClientName}", LogRedaction.SanitizeText(client.Name ?? client.Id));
            }

            return items;
        }
    }
}
