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

namespace Listenarr.Infrastructure.HostedServices.Search
{
    public class AutomaticSearchService(
        ILogger<AutomaticSearchService> logger,
        IAutomaticSearchProcessor processor,
        IWorkerCycleRunner cycleRunner,
        IServiceScopeFactory scopeFactory) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("AutomaticSearchService started. Sweep interval and per-book cooldown are read from settings each cycle");

            await cycleRunner.RunPeriodicAsync(
                nameof(AutomaticSearchService),
                initialDelay: TimeSpan.FromMinutes(5),
                intervalProvider: GetSweepInterval,
                runCycle: processor.RunCycleAsync,
                stoppingToken);

            logger.LogInformation("AutomaticSearchService stopped");
        }

        // Read each cycle so a settings change takes effect without a restart. The sweep
        // cadence only decides how often the worker looks; eligibility is the cooldown's job.
        private TimeSpan GetSweepInterval()
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var configurationService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                var settings = configurationService.GetApplicationSettingsAsync().GetAwaiter().GetResult();
                return AutomaticSearchTiming.FromSettings(settings).SweepInterval;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(ex, "Could not read automatic-search timing settings; sweeping every {Hours} hour(s)", AutomaticSearchTiming.Default.SweepInterval.TotalHours);
                return AutomaticSearchTiming.Default.SweepInterval;
            }
        }
    }
}
