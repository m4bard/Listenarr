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
using Listenarr.Application.Search.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Listenarr.Infrastructure.Persistence.Repositories;

// Every relational write here is set-based. The book's schedule columns are ignored after save,
// so on SQLite a tracked save of them would be dropped without an error; tracked saves are used
// only on providers without set-based updates (EF InMemory), where they land. Each write runs
// under a non-cancellable token once started, so an outcome already in hand is not lost to a
// cycle cancelled while it is being written.
public sealed partial class EfAutomaticSearchScheduleStore
{
    public async Task PersistPairAsync(
        PairScheduleUpdate update,
        DateTime nowUtc,
        TimeSpan cooldown,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.NotBeforeUtc == null
            && update.Result is PairScheduleResult.GrabNotCompleted or PairScheduleResult.Deferred)
        {
            throw new ArgumentException(
                $"A {update.Result} pair needs the time it may be searched again.", nameof(update));
        }

        var completionToken = RequestCancellationBoundary.EnterNonCancelablePhase(ct);
        await using var transaction = await BeginOwnTransactionAsync(completionToken);
        var current = await db.AudiobookIndexerSearchStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                pair => pair.AudiobookId == update.AudiobookId && pair.IndexerId == update.IndexerId,
                completionToken);
        var next = NextPairState(update, current, nowUtc, cooldown);
        if (db.Database.IsRelational())
        {
            await UpsertPairSetBasedAsync(next, completionToken);
        }
        else
        {
            await UpsertPairTrackedAsync(next, completionToken);
        }

        if (transaction != null)
        {
            await transaction.CommitAsync(completionToken);
        }
    }

    public async Task PersistBookAsync(
        BookScheduleUpdate update,
        DateTime nowUtc,
        TimeSpan cooldown,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var completionToken = RequestCancellationBoundary.EnterNonCancelablePhase(ct);
        await using var transaction = await BeginOwnTransactionAsync(completionToken);
        var storedFailureCount = await db.Audiobooks
            .AsNoTracking()
            .Where(book => book.Id == update.AudiobookId)
            .Select(book => (int?)book.AutomaticSearchFailureCount)
            .SingleOrDefaultAsync(completionToken);
        if (storedFailureCount is not { } failureCount)
        {
            return;
        }

        var (dueUtc, nextFailureCount) = update.Outcome switch
        {
            BookSearchOutcome.Satisfied or BookSearchOutcome.NoAcceptableResults =>
                (await EarliestPairAttemptAsync(update.AudiobookId, nowUtc, cooldown, completionToken), 0),
            BookSearchOutcome.PartiallyCompleted or BookSearchOutcome.Failed or BookSearchOutcome.Deferred =>
                (await EarliestPairAttemptAsync(update.AudiobookId, nowUtc, cooldown, completionToken), failureCount),
            BookSearchOutcome.GrabNotCompleted when update.NotBeforeUtc is { } deferredUntilUtc =>
                (deferredUntilUtc, failureCount),
            BookSearchOutcome.GrabNotCompleted or BookSearchOutcome.BookFailed =>
                (AutomaticSearchSchedulePolicy.NextBookFailureAttempt(nowUtc, failureCount + 1, cooldown), failureCount + 1),
            BookSearchOutcome.NotNeeded =>
                (Later(nowUtc.Add(cooldown), await EarliestPairAttemptAsync(update.AudiobookId, nowUtc, cooldown, completionToken)),
                    failureCount),
            BookSearchOutcome.Cancelled => ((DateTime?)null, failureCount),
            _ => throw new ArgumentOutOfRangeException(nameof(update), update.Outcome, "Unknown book outcome.")
        };
        DateTime? lastSearchUtc = update.AnyPairCompleted ? nowUtc : null;

        if (db.Database.IsRelational())
        {
            await db.Audiobooks
                .Where(book => book.Id == update.AudiobookId)
                .ExecuteUpdateAsync(
                    setters =>
                    {
                        setters.SetProperty(book => book.AutomaticSearchFailureCount, nextFailureCount);
                        if (dueUtc is { } due)
                        {
                            setters.SetProperty(book => book.AutomaticSearchDueUtc, due);
                        }

                        if (lastSearchUtc != null)
                        {
                            setters.SetProperty(book => book.LastSearchTime, lastSearchUtc);
                        }
                    },
                    completionToken);
        }
        else
        {
            var book = await db.Audiobooks.FindAsync([update.AudiobookId], completionToken);
            book!.AutomaticSearchFailureCount = nextFailureCount;
            book.AutomaticSearchDueUtc = dueUtc ?? book.AutomaticSearchDueUtc;
            book.LastSearchTime = lastSearchUtc ?? book.LastSearchTime;
            await db.SaveChangesAsync(completionToken);
        }

        if (transaction != null)
        {
            await transaction.CommitAsync(completionToken);
        }
    }

    private static AudiobookIndexerSearchState NextPairState(
        PairScheduleUpdate update,
        AudiobookIndexerSearchState? current,
        DateTime nowUtc,
        TimeSpan cooldown)
    {
        var next = new AudiobookIndexerSearchState
        {
            AudiobookId = update.AudiobookId,
            IndexerId = update.IndexerId,
            NoResultCount = current?.NoResultCount ?? 0,
            FailureCount = current?.FailureCount ?? 0,
            LastOutcome = update.Result.ToString(),
            LastCompletedUtc = current?.LastCompletedUtc
        };

        switch (update.Result)
        {
            case PairScheduleResult.Satisfied:
                next.NoResultCount = 0;
                next.FailureCount = 0;
                next.NextAttemptUtc = nowUtc.Add(cooldown);
                next.LastCompletedUtc = nowUtc;
                break;
            case PairScheduleResult.NoAcceptableResults:
                next.NoResultCount++;
                next.FailureCount = 0;
                next.NextAttemptUtc = AutomaticSearchSchedulePolicy.NextNoResultAttempt(nowUtc, next.NoResultCount, cooldown);
                next.LastCompletedUtc = nowUtc;
                break;
            case PairScheduleResult.GrabNotCompleted:
                next.NextAttemptUtc = update.NotBeforeUtc!.Value;
                next.LastCompletedUtc = nowUtc;
                break;
            case PairScheduleResult.Failed:
                next.FailureCount++;
                next.NextAttemptUtc = Later(
                    AutomaticSearchSchedulePolicy.NextPairFailureAttempt(nowUtc, next.FailureCount),
                    update.NotBeforeUtc);
                break;
            case PairScheduleResult.Deferred:
                next.NextAttemptUtc = update.NotBeforeUtc!.Value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(update), update.Result, "Unknown pair result.");
        }

        return next;
    }

    private async Task UpsertPairSetBasedAsync(AudiobookIndexerSearchState next, CancellationToken ct)
    {
        var updated = await db.AudiobookIndexerSearchStates
            .Where(pair => pair.AudiobookId == next.AudiobookId && pair.IndexerId == next.IndexerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(pair => pair.NextAttemptUtc, next.NextAttemptUtc)
                    .SetProperty(pair => pair.NoResultCount, next.NoResultCount)
                    .SetProperty(pair => pair.FailureCount, next.FailureCount)
                    .SetProperty(pair => pair.LastOutcome, next.LastOutcome)
                    .SetProperty(pair => pair.LastCompletedUtc, next.LastCompletedUtc),
                ct);
        if (updated == 0)
        {
            // A statement rather than a tracked Add, so the write never flushes other pending
            // changes of a context the store shares with its caller.
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "AudiobookIndexerSearchStates"
                    ("AudiobookId", "IndexerId", "NextAttemptUtc", "NoResultCount", "FailureCount", "LastOutcome", "LastCompletedUtc")
                VALUES ({next.AudiobookId}, {next.IndexerId}, {next.NextAttemptUtc}, {next.NoResultCount},
                    {next.FailureCount}, {next.LastOutcome}, {next.LastCompletedUtc})
                """,
                ct);
        }
    }

    private async Task UpsertPairTrackedAsync(AudiobookIndexerSearchState next, CancellationToken ct)
    {
        var tracked = await db.AudiobookIndexerSearchStates.FindAsync([next.AudiobookId, next.IndexerId], ct);
        if (tracked == null)
        {
            db.AudiobookIndexerSearchStates.Add(next);
        }
        else
        {
            db.Entry(tracked).CurrentValues.SetValues(next);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The earliest next attempt over the indexers enabled for automatic search, an indexer with no
    /// pair row counting as <paramref name="nowUtc"/>. With no such indexer the book waits the cooldown.
    /// </summary>
    private async Task<DateTime> EarliestPairAttemptAsync(
        int audiobookId,
        DateTime nowUtc,
        TimeSpan cooldown,
        CancellationToken ct)
    {
        var enabledIndexerIds = await db.Indexers
            .AsNoTracking()
            .Where(indexer => indexer.IsEnabled && indexer.EnableAutomaticSearch)
            .Select(indexer => indexer.Id)
            .ToListAsync(ct);
        if (enabledIndexerIds.Count == 0)
        {
            return nowUtc.Add(cooldown);
        }

        var nextAttempts = await db.AudiobookIndexerSearchStates
            .AsNoTracking()
            .Where(pair => pair.AudiobookId == audiobookId && enabledIndexerIds.Contains(pair.IndexerId))
            .Select(pair => pair.NextAttemptUtc)
            .ToListAsync(ct);
        return nextAttempts.Count < enabledIndexerIds.Count
            ? nextAttempts.Append(nowUtc).Min()
            : nextAttempts.Min();
    }

    private async Task<IDbContextTransaction?> BeginOwnTransactionAsync(CancellationToken ct) =>
        db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

    private static DateTime Later(DateTime first, DateTime? second) =>
        second is { } value && value > first ? value : first;
}
