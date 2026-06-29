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

namespace Listenarr.Infrastructure.DownloadClients.Nzbget
{
    internal sealed class NzbgetRemovalWorkflow
    {
        private readonly NzbgetXmlRpcClient _xmlRpcClient;
        private readonly ILogger _logger;

        public NzbgetRemovalWorkflow(NzbgetXmlRpcClient xmlRpcClient, ILogger logger)
        {
            _xmlRpcClient = xmlRpcClient;
            _logger = logger;
        }

        public async Task<bool> RemoveAsync(DownloadClientConfiguration client, string id, bool deleteFiles = false, CancellationToken ct = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            var numericId = NzbgetRequestPlanner.TryParseId(id);
            if (!numericId.HasValue)
            {
                // Listenarr stores NZBGet's numeric queue/history ID. Legacy drone IDs are intentionally
                // unsupported because the submission flow no longer emits or persists them.
                _logger.LogWarning(
                    "NZBGet removal skipped because item id {ItemId} is not a numeric NZBGet id",
                    LogRedaction.SanitizeText(id));
                return false;
            }

            // Try to remove from history first (for completed downloads)
            try
            {
                var historyCommand = deleteFiles ? "HistoryFinalDelete" : "HistoryDelete";
                var historyDeleteResult = await _xmlRpcClient.CallAsync(client, "editqueue", historyCommand, 0, string.Empty, new[] { numericId.Value });
                var historySuccess = historyDeleteResult.Element("boolean")?.Value == "1";

                if (historySuccess)
                {
                    _logger.LogInformation("Removed NZB {Id} from NZBGet history (deleteFiles={DeleteFiles})", LogRedaction.SanitizeText(id), deleteFiles);
                    return true;
                }
            }
            catch (Exception histEx) when (histEx is not OperationCanceledException && histEx is not OutOfMemoryException && histEx is not StackOverflowException)
            {
                _logger.LogDebug(histEx, "Could not remove {Id} from NZBGet history (may not be in history)", LogRedaction.SanitizeText(id));
            }

            // Fall back to queue removal (for active downloads)
            try
            {
                var command = deleteFiles ? "GroupFinalDelete" : "GroupDelete";
                var editResult = await _xmlRpcClient.CallAsync(client, "editqueue", command, 0, string.Empty, new[] { numericId.Value });
                var success = editResult.Element("boolean")?.Value == "1";

                if (success)
                {
                    _logger.LogInformation("Removed NZB {Id} from NZBGet queue (deleteFiles={DeleteFiles})", LogRedaction.SanitizeText(id), deleteFiles);
                    return true;
                }

                _logger.LogWarning("NZBGet reported failure when removing {Id} from both history and queue", LogRedaction.SanitizeText(id));
                return false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Error removing NZB {Id} from NZBGet", LogRedaction.SanitizeText(id));
                return false;
            }
        }
    }
}
