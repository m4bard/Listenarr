namespace Listenarr.Api.Services
{
    public class SeriesMonitoringBackgroundService : BackgroundService
    {
        private readonly ILogger<SeriesMonitoringBackgroundService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly TimeSpan _syncInterval = TimeSpan.FromDays(1);

        public SeriesMonitoringBackgroundService(
            ILogger<SeriesMonitoringBackgroundService> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "SeriesMonitoringBackgroundService started. Monitored series will be checked every {Hours} hours",
                _syncInterval.TotalHours);

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("SeriesMonitoringBackgroundService canceled before first sync cycle");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var monitoringService = scope.ServiceProvider.GetRequiredService<ISeriesMonitoringService>();
                    var syncedCount = await monitoringService.SyncDueSeriesAsync(stoppingToken);
                    _logger.LogInformation(
                        "SeriesMonitoringBackgroundService completed sync cycle. Synced {Count} monitored series",
                        syncedCount);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, "Series monitoring sync cycle canceled unexpectedly; continuing");
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogError(ex, "Error during series monitoring sync cycle");
                }

                try
                {
                    await Task.Delay(_syncInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("SeriesMonitoringBackgroundService stopped");
        }
    }
}
