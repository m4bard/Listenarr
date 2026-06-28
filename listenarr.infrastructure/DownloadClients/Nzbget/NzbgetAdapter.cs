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
using System.Net;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.DownloadClients.Nzbget
{
    public class NzbgetAdapter : IDownloadClientAdapter
    {
        public string ClientId => "nzbget";
        public string ClientType => "nzbget";
        public DownloadProtocol Protocol => DownloadProtocol.Usenet;

        private readonly ILogger<NzbgetAdapter> _logger;
        private readonly NzbgetXmlRpcClient _xmlRpcClient;
        private readonly NzbgetHistoryEnrichmentWorkflow _historyEnrichmentWorkflow;
        private readonly NzbgetRemovalWorkflow _removalWorkflow;
        private readonly NzbgetAddWorkflow _addWorkflow;
        private readonly NzbgetImportItemResolver _importItemResolver;
        private readonly NzbgetConnectionTester _connectionTester;

        public NzbgetAdapter(
            IHttpClientFactory httpClientFactory,
            INzbUrlResolver nzbUrlResolver,
            ILogger<NzbgetAdapter> logger)
            : this(
                httpClientFactory,
                nzbUrlResolver,
                logger,
                TimeProvider.System)
        {
        }

        internal NzbgetAdapter(
            IHttpClientFactory httpClientFactory,
            INzbUrlResolver nzbUrlResolver,
            ILogger<NzbgetAdapter> logger,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(httpClientFactory);
            ArgumentNullException.ThrowIfNull(nzbUrlResolver);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            ArgumentNullException.ThrowIfNull(timeProvider);
            _xmlRpcClient = new NzbgetXmlRpcClient(httpClientFactory, ClientType);
            var historyReader = new NzbgetHistoryReader(_xmlRpcClient);
            _historyEnrichmentWorkflow = new NzbgetHistoryEnrichmentWorkflow(
                historyReader,
                _logger,
                timeProvider);
            _removalWorkflow = new NzbgetRemovalWorkflow(_xmlRpcClient, _logger);
            _addWorkflow = new NzbgetAddWorkflow(_xmlRpcClient, _logger);
            _importItemResolver = new NzbgetImportItemResolver(_xmlRpcClient, _logger);
            _connectionTester = new NzbgetConnectionTester(_xmlRpcClient, _logger);
        }

        public async Task<(bool Success, string Message)> TestConnectionAsync(DownloadClientConfiguration client, CancellationToken ct = default)
        {
            return await _connectionTester.TestConnectionAsync(client, ct);
        }

        public async Task<DownloadClientSubmissionResult> AddAsync(
            DownloadClientConfiguration client,
            PreparedDownloadSubmission submission,
            CancellationToken ct = default)
        {
            if (submission is not PreparedUsenetSubmission usenet)
                throw new DownloadClientSubmissionException("NZBGet requires a prepared Usenet submission.");
            return await _addWorkflow.AddAsync(client, usenet, ct);
        }

        public async Task<bool> RemoveAsync(DownloadClientConfiguration client, string id, bool deleteFiles = false, CancellationToken ct = default)
        {
            return await _removalWorkflow.RemoveAsync(client, id, deleteFiles, ct);
        }

        public async Task<List<QueueItem>> GetQueueAsync(DownloadClientConfiguration client, List<string> ids, CancellationToken ct = default)
        {
            var items = new List<QueueItem>();
            if (client == null) return items;

            var isMonitorPoll = ids.Count > 0;
            var configuredCategory = DownloadClientCategoryFilter.GetConfiguredCategory(client);
            var activeIdentities = new List<NzbgetHistoryEnrichmentWorkflow.ActiveHistoryIdentity>();

            try
            {
                var listResult = await _xmlRpcClient.CallAsync(
                    new NzbgetXmlRpcRequest
                    {
                        Client = client,
                        MethodName = "listgroups",
                        Parameters = [0]
                    },
                    ct);
                var arrayData = listResult.Element("array")?.Element("data");

                if (arrayData == null)
                {
                    var message = $"NZBGet returned an invalid queue response for client {LogRedaction.SanitizeText(client.Name ?? client.Id)}.";
                    _logger.LogWarning("NZBGet returned an invalid queue response for client {ClientName}", LogRedaction.SanitizeText(client.Name ?? client.Id));
                    if (isMonitorPoll)
                    {
                        throw new DownloadClientAdapterPollingException(message);
                    }
                    return items;
                }

                foreach (var valueElement in arrayData.Elements("value"))
                {
                    try
                    {
                        var structElement = valueElement.Element("struct");
                        if (structElement != null)
                        {
                            var groupCategory = structElement.Elements("member")
                                .FirstOrDefault(m => string.Equals(m.Element("name")?.Value, "Category", StringComparison.Ordinal))?
                                .Element("value")?.Elements().FirstOrDefault()?.Value ?? string.Empty;

                            if (!DownloadClientCategoryFilter.Matches(configuredCategory, groupCategory))
                            {
                                continue;
                            }

                            var queueItem = NzbgetResponseMapper.MapGroup(client, structElement);
                            items.Add(queueItem);
                            activeIdentities.Add(
                                _historyEnrichmentWorkflow.ParseActiveIdentity(
                                    structElement,
                                    queueItem.Id,
                                    queueItem.Title));
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                    {
                        _logger.LogDebug(ex, "Failed to map NZBGet queue item (non-fatal)");
                    }
                }
            }
            catch (DownloadClientAdapterPollingException)
            {
                throw;
            }
            catch (HttpRequestException httpEx) when (httpEx.StatusCode == HttpStatusCode.Unauthorized || httpEx.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("NZBGet authentication failed for client {ClientName} — check username/password", LogRedaction.SanitizeText(client.Name ?? client.Id));
                if (isMonitorPoll)
                {
                    throw new DownloadClientAdapterPollingException("NZBGet authentication failed.", httpEx);
                }
                return items;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Failed to retrieve NZBGet queue for client {ClientName}", LogRedaction.SanitizeText(client.Name ?? client.Id));
                if (isMonitorPoll)
                {
                    throw new DownloadClientAdapterPollingException("Error polling NZBGet queue.", ex);
                }
                return items;
            }

            await ApplyGlobalDownloadRateFallbackAsync(client, items, ct);
            await _historyEnrichmentWorkflow.EnrichQueueAsync(
                client,
                configuredCategory,
                activeIdentities,
                items,
                ct,
                monitoredIds: ids);
            return NzbgetQueueFilter.FilterByIds(items, ids, activeIdentities);
        }

        public Task<List<QueueItem>> GetQueueAsync(DownloadClientConfiguration client, CancellationToken ct = default)
            => GetQueueAsync(client, [], ct);

        public async Task<List<(string Id, string Name)>> GetRecentHistoryAsync(DownloadClientConfiguration client, int limit = 100, CancellationToken ct = default)
        {
            var history = new List<(string Id, string Name)>();
            if (client == null) return history;

            try
            {
                var historyResult = await _xmlRpcClient.CallAsync(client, "history", false);
                var arrayData = historyResult.Element("array")?.Element("data");

                if (arrayData == null)
                {
                    return history;
                }

                var count = 0;
                foreach (var valueElement in arrayData.Elements("value"))
                {
                    if (count >= limit) break;

                    var structElement = valueElement.Element("struct");
                    if (structElement != null)
                    {
                        var members = structElement.Elements("member").ToDictionary(
                            m => m.Element("name")?.Value ?? string.Empty,
                            m => m.Element("value")?.Elements().FirstOrDefault()?.Value ?? string.Empty
                        );

                        var entryId = members.GetValueOrDefault("ID", string.Empty);
                        var entryName = members.GetValueOrDefault("NZBName", string.Empty);

                        if (!string.IsNullOrEmpty(entryId) && !string.IsNullOrEmpty(entryName))
                        {
                            history.Add((entryId, entryName));
                            count++;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(ex, "Failed to fetch NZBGet history for client {ClientName}", LogRedaction.SanitizeText(client.Name ?? client.Id));
            }

            return history;
        }

        /// <summary>
        /// Get all downloads as standardized DownloadClientItem objects
        /// </summary>
        public async Task<List<DownloadClientItem>> GetItemsAsync(DownloadClientConfiguration client, CancellationToken ct = default)
        {
            var items = new List<DownloadClientItem>();
            if (client == null) return items;

            var configuredCategory = DownloadClientCategoryFilter.GetConfiguredCategory(client);
            var activeIdentities = new List<NzbgetHistoryEnrichmentWorkflow.ActiveHistoryIdentity>();

            try
            {
                var listResult = await _xmlRpcClient.CallAsync(
                    new NzbgetXmlRpcRequest
                    {
                        Client = client,
                        MethodName = "listgroups",
                        Parameters = [0]
                    },
                    ct);
                var arrayData = listResult.Element("array")?.Element("data");

                if (arrayData == null)
                {
                    return items;
                }

                foreach (var valueElement in arrayData.Elements("value"))
                {
                    try
                    {
                        var structElement = valueElement.Element("struct");
                        if (structElement != null)
                        {
                            var groupCategory = structElement.Elements("member")
                                .FirstOrDefault(m => string.Equals(m.Element("name")?.Value, "Category", StringComparison.Ordinal))?
                                .Element("value")?.Elements().FirstOrDefault()?.Value ?? string.Empty;

                            if (!DownloadClientCategoryFilter.Matches(configuredCategory, groupCategory))
                            {
                                continue;
                            }

                            var downloadClientItem = NzbgetResponseMapper.MapGroupToDownloadClientItem(client, structElement);
                            items.Add(downloadClientItem);
                            activeIdentities.Add(
                                _historyEnrichmentWorkflow.ParseActiveIdentity(
                                    structElement,
                                    downloadClientItem.DownloadId,
                                    downloadClientItem.Title));
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                    {
                        _logger.LogDebug(ex, "Failed to map NZBGet queue item (non-fatal)");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Failed to retrieve NZBGet items for client {ClientName}", LogRedaction.SanitizeText(client.Name ?? client.Id));
                return items;
            }

            await ApplyGlobalDownloadRateFallbackAsync(client, items, ct);
            await _historyEnrichmentWorkflow.EnrichItemsAsync(
                client,
                configuredCategory,
                activeIdentities,
                items,
                ct);
            return items;
        }

        /// <summary>
        /// Get import item from DownloadClientItem
        /// </summary>
        public async Task<DownloadClientItem> GetImportItemAsync(
            DownloadClientConfiguration client,
            DownloadClientItem item,
            DownloadClientItem? previousAttempt = null,
            CancellationToken ct = default)
        {
            return await _importItemResolver.GetImportItemAsync(client, item);
        }

        /// <summary>
        /// Resolves the actual import item for a completed download.
        /// Queries NZBGet history for FinalDir or DestDir.
        /// Matches NzbGet.GetImportItem pattern.
        /// </summary>
        public async Task<QueueItem> GetImportItemAsync(
            DownloadClientConfiguration client,
            Download download,
            QueueItem queueItem,
            QueueItem? previousAttempt = null,
            CancellationToken ct = default)
        {
            return await _importItemResolver.GetImportItemAsync(client, queueItem);
        }

        private async Task ApplyGlobalDownloadRateFallbackAsync(
            DownloadClientConfiguration client,
            List<QueueItem> items,
            CancellationToken ct)
        {
            var candidates = items
                .Where(item => item.Eta == null &&
                    item.DownloadSpeed <= 0 &&
                    IsActiveDownloadStatus(item.Status) &&
                    item.Size > item.Downloaded)
                .ToList();
            if (candidates.Count != 1)
            {
                return;
            }

            var downloadRate = await TryGetGlobalDownloadRateAsync(client, ct);
            if (!downloadRate.HasValue || downloadRate.Value <= 0)
            {
                return;
            }

            var item = candidates[0];
            var remainingBytes = Math.Max(0, item.Size - item.Downloaded);
            item.DownloadSpeed = downloadRate.Value;
            item.Eta = CalculateEtaSeconds(remainingBytes, downloadRate.Value);
        }

        private async Task ApplyGlobalDownloadRateFallbackAsync(
            DownloadClientConfiguration client,
            List<DownloadClientItem> items,
            CancellationToken ct)
        {
            var candidates = items
                .Where(item => item.RemainingTime == null &&
                    item.DownloadSpeed <= 0 &&
                    item.Status == DownloadItemStatus.Downloading &&
                    item.RemainingSize > 0)
                .ToList();
            if (candidates.Count != 1)
            {
                return;
            }

            var downloadRate = await TryGetGlobalDownloadRateAsync(client, ct);
            if (!downloadRate.HasValue || downloadRate.Value <= 0)
            {
                return;
            }

            var item = candidates[0];
            item.DownloadSpeed = downloadRate.Value;
            var etaSeconds = CalculateEtaSeconds(item.RemainingSize, downloadRate.Value);
            item.RemainingTime = etaSeconds.HasValue
                ? TimeSpan.FromSeconds(etaSeconds.Value)
                : null;
        }

        private async Task<long?> TryGetGlobalDownloadRateAsync(
            DownloadClientConfiguration client,
            CancellationToken ct)
        {
            try
            {
                var statusResult = await _xmlRpcClient.CallAsync(
                    new NzbgetXmlRpcRequest
                    {
                        Client = client,
                        MethodName = "status"
                    },
                    ct);
                return NzbgetResponseMapper.MapStatusDownloadRate(statusResult);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(
                    ex,
                    "Unable to retrieve NZBGet global download rate for ETA fallback for client {ClientName}",
                    LogRedaction.SanitizeText(client.Name ?? client.Id));
                return null;
            }
        }

        private static int? CalculateEtaSeconds(long remainingBytes, long downloadRate)
        {
            if (remainingBytes <= 0 || downloadRate <= 0)
            {
                return null;
            }

            var etaSeconds = (long)Math.Ceiling(remainingBytes / (double)downloadRate);
            return etaSeconds > int.MaxValue ? int.MaxValue : (int)etaSeconds;
        }

        private static bool IsActiveDownloadStatus(string? status)
        {
            return string.Equals(status, "downloading", StringComparison.OrdinalIgnoreCase);
        }

    }
}
