/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Moving;

public sealed class MoveBackgroundService(
    IMoveQueueService moveQueueService,
    IMoveJobProcessor processor,
    ILogger<MoveBackgroundService> logger,
    IAppMetricsService? metrics = null) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var leaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        var retryDelay = TimeSpan.FromSeconds(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await moveQueueService.RecoverActiveJobsAsync(stoppingToken);
                if (metrics != null)
                {
                    var health = await moveQueueService.GetQueueHealthAsync(stoppingToken);
                    metrics.Gauge("worker.move.queue.depth", health.QueueDepth);
                    metrics.Gauge("worker.move.queue.oldest_age_seconds", health.OldestQueuedAgeSeconds);
                    metrics.Gauge("worker.move.queue.retries", health.RetryCount);
                    metrics.Gauge("worker.move.queue.expired_leases", health.ExpiredLeaseCount);
                    metrics.Gauge("worker.move.queue.needs_attention", health.NeedsAttentionCount);
                }
                while (moveQueueService.Reader.TryRead(out var job))
                {
                    if (!await moveQueueService.TryClaimJobAsync(job.Id, leaseOwner, stoppingToken))
                    {
                        continue;
                    }

                    try
                    {
                        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        var heartbeatTask = RunHeartbeatAsync(
                            job.Id,
                            leaseOwner,
                            heartbeatCancellation.Token);
                        try
                        {
                            await processor.ProcessJobAsync(job, stoppingToken);
                        }
                        finally
                        {
                            await heartbeatCancellation.CancelAsync();
                            try
                            {
                                await heartbeatTask;
                            }
                            catch (OperationCanceledException) when (heartbeatCancellation.IsCancellationRequested)
                            {
                            }
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
                    {
                        logger.LogError(exception, "Unexpected error processing move job {JobId}", job.Id);
                        await moveQueueService.UpdateJobStatusAsync(
                            job.Id,
                            MoveJobStatus.Failed,
                            exception.Message,
                            stoppingToken);
                    }
                }

                retryDelay = TimeSpan.FromSeconds(1);
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                logger.LogError(exception, "Move queue poll failed; the worker will retry");
                await Task.Delay(retryDelay, stoppingToken);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
            }
        }

        logger.LogInformation("MoveBackgroundService stopping due to host shutdown");
    }

    private async Task RunHeartbeatAsync(
        Guid jobId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await moveQueueService.HeartbeatJobAsync(jobId, leaseOwner, cancellationToken);
        }
    }
}
