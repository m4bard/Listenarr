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
namespace Listenarr.Api.Services
{
    /// <summary>
    /// Hosted service that consumes job IDs published to the DownloadProcessingChannel and triggers immediate processing.
    /// It acts as a bridge to the existing DownloadProcessingBackgroundService which still polls the DB for jobs.
    /// </summary>
    public class DownloadProcessingChannelConsumer : BackgroundService
    {
        private readonly IProcessingChannel _channel;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DownloadProcessingChannelConsumer> _logger;

        public DownloadProcessingChannelConsumer(DownloadProcessingChannel channel, IServiceScopeFactory scopeFactory, ILogger<DownloadProcessingChannelConsumer> logger)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var jobId in _channel.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var queueService = scope.ServiceProvider.GetRequiredService<IDownloadProcessingQueueService>();
                        var job = await queueService.GetJobAsync(jobId);
                        if (job == null) continue;

                        // If job is pending, trigger immediate processing via DownloadProcessingBackgroundService by
                        // leaving it in Pending state. The background service will pick it up during its next loop.
                        // Optionally we could signal a processing mechanism here; keep lightweight for now.
                        _logger.LogDebug("Channel consumer observed job {JobId}", jobId);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (OperationCanceledException ex)
                    {
                        _logger.LogWarning(ex, "Channel consumer canceled/timed out while handling job {JobId}", jobId);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                    {
                        _logger.LogWarning(ex, "Failed to handle channel job {JobId}", jobId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Download processing channel consumer stopping due to host shutdown");
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "Download processing channel stream canceled/timed out");
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Unhandled error in DownloadProcessingChannelConsumer stream");
            }
        }
    }
}

