/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Listenarr.Infrastructure.Persistence;

/// <summary>
/// Makes a monitored book due for automatic search now when a save changes something its search
/// query or result acceptance is built from: the title, the first author, the series, the ASIN,
/// the quality profile, or monitoring switched on. The book's failure count goes to zero and its
/// per-indexer rows are deleted, so every indexer is asked again instead of waiting out a backoff
/// earned by the old metadata. Hooking every save, rather than each workflow, covers all the
/// places that write these fields.
/// </summary>
/// <remarks>
/// The schedule columns are ignored after save, so on a relational provider the reset is two
/// set-based statements run after the save, in the save's transaction or in one this interceptor
/// opens, and the edit and the reset commit together or not at all. EF InMemory has neither
/// set-based statements nor transactions; there the reset rides on the save as tracked changes.
/// </remarks>
public sealed class AutomaticSearchResetInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, PendingReset> _pending = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not ListenArrDbContext context
            || CollectResetIds(context) is not { Count: > 0 } ids)
        {
            return result;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (!context.Database.IsRelational())
        {
            ResetTracked(context, ids, nowUtc, PairRows(context, ids).ToList());
            return result;
        }

        var transaction = context.Database.CurrentTransaction == null
            ? context.Database.BeginTransaction()
            : null;
        _pending.AddOrUpdate(context, new PendingReset(ids, nowUtc, transaction));
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not ListenArrDbContext context
            || CollectResetIds(context) is not { Count: > 0 } ids)
        {
            return result;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (!context.Database.IsRelational())
        {
            ResetTracked(context, ids, nowUtc, await PairRows(context, ids).ToListAsync(cancellationToken));
            return result;
        }

        var transaction = context.Database.CurrentTransaction == null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        _pending.AddOrUpdate(context, new PendingReset(ids, nowUtc, transaction));
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (!TryTake(eventData.Context, out var context, out var pending))
        {
            return result;
        }

        try
        {
            PairRows(context, pending.Ids).ExecuteDelete();
            BooksToReset(context, pending.Ids).ExecuteUpdate(setters => setters
                .SetProperty(book => book.AutomaticSearchDueUtc, pending.NowUtc)
                .SetProperty(book => book.AutomaticSearchFailureCount, 0));
            pending.Transaction?.Commit();
        }
        catch
        {
            pending.Transaction?.Rollback();
            throw;
        }
        finally
        {
            pending.Transaction?.Dispose();
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (!TryTake(eventData.Context, out var context, out var pending))
        {
            return result;
        }

        try
        {
            await PairRows(context, pending.Ids).ExecuteDeleteAsync(cancellationToken);
            await BooksToReset(context, pending.Ids).ExecuteUpdateAsync(setters => setters
                .SetProperty(book => book.AutomaticSearchDueUtc, pending.NowUtc)
                .SetProperty(book => book.AutomaticSearchFailureCount, 0),
                cancellationToken);
            if (pending.Transaction != null)
            {
                await pending.Transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (pending.Transaction != null)
            {
                await pending.Transaction.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
        finally
        {
            if (pending.Transaction != null)
            {
                await pending.Transaction.DisposeAsync();
            }
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) =>
        Discard(eventData.Context);

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default) =>
        DiscardAsync(eventData.Context);

    public override void SaveChangesCanceled(DbContextEventData eventData) =>
        Discard(eventData.Context);

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default) =>
        DiscardAsync(eventData.Context);

    private static List<int> CollectResetIds(ListenArrDbContext context)
    {
        var ids = new List<int>();
        foreach (var entry in context.ChangeTracker.Entries<Audiobook>())
        {
            // Only books that are monitored after the save are reset; an unmonitored book is reset
            // when monitoring is switched back on.
            if (entry.State == EntityState.Modified
                && entry.Entity.Monitored
                && ChangesSearchInputs(entry))
            {
                ids.Add(entry.Entity.Id);
            }
        }

        return ids;
    }

    // Original and current values are compared rather than IsModified, so a save that writes back
    // an identical value (a rescan returning the same title) resets nothing.
    private static bool ChangesSearchInputs(EntityEntry<Audiobook> entry) =>
        Changed(entry, book => book.Title)
        || !string.Equals(
            entry.Property(book => book.Authors).OriginalValue?.FirstOrDefault(),
            entry.Entity.Authors?.FirstOrDefault(),
            StringComparison.Ordinal)
        || Changed(entry, book => book.Series)
        || Changed(entry, book => book.Asin)
        || Changed(entry, book => book.QualityProfileId)
        || (!entry.Property(book => book.Monitored).OriginalValue && entry.Entity.Monitored);

    private static bool Changed<TValue>(
        EntityEntry<Audiobook> entry,
        Expression<Func<Audiobook, TValue>> property)
    {
        var values = entry.Property(property);
        return !EqualityComparer<TValue>.Default.Equals(values.OriginalValue, values.CurrentValue);
    }

    // Runs inside SavingChanges, so the save that is already under way carries the reset and no
    // second save is needed. The pair rows are queried with tracking, so rows this context had
    // not loaded are deleted too.
    private static void ResetTracked(
        ListenArrDbContext context,
        List<int> ids,
        DateTime nowUtc,
        List<AudiobookIndexerSearchState> pairRows)
    {
        foreach (var entry in context.ChangeTracker.Entries<Audiobook>()
                     .Where(candidate => ids.Contains(candidate.Entity.Id)))
        {
            entry.Property(book => book.AutomaticSearchDueUtc).CurrentValue = nowUtc;
            entry.Property(book => book.AutomaticSearchFailureCount).CurrentValue = 0;
        }

        context.AudiobookIndexerSearchStates.RemoveRange(pairRows);
    }

    private static IQueryable<AudiobookIndexerSearchState> PairRows(ListenArrDbContext context, List<int> ids) =>
        context.AudiobookIndexerSearchStates.Where(pair => ids.Contains(pair.AudiobookId));

    private static IQueryable<Audiobook> BooksToReset(ListenArrDbContext context, List<int> ids) =>
        context.Audiobooks.Where(book => ids.Contains(book.Id));

    private bool TryTake(
        DbContext? eventContext,
        [NotNullWhen(true)] out ListenArrDbContext? context,
        [NotNullWhen(true)] out PendingReset? pending)
    {
        context = eventContext as ListenArrDbContext;
        pending = null;
        if (context == null || !_pending.TryGetValue(context, out pending))
        {
            return false;
        }

        _pending.Remove(context);
        return true;
    }

    // The save failed or was cancelled before the reset ran: disposing the transaction this
    // interceptor opened rolls the edit back.
    private void Discard(DbContext? eventContext)
    {
        if (TryTake(eventContext, out _, out var pending))
        {
            pending.Transaction?.Dispose();
        }
    }

    private async Task DiscardAsync(DbContext? eventContext)
    {
        if (TryTake(eventContext, out _, out var pending) && pending.Transaction != null)
        {
            await pending.Transaction.DisposeAsync();
        }
    }

    private sealed record PendingReset(List<int> Ids, DateTime NowUtc, IDbContextTransaction? Transaction);
}
