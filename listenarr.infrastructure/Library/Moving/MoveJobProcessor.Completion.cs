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
        AudiobookContentMoveService contentMoveService,
        AudiobookContentMoveRequest moveRequest,
        CancellationToken cancellationToken)
    {
        var correlationId = $"move:{job.Id:N}";
        var historyRepository = serviceProvider.GetRequiredService<IHistoryRepository>();
        await contentMoveService.MarkCompletionRecordingAsync(
            moveRequest,
            cancellationToken);

        contentMoveService.OnCompletionHandoff(
            job.Id,
            CompletionHandoffFaultPoint.BeforeHistoryPersist);
        await contentMoveService.MarkCompletionRecordingAsync(
            moveRequest,
            cancellationToken);
        var moveHistoryWrite = await historyRepository.GetOrAddLeasedMoveHistoryAsync(
            new History
            {
                AudiobookId = audiobook.Id,
                AudiobookTitle = audiobook.Title,
                EventType = "Moved",
                Message = $"Moved audiobook files from {source} to {target}",
                Source = "Move",
                Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                NotificationSent = false,
                CorrelationId = correlationId,
                Data = System.Text.Json.JsonSerializer.Serialize(new
                {
                    JobId = job.Id,
                    Source = source,
                    Target = target
                })
            },
            job.Id,
            job.LeaseOwner!,
            job.LeaseGeneration,
            timeProvider.GetUtcNow(),
            cancellationToken);
        var moveHistory = moveHistoryWrite.Entry;
        if (moveHistoryWrite.Created)
        {
            logger.LogInformation("Added history entry for move job {JobId}", job.Id);
        }

        if (moveHistoryWrite.Created)
        {
            var notificationSent = await TrySendMoveWebhooksAsync(
                job,
                audiobook,
                source,
                target,
                serviceProvider);
            if (notificationSent)
            {
                moveHistory.NotificationSent = true;
                try
                {
                    await historyRepository.UpdateAsync(moveHistory, cancellationToken);
                }
                catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
                {
                    logger.LogWarning(
                        exception,
                        "Move history was recorded but its notification flag could not be updated for job {JobId}",
                        job.Id);
                }
            }

            await TryPublishMoveToastAsync(job, audiobook, target);
        }

        await contentMoveService.MarkCompletionRecordingAsync(
            moveRequest,
            cancellationToken);
        var completionHistory = await historyRepository.GetByCorrelationIdAsync(
            correlationId,
            cancellationToken);
        var terminalScanExists = completionHistory.Any(entry =>
            (string.Equals(entry.EventType, HistoryEvents.ScanCompleted, StringComparison.Ordinal)
                && entry.Outcome == HistoryOutcome.Succeeded)
            || (string.Equals(entry.EventType, HistoryEvents.ScanFailed, StringComparison.Ordinal)
                && entry.Outcome == HistoryOutcome.Failed));
        if (!terminalScanExists)
        {
            await contentMoveService.MarkCompletionRecordingAsync(
                moveRequest,
                cancellationToken);
            await historyRepository.GetOrAddLeasedMoveHistoryAsync(
                new History
                {
                    AudiobookId = audiobook.Id,
                    AudiobookTitle = audiobook.Title,
                    SourceTitle = audiobook.Title,
                    EventType = HistoryEvents.ScanQueued,
                    Outcome = HistoryOutcome.Requested,
                    Source = "Move",
                    Message = "Post-move library scan requested",
                    Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                    CorrelationId = correlationId,
                    Data = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        MoveJobId = job.Id,
                        AudiobookId = audiobook.Id,
                        Target = target
                    })
                },
                job.Id,
                job.LeaseOwner!,
                job.LeaseGeneration,
                timeProvider.GetUtcNow(),
                cancellationToken);
            try
            {
                await contentMoveService.MarkCompletionRecordingAsync(
                    moveRequest,
                    cancellationToken);
                contentMoveService.OnCompletionHandoff(
                    job.Id,
                    CompletionHandoffFaultPoint.BeforeScanEnqueue);
                await contentMoveService.MarkCompletionRecordingAsync(
                    moveRequest,
                    cancellationToken);
                var scanJobId = await scanQueueService.EnqueueScanAsync(
                    audiobook,
                    path: null,
                    correlationId);
                logger.LogInformation(
                    "Enqueued scan job {ScanJobId} for audiobook {AudiobookId} after move",
                    scanJobId,
                    audiobook.Id);
            }
            catch (Exception exception) when (exception is MoveLeaseLostException or PersistenceException)
            {
                throw;
            }
            catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
            {
                logger.LogWarning(
                    exception,
                    "Durable scan handoff for move job {JobId} was recorded but immediate dispatch failed; background recovery will retry it",
                    job.Id);
            }
        }
        else
        {
            logger.LogInformation(
                "Skipped replaying terminal scan handoff for move job {JobId}",
                job.Id);
        }

        await TryBroadcastAudiobookUpdateAsync(
            job,
            audiobook,
            audiobookRepository);
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
                        Timestamp = timeProvider.GetUtcNow().UtcDateTime
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

    private async Task TryBroadcastAudiobookUpdateAsync(
        MoveJob job,
        Audiobook audiobook,
        IAudiobookRepository audiobookRepository)
    {
        try
        {
            var fresh = await audiobookRepository.GetByIdAsync(audiobook.Id);
            if (fresh == null)
            {
                return;
            }

            var audiobookDtoFull = AudiobookDtoFactory.BuildFromEntity(fresh);
            await hubContext.Clients.All.SendAsync("AudiobookUpdate", audiobookDtoFull);
            logger.LogInformation(
                "Broadcasted full AudiobookUpdate for AudiobookId {AudiobookId} after move job {JobId}",
                audiobook.Id,
                job.Id);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogWarning(
                exception,
                "Failed to broadcast AudiobookUpdate after move job {JobId}",
                job.Id);
        }
    }
}
