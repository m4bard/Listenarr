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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Listenarr.Api.Services;
using Listenarr.Domain.Models;
using Listenarr.Application.Repositories;
using Microsoft.Extensions.Logging;
using Listenarr.Domain.Utils;

namespace Listenarr.Api.Tests
{
    internal class TestDownloadQueueService : IDownloadQueueService
    {
        private readonly IDownloadRepository _downloadRepository;
        private readonly IDownloadClientGateway _clientGateway;
        private readonly IConfigurationService _config;
        private readonly ILogger<TestDownloadQueueService>? _logger;
        private readonly IAppMetricsService? _metrics;
        private readonly HttpClient? _httpClient;

        public TestDownloadQueueService(IDownloadRepository downloadRepo, IDownloadClientGateway clientGateway, IConfigurationService config, ILogger<TestDownloadQueueService>? logger, IAppMetricsService? metrics = null, HttpClient? httpClient = null)
        {
            _downloadRepository = downloadRepo;
            _clientGateway = clientGateway;
            _config = config;
            _logger = logger;
            _metrics = metrics;
            _httpClient = httpClient;
        }

        public async Task<List<QueueItem>> GetQueueAsync()
        {
            var clients = await _config.GetDownloadClientConfigurationsAsync();
            var enabled = clients.Where(c => c.IsEnabled).ToList();

            var allDownloads = await _downloadRepository.GetAllAsync();
            var listenarrDownloads = allDownloads.Where(d => d.Status != DownloadStatus.Completed && d.Status != DownloadStatus.Moved).ToList();

            var results = new List<QueueItem>();
            foreach (var client in enabled)
            {
                try
                {
                    var q = await _clientGateway.GetQueueAsync(client);

                    // Simple matching: include items whose id matches a DB download id
                    var matched = q.Where(item => listenarrDownloads.Any(d => d.Id == item.Id)).ToList();
                    results.AddRange(matched);

                    // Emulate SABnzbd history-based purge safety checks used by the real queue service tests
                    if (string.Equals(client.Type, "sabnzbd", StringComparison.OrdinalIgnoreCase) && _httpClient != null && _metrics != null)
                    {
                        // If there's an orphaned DB entry for this client, attempt to fetch history and emit metric when title match prevents purge
                        var orphaned = allDownloads.Where(d => d.DownloadClientId == client.Id && !results.Any(r => r.Id == d.Id)).ToList();
                        if (orphaned.Any())
                        {
                            try
                            {
                                var apiKey = string.Empty;
                                if (client.Settings != null && client.Settings.TryGetValue("apiKey", out var apiKeyObj))
                                    apiKey = apiKeyObj?.ToString() ?? string.Empty;

                                if (!string.IsNullOrEmpty(apiKey))
                                {
                                    var baseUrl = $"{(client.UseSSL ? "https" : "http")}://{client.Host}:{client.Port}/api";
                                    var historyUrl = $"{baseUrl}?mode=history&output=json&limit=100&apikey={Uri.EscapeDataString(apiKey)}";
                                    var historyResp = await _httpClient.GetAsync(historyUrl);
                                    if (historyResp.IsSuccessStatusCode)
                                    {
                                        var historyText = await historyResp.Content.ReadAsStringAsync();
                                        if (!string.IsNullOrWhiteSpace(historyText))
                                        {
                                            try
                                            {
                                                using var doc = JsonDocument.Parse(historyText);
                                                var root = doc.RootElement;
                                                if (root.TryGetProperty("history", out var history) && history.TryGetProperty("slots", out var slots) && slots.ValueKind == JsonValueKind.Array)
                                                {
                                                    var names = new List<string>();
                                                    names.AddRange(slots.EnumerateArray()
                                                        .Select(slot => slot.TryGetProperty("name", out var nm) ? nm.GetString() ?? string.Empty : string.Empty)
                                                        .Where(name => !string.IsNullOrEmpty(name)));

                                                    var matchCount = orphaned.Count(d => !string.IsNullOrEmpty(d.Title) && names.Any(n => TitleUtils.NormalizeTitle(n).Contains(TitleUtils.NormalizeTitle(d.Title!))));
                                                    for (var i = 0; i < matchCount; i++)
                                                        _metrics.Increment("download.purge.skipped.history.title_match");
                                                }
                                            }
                                            catch (JsonException ex)
                                            {
                                                _logger?.LogDebug(ex, "TestDownloadQueueService: failed to parse SAB history payload");
                                            }
                                        }
                                    }
                                }
                            }
                            catch (HttpRequestException ex)
                            {
                                _logger?.LogDebug(ex, "TestDownloadQueueService: failed to fetch SAB history");
                            }
                            catch (TaskCanceledException ex)
                            {
                                _logger?.LogDebug(ex, "TestDownloadQueueService: SAB history fetch timed out");
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger?.LogDebug(ex, "TestDownloadQueueService: client fetch failed");
                }
            }

            return results;
        }

        public async Task<QueueSnapshot> GetQueueSnapshotAsync()
        {
            var items = await GetQueueAsync();
            return new QueueSnapshot
            {
                Items = items,
                GeneratedAt = DateTime.UtcNow
            };
        }
    }
}
