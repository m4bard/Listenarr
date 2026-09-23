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

using System.Net;
using System.Net.Sockets;
using Listenarr.Domain.Downloads.Exceptions;
using Listenarr.Infrastructure.Downloads.Status;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Status
{
    /// <summary>
    /// Where a download client call succeeds or fails, and which failures count against the
    /// client. The distinction the whole feature rests on: a client that could not be reached is
    /// escalated, a client that answered and refused one release is not. Readarr draws the same
    /// line by exception type (DownloadService.cs catches DownloadClientRejectedReleaseException
    /// and records nothing against the client).
    /// </summary>
    [Trait("Area", "DownloadClientStatus")]
    [Trait("Name", "StatusRecordingDownloadClientGatewayTests")]
    [Trait("Category", "Infrastructure")]
    public class StatusRecordingDownloadClientGatewayTests : BaseTests
    {
        private static readonly DownloadClientConfiguration Qbit = new()
        {
            Id = "qb-main",
            Name = "qb-main",
            Type = "qbittorrent",
            IsEnabled = true
        };

        private static readonly DownloadClientSubmissionResult Accepted = new("HASH", "HASH");

        private static readonly PreparedTorrentSubmission Torrent =
            PreparedSubmissionTestFactory.Torrent("Book", "ABCDEF1234567890ABCDEF1234567890ABCDEF12");

        private readonly Mock<IDownloadClientAdapter> _adapter = new();
        private readonly Mock<IDownloadClientStatusService> _status = new();

        /// <summary>
        /// The real gateway with a mocked adapter behind it, so what is asserted is the exception
        /// the gateway actually lets through, not one handed straight to the recorder.
        /// </summary>
        private StatusRecordingDownloadClientGateway CreateGateway()
        {
            _adapter.Setup(a => a.Protocols).Returns([DownloadProtocol.Torrent]);
            var factory = new Mock<IDownloadClientAdapterFactory>();
            factory.Setup(f => f.GetByType(Qbit.Type)).Returns(_adapter.Object);

            return new StatusRecordingDownloadClientGateway(
                new Mock<IRemotePathMappingService>().Object,
                factory.Object,
                new Mock<IFileSystem>().Object,
                new Mock<IFileSystemSemanticsResolver>().Object,
                NullLogger<DownloadClientGateway>.Instance,
                _status.Object,
                NullLogger<StatusRecordingDownloadClientGateway>.Instance);
        }

        private void AddThrows(Exception ex) =>
            _adapter
                .Setup(a => a.AddAsync(Qbit, It.IsAny<PreparedDownloadSubmission>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(ex);

        private void VerifyFailureRecorded(Times times) =>
            _status.Verify(s => s.RecordFailureAsync(Qbit.Id, It.IsAny<CancellationToken>()), times);

        private void VerifySuccessRecorded(Times times) =>
            _status.Verify(s => s.RecordSuccessAsync(Qbit.Id, It.IsAny<CancellationToken>()), times);

        private static HttpRequestException ConnectionRefused() =>
            new("Connection refused (localhost:8080)", new SocketException((int)SocketError.ConnectionRefused));

        [Fact]
        [Trait("Scenario", "Unreachable")]
        public async Task AddAsync_ClientUnreachable_EscalatesTheClientAndRethrows()
        {
            // A transport failure wrapped once, as the adapters wrap them.
            var thrown = new DownloadClientSubmissionException("Failed to send the torrent to the download client.", ConnectionRefused());
            AddThrows(thrown);

            var caught = await Assert.ThrowsAsync<DownloadClientSubmissionException>(
                () => CreateGateway().AddAsync(Qbit, Torrent));

            Assert.Same(thrown, caught);
            VerifyFailureRecorded(Times.Once());
            VerifySuccessRecorded(Times.Never());
        }

        [Fact]
        [Trait("Scenario", "Unreachable")]
        public async Task AddAsync_BreakerOpen_EscalatesTheClient()
        {
            // The Polly breaker refusing the call is as unreachable as the client gets.
            AddThrows(new BrokenCircuitException("The circuit is now open"));

            await Assert.ThrowsAsync<BrokenCircuitException>(() => CreateGateway().AddAsync(Qbit, Torrent));

            VerifyFailureRecorded(Times.Once());
        }

        [Fact]
        [Trait("Scenario", "Unreachable")]
        public async Task AddAsync_HttpClientTimeout_EscalatesTheClient()
        {
            // HttpClient reports its own timeout as a cancellation the caller did not ask for.
            AddThrows(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout", new TimeoutException()));

            await Assert.ThrowsAsync<TaskCanceledException>(() => CreateGateway().AddAsync(Qbit, Torrent));

            VerifyFailureRecorded(Times.Once());
        }

        [Fact]
        [Trait("Scenario", "ReleaseRejected")]
        public async Task AddAsync_ClientRefusedThisRelease_DoesNotEscalate()
        {
            // qBittorrent 5.2 answers 409 for an info-hash it already holds, which is a statement
            // about the release, not about the client. The adapter's exception carries no
            // transport failure because there was none: the client answered.
            AddThrows(new DownloadClientSubmissionException("qBittorrent rejected the torrent with HTTP 409."));

            await Assert.ThrowsAsync<DownloadClientSubmissionException>(() => CreateGateway().AddAsync(Qbit, Torrent));

            VerifyFailureRecorded(Times.Never());
            VerifySuccessRecorded(Times.Never());
        }

        [Fact]
        [Trait("Scenario", "ReleaseRejected")]
        public async Task AddAsync_HttpStatusFromTheClient_OnlyServerAndAuthFailuresEscalate()
        {
            // A status code means the client answered. 409 is about the release; 503 and 401 are
            // about the client, and every later grab would hit them too.
            AddThrows(new HttpRequestException("Conflict", null, HttpStatusCode.Conflict));
            await Assert.ThrowsAsync<HttpRequestException>(() => CreateGateway().AddAsync(Qbit, Torrent));
            VerifyFailureRecorded(Times.Never());

            AddThrows(new HttpRequestException("Service Unavailable", null, HttpStatusCode.ServiceUnavailable));
            await Assert.ThrowsAsync<HttpRequestException>(() => CreateGateway().AddAsync(Qbit, Torrent));
            VerifyFailureRecorded(Times.Once());

            AddThrows(new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));
            await Assert.ThrowsAsync<HttpRequestException>(() => CreateGateway().AddAsync(Qbit, Torrent));
            VerifyFailureRecorded(Times.Exactly(2));
        }

        [Fact]
        [Trait("Scenario", "CallerCancelled")]
        public async Task AddAsync_CancelledByTheCaller_RecordsNothing()
        {
            // Shutdown or a caller's own deadline says nothing about the client.
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            _adapter
                .Setup(a => a.AddAsync(Qbit, It.IsAny<PreparedDownloadSubmission>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException(cts.Token));

            await Assert.ThrowsAsync<OperationCanceledException>(() => CreateGateway().AddAsync(Qbit, Torrent, cts.Token));

            VerifyFailureRecorded(Times.Never());
            VerifySuccessRecorded(Times.Never());
        }

        [Fact]
        [Trait("Scenario", "Success")]
        public async Task AddAsync_Accepted_RecordsASuccess()
        {
            _adapter
                .Setup(a => a.AddAsync(Qbit, It.IsAny<PreparedDownloadSubmission>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Accepted);

            var result = await CreateGateway().AddAsync(Qbit, Torrent);

            Assert.Same(Accepted, result);
            VerifySuccessRecorded(Times.Once());
            VerifyFailureRecorded(Times.Never());
        }

        [Fact]
        [Trait("Scenario", "RecordingFails")]
        public async Task AddAsync_WhenRecordingTheStatusFails_TheGrabStillSucceeds()
        {
            // The client took the release. A status write that fails must not turn that into a
            // failed grab, or the release is sent again and duplicated.
            _adapter
                .Setup(a => a.AddAsync(Qbit, It.IsAny<PreparedDownloadSubmission>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Accepted);
            _status
                .Setup(s => s.RecordSuccessAsync(Qbit.Id, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("database is locked"));

            var result = await CreateGateway().AddAsync(Qbit, Torrent);

            Assert.Same(Accepted, result);
        }

        [Fact]
        [Trait("Scenario", "Polling")]
        public async Task GetQueueAsync_AFailureEscalates_ButAnAnswerIsNotTakenAsHealth()
        {
            // Polling is not about any one release, so a failure there counts against the client,
            // as in Readarr's DownloadMonitoringService (RecordFailure on any exception).
            _adapter
                .Setup(a => a.GetQueueAsync(Qbit, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Download client reported a save path that is not valid on this host."));

            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateGateway().GetQueueAsync(Qbit));
            VerifyFailureRecorded(Times.Once());

            // The display snapshot is not evidence of health: the qBittorrent adapter answers an
            // unreachable client there with an empty list rather than an exception, so recording a
            // success would walk a dead client down on every page load.
            _adapter
                .Setup(a => a.GetQueueAsync(Qbit, It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            await CreateGateway().GetQueueAsync(Qbit);
            VerifySuccessRecorded(Times.Never());
        }

        [Fact]
        [Trait("Scenario", "Polling")]
        public async Task FetchDownloadsAsync_PollingFailure_Escalates()
        {
            _adapter
                .Setup(a => a.GetQueueAsync(Qbit, It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(ConnectionRefused());

            await Assert.ThrowsAsync<DownloadClientAdapterPollingException>(
                () => CreateGateway().FetchDownloadsAsync(Qbit, [Tracked("HASH")]));

            VerifyFailureRecorded(Times.Once());
        }

        [Fact]
        [Trait("Scenario", "Polling")]
        public async Task FetchDownloadsAsync_RecordsASuccessOnlyWhenTheClientWasActuallyAsked()
        {
            _adapter
                .Setup(a => a.GetQueueAsync(Qbit, It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            // No download carries a client id, so the gateway returns without calling the client.
            await CreateGateway().FetchDownloadsAsync(Qbit, [new Download()]);
            VerifySuccessRecorded(Times.Never());
            _adapter.Verify(a => a.GetQueueAsync(Qbit, It.IsAny<List<string>>(), It.IsAny<CancellationToken>()), Times.Never());

            // The monitor poll proper: the adapter throws when it cannot ask, so an answer is health.
            await CreateGateway().FetchDownloadsAsync(Qbit, [Tracked("HASH")]);
            VerifySuccessRecorded(Times.Once());
        }

        private static Download Tracked(string externalId)
        {
            var download = new Download();
            download.Metadata[Download.METADATA_EXTERNAL_ID_KEY] = externalId;
            return download;
        }

        [Fact]
        [Trait("Scenario", "ConnectionTest")]
        public async Task TestConnectionAsync_RecordsTheOutcome()
        {
            // Readarr's DownloadClientFactory.Test records both outcomes for a saved client, so a
            // user who fixes a client and presses Test walks it back down straight away.
            _adapter
                .Setup(a => a.TestConnectionAsync(Qbit, It.IsAny<CancellationToken>()))
                .ReturnsAsync((false, "Connection refused"));
            await CreateGateway().TestConnectionAsync(Qbit);
            VerifyFailureRecorded(Times.Once());

            _adapter
                .Setup(a => a.TestConnectionAsync(Qbit, It.IsAny<CancellationToken>()))
                .ReturnsAsync((true, "Connected"));
            await CreateGateway().TestConnectionAsync(Qbit);
            VerifySuccessRecorded(Times.Once());
        }

        [Fact]
        [Trait("Scenario", "PassThrough")]
        public async Task RemoveAsync_IsPassedThroughWithoutRecording()
        {
            _adapter
                .Setup(a => a.RemoveAsync(Qbit, "HASH", true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            Assert.True(await CreateGateway().RemoveAsync(Qbit, "HASH", true));

            VerifyFailureRecorded(Times.Never());
            VerifySuccessRecorded(Times.Never());
        }
    }
}
