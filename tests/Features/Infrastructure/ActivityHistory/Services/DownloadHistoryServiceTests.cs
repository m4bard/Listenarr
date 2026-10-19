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

namespace Listenarr.Tests.Features.Infrastructure.ActivityHistory.Services
{
    public class DownloadHistoryServiceTests : IDisposable
    {
        private readonly ListenArrDbContext _context;
        private readonly DownloadHistoryService _service;
        private readonly Mock<IDownloadClientAdapterFactory> _adapterFactory = new();

        public DownloadHistoryServiceTests()
        {
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            _context = new ListenArrDbContext(options);
            _adapterFactory
                .Setup(factory => factory.GetByType(It.IsAny<string>()))
                .Throws(() => new InvalidOperationException("no adapter registered for this type"));
            _service = new DownloadHistoryService(
                _context,
                new Mock<ILogger<DownloadHistoryService>>().Object,
                _adapterFactory.Object);
        }

        // Registers a download client of the given type and teaches the adapter factory what
        // protocol that type speaks, which is the same pairing the queue and submission paths use.
        private async Task GivenDownloadClient(string clientId, string type, DownloadProtocol protocol)
        {
            _context.DownloadClientConfigurations.Add(new DownloadClientConfiguration
            {
                Id = clientId,
                Name = clientId,
                Type = type
            });
            await _context.SaveChangesAsync();

            var adapter = new Mock<IDownloadClientAdapter>();
            adapter.SetupGet(a => a.Protocol).Returns(protocol);
            _adapterFactory.Setup(factory => factory.GetByType(type)).Returns(adapter.Object);
        }

        [Fact]
        public async Task RecordGrabbedAsync_WritesCanonicalHistory()
        {
            await _service.RecordGrabbedAsync(
                "abc123",
                "client-1",
                "Test Book",
                DownloadProtocol.Torrent);

            var entry = Assert.Single(_context.History);
            Assert.Equal("ABC123", entry.DownloadId);
            Assert.Equal(HistoryEvents.Grabbed, entry.EventType);
            Assert.Equal(HistoryOutcome.Succeeded, entry.Outcome);
            Assert.Equal("Test Book", entry.SourceTitle);
        }

        [Fact]
        public async Task RecordGrabbedAsync_AttachesAudiobookIdToTheCanonicalRow()
        {
            await _service.RecordGrabbedAsync(
                "abc123",
                "client-1",
                "Test Book",
                DownloadProtocol.Torrent,
                audiobookId: 42);

            var entry = Assert.Single(_context.History);
            Assert.Equal(42, entry.AudiobookId);
        }

        [Fact]
        public async Task RecordGrabbedAsync_IsVisibleToThePerBookHistoryQuery()
        {
            await _service.RecordGrabbedAsync(
                "abc123",
                "client-1",
                "Test Book",
                DownloadProtocol.Torrent,
                audiobookId: 42);

            var repository = new EfHistoryRepository(_context);
            var forBook = await repository.GetByAudiobookIdAsync(42);

            var entry = Assert.Single(forBook);
            Assert.Equal("ABC123", entry.DownloadId);
            Assert.Equal(HistoryEvents.Grabbed, entry.EventType);
        }

        [Fact]
        public async Task RecordImportedAsync_IsVisibleToThePerBookHistoryQueryOfThatBookOnly()
        {
            await _service.RecordImportedAsync("abc123", "client-1", "Test Book", audiobookId: 42);
            await _service.RecordImportedAsync("def456", "client-1", "Other Book", audiobookId: 43);

            var repository = new EfHistoryRepository(_context);

            var entry = Assert.Single(await repository.GetByAudiobookIdAsync(42));
            Assert.Equal("ABC123", entry.DownloadId);
            Assert.Equal(HistoryEvents.Imported, entry.EventType);

            Assert.Empty(await repository.GetByAudiobookIdAsync(99));
        }

        [Fact]
        public async Task RecordGrabbedAsync_WithoutAudiobookIdLeavesTheRowUnattached()
        {
            await _service.RecordGrabbedAsync("abc123", "client-1", "Test Book", DownloadProtocol.Torrent);

            var entry = Assert.Single(_context.History);
            Assert.Null(entry.AudiobookId);
        }

        [Fact]
        public async Task RecordImportedAsync_IsAppendOnlyAndDrivesIdempotency()
        {
            await _service.RecordGrabbedAsync("abc123", "client-1", "Test Book", DownloadProtocol.Torrent);
            await _service.RecordImportedAsync("abc123", "client-1", "Test Book");

            Assert.Equal(2, await _context.History.CountAsync());
            Assert.True(await _service.IsAlreadyImportedAsync("AbC123", "client-1"));
            Assert.Equal(HistoryEvents.Imported, (await _service.GetLatestEventAsync("abc123", "client-1"))!.EventType.ToString());
        }

        [Fact]
        public async Task RecordFailure_PreservesErrorAndFailedOutcome()
        {
            await _service.RecordImportFailedAsync("abc123", "client-1", "Test Book", "permission denied");

            var entry = Assert.Single(_context.History);
            Assert.Equal(HistoryEvents.ImportFailed, entry.EventType);
            Assert.Equal(HistoryOutcome.Failed, entry.Outcome);
            Assert.Equal("permission denied", entry.Error);
        }

        [Fact]
        public async Task GetHistoryAsync_ReturnsEventsInChronologicalOrder()
        {
            await _service.RecordGrabbedAsync("abc123", "client-1", "Test Book", DownloadProtocol.Torrent);
            await _service.RecordDownloadCompleteAsync("abc123", "client-1", "Test Book");
            await _service.RecordImportedAsync("abc123", "client-1", "Test Book");

            var history = await _service.GetHistoryAsync("abc123", "client-1");

            Assert.Collection(
                history,
                entry => Assert.Equal(DownloadHistoryEventType.Grabbed, entry.EventType),
                entry => Assert.Equal(DownloadHistoryEventType.DownloadCompleted, entry.EventType),
                entry => Assert.Equal(DownloadHistoryEventType.Imported, entry.EventType));
        }

        [Fact]
        public async Task HasRecentGrabbedAsync_UsesCanonicalHistory()
        {
            await _service.RecordGrabbedAsync("abc123", "client-1", "Test Book", DownloadProtocol.Torrent);

            Assert.True(await _service.HasRecentGrabbedAsync("abc123", "client-1"));
            Assert.False(await _service.HasRecentGrabbedAsync("abc123", "other-client"));
        }

        [Fact]
        public async Task CleanupOldEntriesAsync_DeletesOnlyDownloadHistoryBeforeCutoff()
        {
            _context.History.AddRange(
                new History
                {
                    DownloadId = "OLD",
                    DownloadClientId = "client-1",
                    EventType = HistoryEvents.Grabbed,
                    Timestamp = DateTime.UtcNow.AddDays(-120),
                    CorrelationId = "old"
                },
                new History
                {
                    EventType = HistoryEvents.LibraryUpdated,
                    Timestamp = DateTime.UtcNow.AddDays(-120),
                    CorrelationId = "library"
                },
                new History
                {
                    DownloadId = "RECENT",
                    DownloadClientId = "client-1",
                    EventType = HistoryEvents.Grabbed,
                    Timestamp = DateTime.UtcNow.AddDays(-1),
                    CorrelationId = "recent"
                });
            await _context.SaveChangesAsync();

            var deleted = await _service.CleanupOldEntriesAsync(90);

            Assert.Equal(1, deleted);
            Assert.Equal(2, await _context.History.CountAsync());
        }

        [Fact]
        public async Task CleanupOldEntriesAsync_ZeroRetentionPreservesAllHistory()
        {
            await _service.RecordGrabbedAsync(
                "unlimited-history",
                "client-1",
                "Unlimited History",
                DownloadProtocol.Torrent);

            var deleted = await _service.CleanupOldEntriesAsync();

            Assert.Equal(0, deleted);
            Assert.Single(await _service.GetHistoryAsync("unlimited-history", "client-1"));
        }

        // Every Record method hardcoded DownloadClient = "Unknown". The string is not blank, so
        // AddUnifiedAsync's fallback never fired and "Unknown" is what reached History.Source,
        // which is a sortable column, a filterable field, and a column on the History page. The
        // rows people most want to read were the ones that said nothing.
        [Fact]
        public async Task RecordGrabbedAsync_NamesTheDownloadClientInsteadOfWritingUnknown()
        {
            await GivenDownloadClient("client-1", "qbittorrent", DownloadProtocol.Torrent);

            await _service.RecordGrabbedAsync(
                "abc123",
                "client-1",
                "Test Book",
                DownloadProtocol.Torrent);

            var entry = Assert.Single(_context.History);
            Assert.Equal("client-1", entry.Source);
            Assert.NotEqual("Unknown", entry.Source);
        }

        [Theory]
        [InlineData("completed")]
        [InlineData("failed")]
        [InlineData("imported")]
        [InlineData("importfailed")]
        [InlineData("paused")]
        [InlineData("resumed")]
        [InlineData("removed")]
        public async Task EveryRecordMethod_NamesTheDownloadClient(string which)
        {
            _context.DownloadClientConfigurations.Add(new DownloadClientConfiguration
            {
                Id = "client-1",
                Name = "Living Room SAB",
                Type = "sabnzbd"
            });
            await _context.SaveChangesAsync();

            var adapter = new Mock<IDownloadClientAdapter>();
            adapter.SetupGet(a => a.Protocol).Returns(DownloadProtocol.Usenet);
            _adapterFactory.Setup(factory => factory.GetByType("sabnzbd")).Returns(adapter.Object);

            switch (which)
            {
                case "completed":
                    await _service.RecordDownloadCompleteAsync("abc123", "client-1", "Test Book");
                    break;
                case "failed":
                    await _service.RecordDownloadFailedAsync("abc123", "client-1", "Test Book", "nope");
                    break;
                case "imported":
                    await _service.RecordImportedAsync("abc123", "client-1", "Test Book");
                    break;
                case "importfailed":
                    await _service.RecordImportFailedAsync("abc123", "client-1", "Test Book", "nope");
                    break;
                case "paused":
                    await _service.RecordPausedAsync("abc123", "client-1", "Test Book");
                    break;
                case "resumed":
                    await _service.RecordResumedAsync("abc123", "client-1", "Test Book");
                    break;
                default:
                    await _service.RecordRemovedAsync("abc123", "client-1", "Test Book");
                    break;
            }

            var entry = Assert.Single(_context.History);
            Assert.Equal("Living Room SAB", entry.Source);
        }

        // A client that has been deleted leaves the name blank rather than filling it with a
        // literal, so AddUnifiedAsync's own fallback is what decides, which is what it was
        // written to do.
        [Fact]
        public async Task RecordGrabbedAsync_UnresolvableClientFallsBackToTheGenericSource()
        {
            await _service.RecordGrabbedAsync(
                "abc123",
                "gone-client",
                "Test Book",
                DownloadProtocol.Torrent);

            var entry = Assert.Single(_context.History);
            Assert.Equal("Download", entry.Source);
        }

        // The grab path holds the indexer, the quality and the size on the search result it is
        // submitting, and threw all three away. Without them a grab row cannot say where a file
        // came from or what was expected of it.
        [Fact]
        public async Task RecordGrabbedAsync_KeepsTheIndexerQualityAndSizeOfTheRelease()
        {
            await GivenDownloadClient("client-1", "qbittorrent", DownloadProtocol.Torrent);

            await _service.RecordGrabbedAsync(
                "abc123",
                "client-1",
                "Test Book",
                DownloadProtocol.Torrent,
                audiobookId: 42,
                indexer: "Example Indexer",
                quality: "M4B 128kbps",
                size: 734003200L);

            var entry = Assert.Single(_context.History);
            Assert.Equal("Example Indexer", entry.Indexer);
            Assert.Equal("M4B 128kbps", entry.Quality);
            Assert.Equal(734003200L, entry.Size);
        }

        // A search result that reported no size carries zero, not null, and storing zero would
        // render as an empty release rather than as one whose size nobody knows.
        [Fact]
        public async Task RecordGrabbedAsync_TreatsAnUnreportedSizeAsUnknownRatherThanEmpty()
        {
            await GivenDownloadClient("client-1", "qbittorrent", DownloadProtocol.Torrent);

            await _service.RecordGrabbedAsync(
                "abc123",
                "client-1",
                "Test Book",
                DownloadProtocol.Torrent,
                indexer: "   ",
                quality: null,
                size: 0L);

            var entry = Assert.Single(_context.History);
            Assert.Null(entry.Indexer);
            Assert.Null(entry.Quality);
            Assert.Null(entry.Size);
        }

        // Only a grab has a release behind it. Filling these in for a completion or an import
        // would be inventing provenance the event never had.
        [Fact]
        public async Task EventsWithNoReleaseBehindThemLeaveTheReleaseColumnsNull()
        {
            await GivenDownloadClient("client-1", "qbittorrent", DownloadProtocol.Torrent);

            await _service.RecordDownloadCompleteAsync("abc123", "client-1", "Test Book");

            var entry = Assert.Single(_context.History);
            Assert.Null(entry.Indexer);
            Assert.Null(entry.Quality);
            Assert.Null(entry.Size);
        }

        public void Dispose() => _context.Dispose();

        // Every one of these three methods wrote DownloadProtocol.Torrent regardless of the client,
        // so a Usenet event was filed as a torrent. Torrent is also the zero member of the enum,
        // which is why the wrong value was invisible: it is what an unset field reads as.
        [Theory]
        [InlineData("sabnzbd", DownloadProtocol.Usenet)]
        [InlineData("nzbget", DownloadProtocol.Usenet)]
        [InlineData("qbittorrent", DownloadProtocol.Torrent)]
        [InlineData("transmission", DownloadProtocol.Torrent)]
        public async Task RecordDownloadFailedAsync_RecordsTheProtocolTheClientSpeaks(
            string clientType,
            DownloadProtocol expected)
        {
            await GivenDownloadClient("client-1", clientType, expected);

            await _service.RecordDownloadFailedAsync("abc123", "client-1", "Test Book", "refused");

            Assert.Equal(expected, Assert.Single(_context.History).Protocol);
        }

        [Theory]
        [InlineData("sabnzbd", DownloadProtocol.Usenet)]
        [InlineData("qbittorrent", DownloadProtocol.Torrent)]
        public async Task RecordDownloadCompleteAsync_RecordsTheProtocolTheClientSpeaks(
            string clientType,
            DownloadProtocol expected)
        {
            await GivenDownloadClient("client-1", clientType, expected);

            await _service.RecordDownloadCompleteAsync("abc123", "client-1", "Test Book");

            Assert.Equal(expected, Assert.Single(_context.History).Protocol);
        }

        [Theory]
        [InlineData("sabnzbd", DownloadProtocol.Usenet)]
        [InlineData("qbittorrent", DownloadProtocol.Torrent)]
        public async Task RecordImportedAsync_RecordsTheProtocolTheClientSpeaks(
            string clientType,
            DownloadProtocol expected)
        {
            await GivenDownloadClient("client-1", clientType, expected);

            await _service.RecordImportedAsync("abc123", "client-1", "Test Book");

            Assert.Equal(expected, Assert.Single(_context.History).Protocol);
        }

        // A caller that knows the protocol is believed without a lookup, which is how the
        // submission path already passes it to RecordGrabbedAsync.
        [Fact]
        public async Task RecordDownloadFailedAsync_PrefersTheProtocolTheCallerPasses()
        {
            await GivenDownloadClient("client-1", "qbittorrent", DownloadProtocol.Torrent);

            await _service.RecordDownloadFailedAsync(
                "abc123", "client-1", "Test Book", "refused", DownloadProtocol.Usenet);

            Assert.Equal(DownloadProtocol.Usenet, Assert.Single(_context.History).Protocol);
            _adapterFactory.Verify(factory => factory.GetByType(It.IsAny<string>()), Times.Never);
        }

        // A deleted client, or a type with no adapter, must read as unknown. Falling back to the
        // enum's first member would be the original bug wearing a different hat.
        [Fact]
        public async Task RecordDownloadFailedAsync_UnresolvableClient_RecordsUnknownRatherThanTorrent()
        {
            await _service.RecordDownloadFailedAsync("abc123", "gone", "Test Book", "refused");

            Assert.Equal(DownloadProtocol.Unknown, Assert.Single(_context.History).Protocol);
        }

        // The value has to survive the write and the read, or fixing the construction changes
        // nothing observable. Before this it was dropped by the mapping and defaulted on the way
        // back out, so every row read as Torrent whatever had been recorded.
        [Fact]
        public async Task GetHistoryAsync_ReturnsTheProtocolThatWasRecorded()
        {
            await GivenDownloadClient("client-1", "sabnzbd", DownloadProtocol.Usenet);

            await _service.RecordGrabbedAsync("abc123", "client-1", "Test Book", DownloadProtocol.Usenet);
            await _service.RecordImportedAsync("abc123", "client-1", "Test Book");

            var history = await _service.GetHistoryAsync("abc123", "client-1");

            Assert.All(history, entry => Assert.Equal(DownloadProtocol.Usenet, entry.Protocol));
        }

        // The remaining four Record methods carried the same hardcoded Torrent. Nothing about them
        // is different, so leaving them would mean a history where some rows are right and some
        // are wrong with no way to tell which.
        [Theory]
        [InlineData("sabnzbd", DownloadProtocol.Usenet)]
        [InlineData("qbittorrent", DownloadProtocol.Torrent)]
        public async Task RecordImportFailedAsync_RecordsTheProtocolTheClientSpeaks(
            string clientType,
            DownloadProtocol expected)
        {
            await GivenDownloadClient("client-1", clientType, expected);

            await _service.RecordImportFailedAsync("abc123", "client-1", "Test Book", "denied");

            Assert.Equal(expected, Assert.Single(_context.History).Protocol);
        }

        [Theory]
        [InlineData("sabnzbd", DownloadProtocol.Usenet)]
        [InlineData("qbittorrent", DownloadProtocol.Torrent)]
        public async Task RecordPausedResumedRemoved_RecordTheProtocolTheClientSpeaks(
            string clientType,
            DownloadProtocol expected)
        {
            await GivenDownloadClient("client-1", clientType, expected);

            await _service.RecordPausedAsync("abc123", "client-1", "Test Book");
            await _service.RecordResumedAsync("abc123", "client-1", "Test Book");
            await _service.RecordRemovedAsync("abc123", "client-1", "Test Book");

            var rows = _context.History.ToList();
            Assert.Equal(3, rows.Count);
            Assert.All(rows, row => Assert.Equal(expected, row.Protocol));
        }

        // Rows written before the column existed have no protocol, and guessing one for them would
        // be worse than saying we do not know.
        [Fact]
        public async Task GetHistoryAsync_RowWithNoProtocol_ReadsAsUnknown()
        {
            _context.History.Add(new History
            {
                DownloadId = "ABC123",
                DownloadClientId = "client-1",
                EventType = HistoryEvents.Grabbed,
                Outcome = HistoryOutcome.Succeeded,
                Timestamp = DateTime.UtcNow,
                SourceTitle = "Test Book",
                CorrelationId = "ABC123",
                Protocol = null
            });
            await _context.SaveChangesAsync();

            var entry = Assert.Single(await _service.GetHistoryAsync("abc123", "client-1"));

            Assert.Equal(DownloadProtocol.Unknown, entry.Protocol);
        }

        // The audiobook key and the protocol were fixed separately and both land on the same two
        // lines of RecordImportedAsync, so they are easy to reconcile in a way that keeps one and
        // quietly drops the other. This asks for both off one call: the row reached the book
        // through the per-book query, and it carries the protocol its client actually speaks
        // rather than the enum's first member.
        [Fact]
        public async Task RecordImportedAsync_AttachesTheBookAndRecordsTheResolvedProtocol()
        {
            await GivenDownloadClient("client-1", "sabnzbd", DownloadProtocol.Usenet);

            await _service.RecordImportedAsync("abc123", "client-1", "Test Book", audiobookId: 42);

            var repository = new EfHistoryRepository(_context);
            var entry = Assert.Single(await repository.GetByAudiobookIdAsync(42));

            Assert.Equal(42, entry.AudiobookId);
            Assert.Equal(DownloadProtocol.Usenet, entry.Protocol);
        }

        // The same pairing on the grab path. The protocol comes from the caller here rather than
        // from a lookup, because the submission knows it, but it still has to survive the write
        // onto the row the book query finds.
        [Fact]
        public async Task RecordGrabbedAsync_AttachesTheBookAndKeepsTheCallersProtocol()
        {
            await GivenDownloadClient("client-1", "sabnzbd", DownloadProtocol.Usenet);

            await _service.RecordGrabbedAsync(
                "abc123",
                "client-1",
                "Test Book",
                DownloadProtocol.Usenet,
                audiobookId: 42);

            var repository = new EfHistoryRepository(_context);
            var entry = Assert.Single(await repository.GetByAudiobookIdAsync(42));

            Assert.Equal(42, entry.AudiobookId);
            Assert.Equal(DownloadProtocol.Usenet, entry.Protocol);
        }
    }
}
