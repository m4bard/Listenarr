namespace Listenarr.Api.Services
{
    public class AuthorMonitoringBackgroundService : BackgroundService
    {
        private readonly ILogger<AuthorMonitoringBackgroundService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly TimeSpan _syncInterval = TimeSpan.FromDays(1);

        public AuthorMonitoringBackgroundService(
            ILogger<AuthorMonitoringBackgroundService> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "AuthorMonitoringBackgroundService started. Monitored authors will be checked every {Hours} hours",
                _syncInterval.TotalHours);

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("AuthorMonitoringBackgroundService canceled before first sync cycle");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var monitoringService = scope.ServiceProvider.GetRequiredService<IAuthorMonitoringService>();
                    var syncedCount = await monitoringService.SyncDueAuthorsAsync(stoppingToken);
                    _logger.LogInformation(
                        "AuthorMonitoringBackgroundService completed sync cycle. Synced {Count} monitored author(s)",
                        syncedCount);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogWarning(ex, "Author monitoring sync cycle canceled unexpectedly; continuing");
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogError(ex, "Error during author monitoring sync cycle");
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

            _logger.LogInformation("AuthorMonitoringBackgroundService stopped");
        }
    }
}
