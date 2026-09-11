/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Common.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Metadata.Refresh;

/// <summary>
/// The run loop. One book at a time, in the order the scope query returned them, each in its
/// own scope, with the shared budget metering the whole thing and the run's terminal state
/// decided by how the loop left it.
/// </summary>
public sealed partial class MetadataRefreshCoordinator
{
    private async Task ExecuteAsync(
        MetadataRefreshRun run,
        List<MetadataRefreshCandidate> candidates,
        CancellationToken cancellationToken)
    {
        MetadataRefreshRunBudget? budget = null;
        var state = MetadataRefreshRunState.Completed;

        try
        {
            // Inside the try, so a throw here still releases the gate and finishes the run
            // rather than leaving _active set and the gate held for the life of the process.
            var runBudget = AcquireBudget();
            budget = runBudget;

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                cancellationToken.ThrowIfCancellationRequested();

                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IMetadataRefreshService>();
                MetadataRefreshOutcome outcome;
                var providerAnswers = 0;
                try
                {
                    var result = await service.RefreshAsync(
                        candidate.AudiobookId,
                        runBudget,
                        cancellationToken);
                    outcome = result.Outcome;
                    providerAnswers = result.ProviderAnswers;
                }
                catch (ApplicationConflictException ex)
                {
                    // A refused filesystem mutation is this book's problem, not the run's: it
                    // keeps its unset timestamp and comes back next cycle.
                    _logger.LogDebug(
                        "Metadata refresh run {RunId} deferred audiobook {AudiobookId}: {Code}",
                        run.RunId,
                        candidate.AudiobookId,
                        ex.Code);
                    outcome = MetadataRefreshOutcome.Conflict;
                }
                catch (Exception ex) when (WorkerExceptionClassifier.IsNonFatal(ex))
                {
                    // One bad book is not a bad run. A malformed row, or a provider client
                    // raising something nobody anticipated, costs that book and nothing else;
                    // letting it out of the loop would end the walk and leave every book behind
                    // it untouched until the next cycle reached the same one and stopped again.
                    _logger.LogWarning(
                        ex,
                        "Metadata refresh run {RunId} failed audiobook {AudiobookId}",
                        run.RunId,
                        candidate.AudiobookId);
                    outcome = MetadataRefreshOutcome.Failed;
                }

                run.Record(outcome);
                run.RequestsSpent = runBudget.RequestsSpent;

                // Updated, Skipped and Unusable are settled locally: one wrote provider
                // metadata, the other two never had a question anyone could ask. NotFound is the
                // only outcome that rests on the provider having answered, and it is stamped
                // only when one did. A run that asked and got silence, whether from pushback or
                // from a provider that was not there, leaves the timestamp unset and asks again
                // next cycle.
                if (outcome is MetadataRefreshOutcome.Updated
                    or MetadataRefreshOutcome.Skipped
                    or MetadataRefreshOutcome.Unusable
                    || (outcome == MetadataRefreshOutcome.NotFound && providerAnswers > 0))
                {
                    var repository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
                    await repository.StampMetadataRefreshAsync(
                        candidate.AudiobookId,
                        _timeProvider.GetUtcNow().UtcDateTime,
                        cancellationToken);
                }

                if (runBudget.WindowClosed)
                {
                    // The rest keep their unset timestamps and stay at the head of the queue.
                    // Saying so is what Truncated is for: a status consumer can tell a cycle
                    // that finished its list from one the clock took the rest of it off.
                    if (index < candidates.Count - 1)
                    {
                        state = MetadataRefreshRunState.Truncated;
                    }

                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            state = MetadataRefreshRunState.Cancelled;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            state = MetadataRefreshRunState.Failed;
            _logger.LogError(ex, "Metadata refresh run {RunId} failed", run.RunId);
        }
        finally
        {
            run.RequestsSpent = budget?.RequestsSpent ?? 0;
            run.Finish(state, _timeProvider.GetUtcNow().UtcDateTime);
            lock (_stateGate)
            {
                _last = run;
                _active = null;
                _running = false;
            }
        }
    }
}
