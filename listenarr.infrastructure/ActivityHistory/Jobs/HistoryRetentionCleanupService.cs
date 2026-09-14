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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.ActivityHistory.Jobs
{
    /// <summary>
    /// Schedules retention cleanup for action-history rows. Kept as its own worker
    /// rather than folded into another cleanup service, because it prunes a table
    /// unrelated to those services' subject matter (moved downloads, image cache,
    /// terminal processing jobs) and has its own configured retention window
    /// (<see cref="ApplicationSettings.HistoryRetentionDays" />).
    /// </summary>
    public sealed class HistoryRetentionCleanupService(
        IHistoryRetentionCleanupProcessor processor,
        IWorkerCycleRunner cycleRunner,
        ILogger<HistoryRetentionCleanupService> logger) : BackgroundService
    {
        private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromDays(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("History retention cleanup worker started");

            await cycleRunner.RunPeriodicAsync(
                nameof(HistoryRetentionCleanupService),
                InitialDelay,
                () => CleanupInterval,
                processor.RunCycleAsync,
                stoppingToken);

            logger.LogInformation("History retention cleanup worker stopped");
        }
    }

    public sealed class HistoryRetentionCleanupProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<HistoryRetentionCleanupProcessor> logger) : IHistoryRetentionCleanupProcessor
    {
        /// <summary>
        /// Runs one cleanup cycle. The processor resolves the scoped repository and
        /// configuration service per cycle so the hosted service remains a scheduling
        /// adapter and the configured setting is always read fresh. Broad exceptions
        /// are not caught here because <see cref="IWorkerCycleRunner" /> owns non-fatal
        /// failure logging/metrics and will retry on the next interval.
        /// </summary>
        public async Task RunCycleAsync(CancellationToken cancellationToken = default)
        {
            using var scope = scopeFactory.CreateScope();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var history = scope.ServiceProvider.GetRequiredService<IHistoryRepository>();

            var settings = await configuration.GetApplicationSettingsAsync();
            var retentionDays = settings.HistoryRetentionDays;

            if (retentionDays <= 0)
            {
                logger.LogInformation("History retention is unlimited; skipping cleanup cycle");
                return;
            }

            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
            var deletedCount = await history.DeleteOlderThanAsync(cutoffDate, cancellationToken);

            logger.LogInformation(
                "Cleaned up {Count} history entries older than {Days} days",
                deletedCount, retentionDays);
        }
    }
}
