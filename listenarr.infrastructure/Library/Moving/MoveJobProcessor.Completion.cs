using Listenarr.Application.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Moving;

internal partial class MoveJobProcessor
{
    private async Task RecordMoveCompletionAsync(
        MoveJob job,
        Audiobook audiobook,
        string source,
        string target,
        IAudiobookRepository audiobookRepository,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var correlationId = $"move:{job.Id:N}";
        try
        {
            var historyRepository = serviceProvider.GetRequiredService<IHistoryRepository>();
            var existingHistory = await historyRepository.GetByCorrelationIdAsync(
                correlationId,
                cancellationToken);
            if (!existingHistory.Any(entry =>
                    string.Equals(entry.EventType, "Moved", StringComparison.Ordinal)))
            {
                var notificationSent = await TrySendMoveWebhooksAsync(
                    job,
                    audiobook,
                    source,
                    target,
                    serviceProvider);
                await historyRepository.AddAsync(new History
                {
                    AudiobookId = audiobook.Id,
                    AudiobookTitle = audiobook.Title,
                    EventType = "Moved",
                    Message = $"Moved audiobook files from {source} to {target}",
                    Source = "Move",
                    Timestamp = DateTime.UtcNow,
                    NotificationSent = notificationSent,
                    CorrelationId = correlationId,
                    Data = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        JobId = job.Id,
                        Source = source,
                        Target = target
                    })
                }, cancellationToken);
                logger.LogInformation("Added history entry for move job {JobId}", job.Id);
                await TryPublishMoveToastAsync(job, audiobook, target);
            }

            await TryEnqueueMoveScanAndBroadcastAsync(
                job,
                audiobook,
                audiobookRepository,
                correlationId);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogWarning(
                exception,
                "Failed to record one or more best-effort completion side effects for move job {JobId}",
                job.Id);
        }
    }

    private async Task<bool> TrySendMoveWebhooksAsync(
        MoveJob job,
        Audiobook audiobook,
        string source,
        string target,
        IServiceProvider serviceProvider)
    {
        try
        {
            var configurationService = serviceProvider.GetRequiredService<IConfigurationService>();
            var notificationService = serviceProvider.GetRequiredService<INotificationService>();
            var webhooks = await configurationService.GetWebhookConfigurationsAsync();
            foreach (var webhook in webhooks.Where(webhook =>
                webhook.IsEnabled && webhook.Triggers.Contains("Moved")))
            {
                await notificationService.SendNotificationAsync(
                    "Moved",
                    new
                    {
                        AudiobookTitle = audiobook.Title,
                        Source = source,
                        Target = target,
                        Timestamp = DateTime.UtcNow
                    },
                    webhook.Url,
                    webhook.Triggers);
            }

            return true;
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogWarning(
                exception,
                "Failed to send move notification for {JobId}",
                job.Id);
            return false;
        }
    }

    private async Task TryPublishMoveToastAsync(
        MoveJob job,
        Audiobook audiobook,
        string target)
    {
        try
        {
            var message = !string.IsNullOrEmpty(audiobook.Title)
                ? $"Moved {audiobook.Title} to {target}"
                : $"Moved audiobook to {target}";
            await toastService.PublishToastAsync(
                "success",
                "Move Complete",
                message,
                timeoutMs: 5000);
            logger.LogDebug("Sent toast notification for move job {JobId}", job.Id);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogDebug(
                exception,
                "Failed to send toast notification for move job {JobId}",
                job.Id);
        }
    }

    private async Task TryEnqueueMoveScanAndBroadcastAsync(
        MoveJob job,
        Audiobook audiobook,
        IAudiobookRepository audiobookRepository,
        string correlationId)
    {
        try
        {
            var scanJobId = await scanQueueService.EnqueueScanAsync(
                audiobook,
                path: null,
                correlationId);
            logger.LogInformation(
                "Enqueued scan job {ScanJobId} for audiobook {AudiobookId} after move",
                scanJobId,
                audiobook.Id);

            var fresh = await audiobookRepository.GetByIdAsync(audiobook.Id);
            if (fresh != null)
            {
                var audiobookDtoFull = AudiobookDtoFactory.BuildFromEntity(fresh);
                await hubContext.Clients.All.SendAsync("AudiobookUpdate", audiobookDtoFull);
                logger.LogInformation(
                    "Broadcasted full AudiobookUpdate for AudiobookId {AudiobookId} after move job {JobId}",
                    audiobook.Id,
                    job.Id);
            }
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogWarning(
                exception,
                "Failed to enqueue scan or broadcast AudiobookUpdate after move job {JobId}",
                job.Id);
        }
    }
}
