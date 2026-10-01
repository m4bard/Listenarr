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
using System.Globalization;
using System.Text.Json;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks.Api;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Monitoring
{
    /// <summary>
    /// Stalled-download handling, driven through whole monitor cycles against the real qBittorrent
    /// adapter and its fake Web API, with the clock moved by hand. What reaches the client's delete
    /// endpoint is therefore what a real qBittorrent would have been asked to remove.
    /// </summary>
    [Trait("Name", "StalledDownloadDetectionTests")]
    [Trait("Category", "DownloadMonitorService")]
    public class StalledDownloadDetectionTests : BaseTests
    {
        private const string Hash = "0123456789abcdef0123456789abcdef01234567";
        private const int TimeoutHours = 2;
        private static readonly TimeSpan PollEvery = TimeSpan.FromMinutes(10);

        private readonly AdjustableTimeProvider _clock = new();
        private DownloadMonitorService _monitor = null!;
        private QbittorrentApiMock _qbittorrent = null!;
        private DownloadClientConfiguration _client = null!;

        public StalledDownloadDetectionTests()
        {
            Init(builder => builder.WithSingleton<TimeProvider>(_clock));
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            _monitor = _provider.GetRequiredService<DownloadMonitorService>();
            _qbittorrent = _provider.GetRequiredService<QbittorrentApiMock>();
            _client = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithType("qbittorrent")
                .WithName("qBittorrent")
                .WithHost("localhost")
                .WithPort(8080)
                .WithUsername("admin")
                .WithPassword("admin")
                .Build());
        }

        [Fact]
        [Trait("Scenario", "A torrent with no progress for the timeout is failed through the existing failure path")]
        public async Task StalledTorrent_IsFailedBlocklistedRemovedFromTheClient_AndFreesTheBook()
        {
            await SaveSettingsAsync(handlingEnabled: true, hours: TimeoutHours);
            var download = await AddTorrentDownloadAsync();
            ReportTorrent("stalledDL", progress: 0.25, size: 1000, downloaded: 250);

            await PollAsync();
            await PollEveryUntilAsync(TimeSpan.FromHours(TimeoutHours));

            var failed = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(failed);
            Assert.Equal(DownloadStatus.Failed, failed!.Status);
            Assert.Contains("No download progress", failed.ErrorMessage);

            // Blocklisted, so the search that follows a failure cannot grab it straight back.
            var blocklist = _provider.GetRequiredService<IBlocklistService>();
            var entry = Assert.Single(await blocklist.GetForAudiobookAsync(download.AudiobookId!.Value));
            Assert.Equal("btih:" + Hash, entry.ReleaseIdentifier);

            // Removed from the client by its own hash, keeping the data, which is what
            // OnDownloadFailed does for any other failed torrent.
            Assert.NotNull(_qbittorrent.LastDeleteForm);
            Assert.Equal(Hash, _qbittorrent.LastDeleteForm!["hashes"]);
            Assert.Equal("false", _qbittorrent.LastDeleteForm["deleteFiles"]);

            // And the book's one active-download slot is free again.
            Assert.Null(failed.ActiveAudiobookDeduplicationKey);
            Assert.DoesNotContain(await _downloadRepository.GetActiveAsync(), d => d.Id == download.Id);
            Assert.False(await DownloadDuplicateGuard.HasActiveDownloadAsync(
                download.AudiobookId!.Value,
                _provider.GetRequiredService<IConfigurationService>(),
                _downloadRepository));

            // Nothing un-fails it afterwards: a Failed row is not polled again, so the slot stays
            // free even while the client (here, still) reports the torrent.
            _clock.Advance(PollEvery);
            await PollAsync();
            Assert.Equal(DownloadStatus.Failed, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
        }

        [Fact]
        [Trait("Scenario", "Just under the timeout is not a stall, the timeout itself is")]
        public async Task NoProgress_JustUnderTheTimeoutIsLeftAlone_AndAtTheTimeoutIsFailed()
        {
            await SaveSettingsAsync(handlingEnabled: true, hours: TimeoutHours);
            var download = await AddTorrentDownloadAsync();
            ReportTorrent("metaDL", progress: 0, size: 0, downloaded: 0);

            await PollAsync();
            await PollEveryUntilAsync(TimeSpan.FromHours(TimeoutHours) - TimeSpan.FromSeconds(1));
            Assert.Equal(DownloadStatus.Downloading, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
            Assert.Null(_qbittorrent.LastDeleteForm);

            _clock.Advance(TimeSpan.FromSeconds(1));
            await PollAsync();
            Assert.Equal(DownloadStatus.Failed, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
            Assert.Equal(Hash, _qbittorrent.LastDeleteForm!["hashes"]);
        }

        [Theory]
        [Trait("Scenario", "A slow download that keeps moving is never a stall")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SlowButProgressing_IsNeverFailed_HoweverLongItRuns(bool bytesMove)
        {
            // Twenty hours against a two hour timeout, moving by the smallest step each poll: one
            // byte, or (for a torrent whose size is not known yet, so bytes are never trusted) a
            // sliver of progress.
            await SaveSettingsAsync(handlingEnabled: true, hours: TimeoutHours);
            var download = await AddTorrentDownloadAsync();
            var size = bytesMove ? 1_000_000L : 0L;

            for (var poll = 0; poll <= 120; poll++)
            {
                ReportTorrent(
                    "downloading",
                    progress: bytesMove ? 0.1 : 0.1 + poll * 0.0001,
                    size: size,
                    downloaded: bytesMove ? 100_000 + poll : 0);
                await PollAsync();
                _clock.Advance(PollEvery);
            }

            var row = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.Equal(DownloadStatus.Downloading, row!.Status);
            Assert.Null(_qbittorrent.LastDeleteForm);
        }

        [Theory]
        [Trait("Scenario", "Stall handling is opt-in and hangs off failed-download handling")]
        [InlineData(true, 0)]
        [InlineData(false, TimeoutHours)]
        public async Task NoProgress_IsNeverFailed_WhenTheTimeoutIsOffOrFailedHandlingIsOff(bool handlingEnabled, int hours)
        {
            await SaveSettingsAsync(handlingEnabled, hours);
            var download = await AddTorrentDownloadAsync();
            ReportTorrent("stalledDL", progress: 0.25, size: 1000, downloaded: 250);

            await PollAsync();
            await PollEveryUntilAsync(TimeSpan.FromHours(TimeoutHours * 3));

            var row = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.Equal(DownloadStatus.Downloading, row!.Status);
            // Polled throughout, so the absence of a failure is not the absence of a poll.
            Assert.Equal(250, row.DownloadedSize);
            Assert.Null(_qbittorrent.LastDeleteForm);
            Assert.Null(row.GetMetadataString(DownloadMonitorProcessor.StallProgressChangedAtKey));
        }

        [Fact]
        [Trait("Scenario", "Usenet items that stop moving are an outage, not a dead release")]
        public async Task UsenetDownload_WithNoProgress_IsNeverFailed()
        {
            await SaveSettingsAsync(handlingEnabled: true, hours: TimeoutHours);
            var sabnzbd = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithType("sabnzbd")
                .WithName("SABnzbd")
                .WithHost("localhost")
                .WithPort(8080)
                .WithApiKey("apiKey")
                .Build());
            var audiobook = await CreateAudiobook();
            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithAudiobook(audiobook)
                .WithExternalId("SABnzbd_nzo_20f9svw_")
                .WithDownloadClientConfiguration(sabnzbd)
                .Build());

            // The fake SABnzbd reports this item at a fixed 50.5% on every poll.
            for (var elapsed = TimeSpan.Zero; elapsed <= TimeSpan.FromHours(TimeoutHours * 3); elapsed += PollEvery)
            {
                _monitor.ScheduleNextClientPoll(sabnzbd, -100);
                await _monitor.MonitorDownloadsAsync(CancellationToken.None);
                _clock.Advance(PollEvery);
            }

            var row = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.Equal(DownloadStatus.Downloading, row!.Status);
            Assert.Equal(50.5m, row.Progress);
        }

        [Theory]
        [Trait("Scenario", "A pause shorter than the observation gap cap still clears the clock")]
        [InlineData("stoppedDL")]
        [InlineData("pausedDL")]
        [InlineData("queuedDL")]
        public async Task ABriefPauseAfterALongStall_ClearsTheClock_SoResumeIsNotFailedAtOnce(string waitingState)
        {
            // The long-pause test above cannot tell clearing from the gap cap: a six hour pause
            // restarts the clock either way. Here every gap stays under the cap, so only clearing
            // the markers while the torrent is not eligible keeps the old clock from firing.
            await SaveSettingsAsync(handlingEnabled: true, hours: TimeoutHours);
            var download = await AddTorrentDownloadAsync();

            // 100 minutes of stall against a 120 minute timeout.
            ReportTorrent("stalledDL", progress: 0.25, size: 1000, downloaded: 250);
            await PollAsync();
            await PollEveryUntilAsync(TimeSpan.FromMinutes(100));

            // 20 minutes waiting, polled every 10.
            ReportTorrent(waitingState, progress: 0.25, size: 1000, downloaded: 250);
            await PollEveryUntilAsync(TimeSpan.FromMinutes(20));

            // Resumed with no bytes, 25 minutes after the last stalled poll (under the 30 minute
            // cap) and 125 minutes after the stall began (past the timeout on the old clock).
            ReportTorrent("stalledDL", progress: 0.25, size: 1000, downloaded: 250);
            _clock.Advance(TimeSpan.FromMinutes(5));
            await PollAsync();

            Assert.Equal(DownloadStatus.Downloading, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
            Assert.Null(_qbittorrent.LastDeleteForm);
        }

        [Theory]
        [Trait("Scenario", "Time spent paused or queued does not count")]
        [InlineData("stoppedDL")]
        [InlineData("pausedDL")]
        [InlineData("queuedDL")]
        public async Task PausedOrQueuedPastTheTimeout_ThenResumedWithNoBytes_StartsTheClockAtResume(string waitingState)
        {
            await SaveSettingsAsync(handlingEnabled: true, hours: TimeoutHours);
            var download = await AddTorrentDownloadAsync();

            // Most of a timeout's worth of stall before the pause, which must not carry over.
            ReportTorrent("stalledDL", progress: 0.25, size: 1000, downloaded: 250);
            await PollAsync();
            await PollEveryUntilAsync(TimeSpan.FromMinutes(90));

            ReportTorrent(waitingState, progress: 0.25, size: 1000, downloaded: 250);
            _clock.Advance(PollEvery);
            await PollAsync();
            await PollEveryUntilAsync(TimeSpan.FromHours(TimeoutHours * 3));
            Assert.NotEqual(DownloadStatus.Failed, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);

            // Resumed, and still not moving. The first poll after resume must not fail it.
            ReportTorrent("stalledDL", progress: 0.25, size: 1000, downloaded: 250);
            _clock.Advance(PollEvery);
            await PollAsync();
            Assert.Equal(DownloadStatus.Downloading, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);

            // The clock started at resume: just under the timeout from there is still fine...
            await PollEveryUntilAsync(TimeSpan.FromHours(TimeoutHours) - TimeSpan.FromSeconds(1));
            Assert.Equal(DownloadStatus.Downloading, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);

            // ...and the timeout from resume fails it, so the test above is not passing because
            // nothing ever fails.
            _clock.Advance(TimeSpan.FromSeconds(1));
            await PollAsync();
            Assert.Equal(DownloadStatus.Failed, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
        }

        [Fact]
        [Trait("Scenario", "Time nobody was watching does not count")]
        public async Task AGapInObservationLongerThanTheCap_RestartsTheClock()
        {
            await SaveSettingsAsync(handlingEnabled: true, hours: TimeoutHours);
            var download = await AddTorrentDownloadAsync();
            ReportTorrent("stalledDL", progress: 0.25, size: 1000, downloaded: 250);

            await PollAsync();
            _clock.Advance(PollEvery);
            await PollAsync();

            // Listenarr, or the client, was away for three hours. The first poll back must not
            // count those hours as observed stall.
            _clock.Advance(TimeSpan.FromHours(3));
            await PollAsync();
            Assert.Equal(DownloadStatus.Downloading, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);

            await PollEveryUntilAsync(TimeSpan.FromHours(TimeoutHours) - TimeSpan.FromSeconds(1));
            Assert.Equal(DownloadStatus.Downloading, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);

            _clock.Advance(TimeSpan.FromSeconds(1));
            await PollAsync();
            Assert.Equal(DownloadStatus.Failed, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
        }

        [Fact]
        [Trait("Scenario", "A torrent the client stops reporting is not observed, so it is not a stall")]
        public async Task ATorrentMissingFromTheClientsAnswer_IsNotCountedAsStalled()
        {
            // Orphan cleanup owns a row whose torrent has gone. Stall handling must not decide on
            // its behalf from bytes that stopped changing only because nobody reported them.
            await SaveSettingsAsync(handlingEnabled: true, hours: TimeoutHours);
            var download = await AddTorrentDownloadAsync();
            _qbittorrent.InfoResponseOverride = "[]";

            await PollAsync();
            await PollEveryUntilAsync(TimeSpan.FromHours(TimeoutHours * 3));

            var row = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.Equal(DownloadStatus.Downloading, row!.Status);
            Assert.Null(_qbittorrent.LastDeleteForm);
        }

        [Fact]
        [Trait("Scenario", "A seeding torrent is complete, not stalled")]
        public async Task SeedingTorrent_AtFullProgress_IsNeverFailed()
        {
            await SaveSettingsAsync(handlingEnabled: true, hours: TimeoutHours);
            var download = await AddTorrentDownloadAsync();
            ReportTorrent("stalledUP", progress: 1.0, size: 1000, downloaded: 1000);

            await PollAsync();
            await PollEveryUntilAsync(TimeSpan.FromHours(TimeoutHours * 3));

            var row = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotEqual(DownloadStatus.Failed, row!.Status);
            Assert.Null(_qbittorrent.LastDeleteForm);
        }

        [Theory]
        [Trait("Scenario", "Only an incomplete download the client is actually downloading is eligible")]
        [InlineData(DownloadStatus.Downloading, 25, 1000, 250, "downloading", true)]
        [InlineData(DownloadStatus.Downloading, 0, 0, 0, "downloading", true)]
        [InlineData(DownloadStatus.Downloading, 25, 1000, 250, null, true)]
        // Seeding states map to Downloading too; complete by progress or by bytes, never eligible.
        [InlineData(DownloadStatus.Downloading, 100, 1000, 1000, "seeding", false)]
        [InlineData(DownloadStatus.Downloading, 100, 0, 0, "stalledUP", false)]
        [InlineData(DownloadStatus.Downloading, 99, 1000, 1000, "uploading", false)]
        // The client is busy with the data rather than downloading it.
        [InlineData(DownloadStatus.Downloading, 25, 1000, 250, "checkingDL", false)]
        [InlineData(DownloadStatus.Downloading, 25, 1000, 250, "checkingUP", false)]
        [InlineData(DownloadStatus.Downloading, 25, 1000, 250, "checkingResumeData", false)]
        [InlineData(DownloadStatus.Downloading, 25, 1000, 250, "moving", false)]
        [InlineData(DownloadStatus.Paused, 25, 1000, 250, "paused", false)]
        [InlineData(DownloadStatus.Queued, 25, 1000, 250, "queued", false)]
        [InlineData(DownloadStatus.Completed, 100, 1000, 1000, "completed", false)]
        public void Eligibility(DownloadStatus status, int progress, long size, long downloaded, string? clientState, bool expected)
        {
            var download = new DownloadBuilder().WithStatus(status).WithProgress(progress).Build();
            download.TotalSize = size;
            download.DownloadedSize = downloaded;
            if (clientState is not null)
            {
                download.Metadata["ClientState"] = clientState;
            }

            Assert.Equal(expected, DownloadMonitorProcessor.IsStallEligible(download));
        }

        [Theory]
        [Trait("Scenario", "Only torrent clients are in scope")]
        [InlineData("qbittorrent", true)]
        [InlineData("QBittorrent", true)]
        [InlineData("transmission", true)]
        [InlineData("sabnzbd", false)]
        [InlineData("nzbget", false)]
        [InlineData("mock", false)]
        public void ClientScope(string type, bool expected)
        {
            Assert.Equal(expected, DownloadMonitorProcessor.IsStallCheckedClient(new DownloadClientConfiguration { Type = type }));
        }

        [Fact]
        [Trait("Scenario", "The clock is persisted with the row and survives a restart")]
        public async Task TheStallClock_SurvivesAPersistenceRoundTrip_AndAProcessorRestart()
        {
            await SaveSettingsAsync(handlingEnabled: true, hours: TimeoutHours);
            var download = await AddTorrentDownloadAsync();
            ReportTorrent("stalledDL", progress: 0.25, size: 1000, downloaded: 250);
            var started = _clock.GetUtcNow();

            await PollAsync();

            // Read back from the store, where metadata comes back as JsonElement rather than the
            // strings that were written.
            var stored = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.IsType<JsonElement>(stored!.Metadata[DownloadMonitorProcessor.StallProgressChangedAtKey]);
            Assert.Equal(
                started,
                DateTimeOffset.Parse(
                    stored.GetMetadataString(DownloadMonitorProcessor.StallProgressChangedAtKey)!,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind));
            Assert.Equal("250", stored.GetMetadataString(DownloadMonitorProcessor.StallLastDownloadedSizeKey));

            await PollEveryUntilAsync(TimeSpan.FromHours(1));

            // A fresh processor, as after a restart: nothing in memory, the same row.
            var restarted = new DownloadMonitorProcessor(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                _provider.GetRequiredService<IDownloadPushService>(),
                _clock,
                NullLogger<DownloadMonitorProcessor>.Instance);

            // Just under the timeout measured from the first poll, not from the restart...
            while (_clock.GetUtcNow() - started < TimeSpan.FromHours(TimeoutHours) - PollEvery)
            {
                _clock.Advance(PollEvery);
                await PollAsync(restarted);
            }

            _clock.Advance(PollEvery - TimeSpan.FromSeconds(1));
            await PollAsync(restarted);
            Assert.Equal(DownloadStatus.Downloading, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);

            // ...and the timeout from the first poll fails it.
            _clock.Advance(TimeSpan.FromSeconds(1));
            await PollAsync(restarted);
            Assert.Equal(DownloadStatus.Failed, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
        }

        private async Task SaveSettingsAsync(bool handlingEnabled, int hours)
        {
            var builder = new ApplicationSettingsBuilder()
                .WithoutCompletionStabilityWindow()
                .WithStalledDownloadTimeoutHours(hours);
            builder = handlingEnabled ? builder.WithFailedDownloadHandling() : builder.WithoutFailedDownloadHandling();
            await _applicationSettingsRepository.SaveAsync(builder.Build());
        }

        private async Task<Download> AddTorrentDownloadAsync()
        {
            var audiobook = await CreateAudiobook();
            var download = new DownloadBuilder()
                .WithDownloading(0)
                .WithTitle("A Stalled Listing")
                .WithAudiobook(audiobook)
                .WithExternalId(Hash)
                .WithDownloadClientConfiguration(_client)
                .Build();
            download.Metadata[ReleaseIdentity.MetadataKey] = ReleaseIdentity.For(Hash, null, null, null)!;
            return await _downloadRepository.AddAsync(download);
        }

        private void ReportTorrent(string state, double progress, long size, long downloaded)
        {
            _qbittorrent.InfoResponseOverride = $$"""
            [
                {
                    "hash": "{{Hash}}",
                    "name": "A Stalled Listing",
                    "progress": {{progress.ToString(CultureInfo.InvariantCulture)}},
                    "size": {{size}},
                    "downloaded": {{downloaded}},
                    "state": "{{state}}",
                    "save_path": "/downloads/listing"
                }
            ]
            """;
        }

        private async Task PollAsync()
        {
            _monitor.ScheduleNextClientPoll(_client, -100);
            await _monitor.MonitorDownloadsAsync(CancellationToken.None);
        }

        private async Task PollAsync(DownloadMonitorProcessor processor)
        {
            // The per-client schedule runs on the wall clock, so clear it before every cycle or
            // the second cycle in a row is skipped.
            processor.ScheduleNextClientPoll(_client, -100);
            await processor.RunCycleAsync(CancellationToken.None);
        }

        /// <summary>
        /// Poll every <see cref="PollEvery"/> until <paramref name="span"/> has passed since the
        /// last poll, with the final step shortened so the last poll lands exactly on it.
        /// </summary>
        private async Task PollEveryUntilAsync(TimeSpan span)
        {
            var target = _clock.GetUtcNow() + span;
            while (_clock.GetUtcNow() < target)
            {
                var step = target - _clock.GetUtcNow();
                _clock.Advance(step < PollEvery ? step : PollEvery);
                await PollAsync();
            }
        }
    }
}
