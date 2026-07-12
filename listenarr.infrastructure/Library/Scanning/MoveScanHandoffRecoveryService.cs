using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Scanning;

public sealed class MoveScanHandoffRecoveryService(
    IScanQueueService scanQueueService,
    IServiceScopeFactory scopeFactory,
    ILogger<MoveScanHandoffRecoveryService> logger)
{
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var historyRepository = scope.ServiceProvider.GetRequiredService<IHistoryRepository>();
        var audiobookRepository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
        var queuedHandoffs = await historyRepository.GetPendingMoveScanHandoffsAsync(
            cancellationToken);

        foreach (var handoff in queuedHandoffs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!handoff.AudiobookId.HasValue)
                {
                    await RecordUnrecoverableHandoffAsync(
                        historyRepository,
                        handoff,
                        "The post-move scan handoff has no audiobook id.",
                        cancellationToken);
                    continue;
                }

                var audiobook = await audiobookRepository.GetByIdAsync(handoff.AudiobookId.Value);
                if (audiobook == null)
                {
                    await RecordUnrecoverableHandoffAsync(
                        historyRepository,
                        handoff,
                        $"Audiobook {handoff.AudiobookId.Value} no longer exists.",
                        cancellationToken);
                    continue;
                }

                var scanJobId = await scanQueueService.EnqueueRecoveredScanAsync(
                    audiobook,
                    handoff.CorrelationId,
                    async () =>
                    {
                        var current = await historyRepository.GetByCorrelationIdAsync(
                            handoff.CorrelationId,
                            cancellationToken);
                        return !HasTerminalScanHistory(current);
                    });

                if (scanJobId.HasValue)
                {
                    logger.LogInformation(
                        "Recovered move scan handoff {CorrelationId} as scan job {ScanJobId}",
                        handoff.CorrelationId,
                        scanJobId.Value);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
            {
                logger.LogWarning(
                    exception,
                    "Failed to recover move scan handoff {CorrelationId}; continuing with remaining handoffs",
                    handoff.CorrelationId);
            }
        }
    }

    private async Task RecordUnrecoverableHandoffAsync(
        IHistoryRepository historyRepository,
        History handoff,
        string error,
        CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Cannot recover move scan handoff {CorrelationId}: {Error}",
            handoff.CorrelationId,
            error);
        var existing = await historyRepository.GetByCorrelationIdAsync(
            handoff.CorrelationId,
            cancellationToken);
        if (HasTerminalScanHistory(existing))
        {
            return;
        }

        await historyRepository.AddAsync(new History
        {
            AudiobookId = handoff.AudiobookId,
            AudiobookTitle = handoff.AudiobookTitle,
            SourceTitle = handoff.SourceTitle,
            EventType = HistoryEvents.ScanFailed,
            Outcome = HistoryOutcome.Failed,
            Source = "LibraryScan",
            Message = "Post-move scan handoff could not be recovered",
            Error = error,
            Timestamp = DateTime.UtcNow,
            CorrelationId = handoff.CorrelationId,
            Data = handoff.Data
        }, cancellationToken);
    }

    private static bool HasTerminalScanHistory(IEnumerable<History> history) =>
        history.Any(entry =>
            (string.Equals(entry.EventType, HistoryEvents.ScanCompleted, StringComparison.Ordinal)
                && entry.Outcome == HistoryOutcome.Succeeded)
            || (string.Equals(entry.EventType, HistoryEvents.ScanFailed, StringComparison.Ordinal)
                && entry.Outcome == HistoryOutcome.Failed));
}
