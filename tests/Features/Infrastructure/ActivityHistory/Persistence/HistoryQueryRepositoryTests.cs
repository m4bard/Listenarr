/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.ActivityHistory.Persistence
{
    public sealed class HistoryQueryRepositoryTests : IDisposable
    {
        private readonly ListenArrDbContext _db;
        private readonly EfHistoryRepository _repository;

        public HistoryQueryRepositoryTests()
        {
            _db = new ListenArrDbContext(new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
            _repository = new EfHistoryRepository(_db);
        }

        [Fact]
        public async Task QueryAsync_FiltersSortsAndPagesCanonicalHistory()
        {
            _db.History.AddRange(
                Entry("corr-1", HistoryEvents.ImportStarted, HistoryOutcome.Requested, DateTime.UtcNow.AddMinutes(-3)),
                Entry("corr-1", HistoryEvents.ImportRetry, HistoryOutcome.Retrying, DateTime.UtcNow.AddMinutes(-2)),
                Entry("corr-1", HistoryEvents.Imported, HistoryOutcome.Succeeded, DateTime.UtcNow.AddMinutes(-1)),
                Entry("corr-2", HistoryEvents.Imported, HistoryOutcome.Succeeded, DateTime.UtcNow));
            await _db.SaveChangesAsync();

            var page = await _repository.QueryAsync(new HistoryQuery
            {
                CorrelationId = "corr-1",
                SortDirection = "asc",
                Limit = 2
            });

            Assert.Equal(3, page.Total);
            Assert.Equal(2, page.Records.Count);
            Assert.Equal(HistoryEvents.ImportStarted, page.Records[0].EventType);
            Assert.Equal(HistoryEvents.ImportRetry, page.Records[1].EventType);
        }

        [Fact]
        public async Task DeleteAllAsync_RemovesAllHistoryIncludingFormerMoveScanRequests()
        {
            _db.History.AddRange(
                new History
                {
                    CorrelationId = "move:pending",
                    EventType = HistoryEvents.ScanQueued,
                    Outcome = HistoryOutcome.Requested,
                    Source = "Move"
                },
                Entry("ordinary", HistoryEvents.Imported, HistoryOutcome.Succeeded, DateTime.UtcNow));
            await _db.SaveChangesAsync();

            await _repository.DeleteAllAsync();

            Assert.Empty(await _db.History.AsNoTracking().ToListAsync());
        }

        [Fact]
        public async Task DeleteAsync_FormerMoveScanRequest_IsOrdinaryHistory()
        {
            var handoff = new History
            {
                CorrelationId = "move:protected",
                EventType = HistoryEvents.ScanQueued,
                Outcome = HistoryOutcome.Requested,
                Source = "Move"
            };
            _db.History.Add(handoff);
            await _db.SaveChangesAsync();

            Assert.True(await _repository.DeleteAsync(handoff.Id));
            Assert.False(await _db.History.AnyAsync(history => history.Id == handoff.Id));
        }

        [Fact]
        public async Task DeleteAsync_TerminalScan_DoesNotDeleteOtherAuditEvents()
        {
            var handoff = new History
            {
                CorrelationId = "move:terminal",
                EventType = HistoryEvents.ScanQueued,
                Outcome = HistoryOutcome.Requested,
                Source = "Move"
            };
            var terminal = new History
            {
                CorrelationId = "move:terminal",
                EventType = HistoryEvents.ScanCompleted,
                Outcome = HistoryOutcome.Succeeded,
                Source = "LibraryScan"
            };
            _db.History.AddRange(handoff, terminal);
            await _db.SaveChangesAsync();

            Assert.True(await _repository.DeleteAsync(terminal.Id));
            Assert.True(await _db.History.AnyAsync(history => history.Id == handoff.Id));
            Assert.False(await _db.History.AnyAsync(history => history.Id == terminal.Id));
        }

        // A filter preset covers a group of event types. Expressed as one query the total and the
        // paging stay true; fired as one request per type neither can be, because there is no
        // way to add up several independently paged results without over- or under-counting.
        [Fact]
        public async Task QueryAsync_MatchesAnyOfSeveralEventTypesInOneQuery()
        {
            _db.History.AddRange(
                Entry("a", HistoryEvents.DownloadFailed, HistoryOutcome.Failed, DateTime.UtcNow.AddMinutes(-4)),
                Entry("b", HistoryEvents.Removed, HistoryOutcome.Succeeded, DateTime.UtcNow.AddMinutes(-3)),
                Entry("c", HistoryEvents.Imported, HistoryOutcome.Succeeded, DateTime.UtcNow.AddMinutes(-2)),
                Entry("d", HistoryEvents.Grabbed, HistoryOutcome.Succeeded, DateTime.UtcNow.AddMinutes(-1)));
            await _db.SaveChangesAsync();

            var page = await _repository.QueryAsync(new HistoryQuery
            {
                EventTypes = [HistoryEvents.DownloadFailed, HistoryEvents.Removed]
            });

            Assert.Equal(2, page.Total);
            Assert.Equal(
                new[] { HistoryEvents.Removed, HistoryEvents.DownloadFailed },
                page.Records.Select(record => record.EventType));
        }

        // The total has to count the whole filtered set, not the page that came back, or a pager
        // built from it says there is one page however much history there is.
        [Fact]
        public async Task QueryAsync_TotalCountsEveryMatchNotJustTheReturnedPage()
        {
            for (var i = 0; i < 7; i++)
            {
                _db.History.Add(Entry($"c{i}", HistoryEvents.Grabbed, HistoryOutcome.Succeeded, DateTime.UtcNow.AddMinutes(-i)));
            }
            await _db.SaveChangesAsync();

            var page = await _repository.QueryAsync(new HistoryQuery
            {
                EventTypes = [HistoryEvents.Grabbed],
                Limit = 3
            });

            Assert.Equal(7, page.Total);
            Assert.Equal(3, page.Records.Count);
        }

        [Fact]
        public async Task QueryAsync_SingleEventTypeStillMatchesExactly()
        {
            _db.History.AddRange(
                Entry("a", HistoryEvents.Grabbed, HistoryOutcome.Succeeded, DateTime.UtcNow.AddMinutes(-1)),
                Entry("b", HistoryEvents.Imported, HistoryOutcome.Succeeded, DateTime.UtcNow));
            await _db.SaveChangesAsync();

            var page = await _repository.QueryAsync(new HistoryQuery { EventType = HistoryEvents.Grabbed });

            Assert.Equal(1, page.Total);
            Assert.Equal(HistoryEvents.Grabbed, Assert.Single(page.Records).EventType);
        }

        // An event type with a space in it is not a special case to the filter, and several of
        // the real ones have one.
        [Fact]
        public async Task QueryAsync_MatchesEventTypesThatContainSpaces()
        {
            _db.History.AddRange(
                Entry("a", "File Added", HistoryOutcome.Succeeded, DateTime.UtcNow.AddMinutes(-2)),
                Entry("b", "File Removed", HistoryOutcome.Succeeded, DateTime.UtcNow.AddMinutes(-1)),
                Entry("c", HistoryEvents.Grabbed, HistoryOutcome.Succeeded, DateTime.UtcNow));
            await _db.SaveChangesAsync();

            var page = await _repository.QueryAsync(new HistoryQuery
            {
                EventTypes = ["File Added", "File Removed"]
            });

            Assert.Equal(2, page.Total);
        }

        [Fact]
        public async Task QueryAsync_EmptyEventTypeSetDoesNotFilterAnythingOut()
        {
            _db.History.AddRange(
                Entry("a", HistoryEvents.Grabbed, HistoryOutcome.Succeeded, DateTime.UtcNow.AddMinutes(-1)),
                Entry("b", HistoryEvents.Imported, HistoryOutcome.Succeeded, DateTime.UtcNow));
            await _db.SaveChangesAsync();

            var page = await _repository.QueryAsync(new HistoryQuery { EventTypes = [] });

            Assert.Equal(2, page.Total);
        }

        private static History Entry(string correlationId, string eventType, HistoryOutcome outcome, DateTime timestamp) =>
            new()
            {
                CorrelationId = correlationId,
                EventType = eventType,
                Outcome = outcome,
                Timestamp = timestamp,
                DownloadId = correlationId
            };

        public void Dispose() => _db.Dispose();
    }
}
