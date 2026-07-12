using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Scanning;

public partial class ScanJobProcessor
{
    private async Task<bool> RecordScanCompletionAsync(
        IHistoryRepository historyRepository,
        ScanJob job,
        Audiobook audiobook,
        int found,
        int created,
        string scanRoot,
        CancellationToken cancellationToken)
    {
        var correlationId = job.CorrelationId ?? job.Id.ToString("N");
        var completion = new History
        {
            AudiobookId = audiobook.Id,
            AudiobookTitle = audiobook.Title,
            SourceTitle = audiobook.Title,
            DownloadId = job.DownloadId,
            EventType = HistoryEvents.ScanCompleted,
            Outcome = HistoryOutcome.Succeeded,
            Source = "LibraryScan",
            Message = $"Library scan completed: {found} found, {created} created",
            Timestamp = DateTime.UtcNow,
            CorrelationId = correlationId,
            Data = JsonSerializer.Serialize(new
            {
                ScanJobId = job.Id,
                Found = found,
                Created = created,
                Path = scanRoot
            })
        };
        if (!string.IsNullOrWhiteSpace(job.CorrelationId)
            && job.CorrelationId.StartsWith("move:", StringComparison.Ordinal))
        {
            await _queue.CommitTerminalJobStatusAsync(
                job.Id,
                async () =>
                {
                    var correlated = await historyRepository.GetByCorrelationIdAsync(
                        correlationId,
                        cancellationToken);
                    if (FindCompletedMoveScanHistory(correlated) != null)
                    {
                        return ("Completed", (string?)null);
                    }

                    var failed = FindFailedMoveScanHistory(correlated);
                    if (failed != null)
                    {
                        return ("Failed", failed.Error);
                    }

                    await historyRepository.AddAsync(completion, cancellationToken);
                    return ("Completed", (string?)null);
                },
                cancellationToken);
            return true;
        }

        await historyRepository.AddAsync(completion, cancellationToken);
        return false;
    }

    private async Task<bool> RecordScanFailureHistoryAsync(
        IHistoryRepository historyRepository,
        ScanJob job,
        Audiobook? audiobook,
        string error,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(job.CorrelationId)
            && job.CorrelationId.StartsWith("move:", StringComparison.Ordinal))
        {
            await RecordMoveScanFailureAsync(
                historyRepository,
                job,
                audiobook,
                error,
                cancellationToken);
            return true;
        }

        await historyRepository.AddAsync(new History
        {
            AudiobookId = job.AudiobookId,
            AudiobookTitle = audiobook?.Title,
            SourceTitle = audiobook?.Title,
            DownloadId = job.DownloadId,
            EventType = HistoryEvents.ScanFailed,
            Outcome = HistoryOutcome.Failed,
            Source = "LibraryScan",
            Message = "Library scan failed",
            Error = error,
            Timestamp = DateTime.UtcNow,
            CorrelationId = job.Id.ToString("N"),
            Data = JsonSerializer.Serialize(new { ScanJobId = job.Id, job.Path })
        }, cancellationToken);
        return false;
    }

    private async Task RecordMoveScanFailureAsync(
        IHistoryRepository historyRepository,
        ScanJob job,
        Audiobook? audiobook,
        string error,
        CancellationToken cancellationToken)
    {
        var isMoveHandoff = !string.IsNullOrWhiteSpace(job.CorrelationId)
            && job.CorrelationId.StartsWith("move:", StringComparison.Ordinal);
        if (!isMoveHandoff)
        {
            UpdateFailedScanStatus(job, error);
            return;
        }

        var correlationId = job.CorrelationId!;
        await _queue.CommitTerminalJobStatusAsync(
            job.Id,
            async () =>
            {
                var correlated = await historyRepository.GetByCorrelationIdAsync(
                    correlationId,
                    cancellationToken);
                var failed = FindFailedMoveScanHistory(correlated);
                if (failed != null)
                {
                    return ("Failed", failed.Error);
                }

                if (FindCompletedMoveScanHistory(correlated) != null)
                {
                    return ("Completed", (string?)null);
                }

                await historyRepository.AddAsync(new History
                {
                    AudiobookId = audiobook?.Id ?? job.AudiobookId,
                    AudiobookTitle = audiobook?.Title,
                    SourceTitle = audiobook?.Title,
                    DownloadId = job.DownloadId,
                    EventType = HistoryEvents.ScanFailed,
                    Outcome = HistoryOutcome.Failed,
                    Source = "LibraryScan",
                    Message = "Post-move library scan failed",
                    Error = error,
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = correlationId,
                    Data = JsonSerializer.Serialize(new
                    {
                        ScanJobId = job.Id,
                        job.Path
                    })
                }, cancellationToken);
                return ("Failed", error);
            },
            cancellationToken);
    }

    private static History? FindCompletedMoveScanHistory(IEnumerable<History> history) =>
        history.FirstOrDefault(entry =>
            string.Equals(entry.EventType, HistoryEvents.ScanCompleted, StringComparison.Ordinal)
            && entry.Outcome == HistoryOutcome.Succeeded);

    private static History? FindFailedMoveScanHistory(IEnumerable<History> history) =>
        history.FirstOrDefault(entry =>
            string.Equals(entry.EventType, HistoryEvents.ScanFailed, StringComparison.Ordinal)
            && entry.Outcome == HistoryOutcome.Failed);

    private void UpdateFailedScanStatus(ScanJob job, string error)
    {
        try
        {
            _queue.UpdateJobStatus(job.Id, "Failed", error);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            _logger.LogDebug(
                exception,
                "Unable to update failed scan job {JobId}",
                job.Id);
        }
    }
}
