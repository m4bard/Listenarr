using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Jobs;

public partial class MoveQueueService
{
    public async Task<MoveRetryScheduleResult> ScheduleRetryAsync(
        Guid id,
        string leaseOwner,
        int leaseGeneration,
        string error,
        CancellationToken cancellationToken = default)
    {
        var job = await _persistence.GetByIdAsync(id, cancellationToken)
            ?? throw new MoveLeaseLostException(id, leaseGeneration);
        var nextAttemptCount = job.AttemptCount + 1;
        var now = _timeProvider.GetUtcNow();
        var nextAttemptAt = now.Add(
            MoveTimingPolicy.GetRetryDelay(id, nextAttemptCount));
        var result = await _persistence.ScheduleRetryAsync(
            id,
            leaseOwner,
            leaseGeneration,
            job.AttemptCount,
            now,
            nextAttemptAt,
            MoveTimingPolicy.MaxTransientAttempts,
            error,
            cancellationToken)
            ?? throw new MoveLeaseLostException(id, leaseGeneration);

        var reportedError = result.Status == MoveJobStatus.NeedsAttention
            ? $"{error} Automatic retry limit exhausted; operator attention is required."
            : error;
        LogStatusChange(id, result.Status, reportedError);
        try
        {
            await _relocationService.OnMoveJobStateChangedAsync(id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            && ex is not OutOfMemoryException
            && ex is not StackOverflowException)
        {
            _logger.LogWarning(ex, "Failed to reconcile relocation for retried move job {JobId}", id);
        }

        try
        {
            await _hubBroadcaster.BroadcastAsync("MoveJobUpdate", new
            {
                jobId = id.ToString(),
                audiobookId = job.AudiobookId,
                status = result.Status.ToString(),
                error = reportedError,
                target = job.RequestedPath,
                updatedAt = now,
                nextAttemptAt = result.NextAttemptAt
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            && ex is not OutOfMemoryException
            && ex is not StackOverflowException)
        {
            _logger.LogWarning(ex, "Failed to broadcast retry state for move job {JobId}", id);
        }

        return result;
    }
}
