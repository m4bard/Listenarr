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
 */
using System.Globalization;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks.Api;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Monitoring
{
    /// <summary>
    /// The stalled-download floor: a second, independent trigger alongside the pre-existing
    /// 8-signal check (<see cref="StalledDownloadDetectionTests"/>). That check resets its clock
    /// on any observed byte or progress movement, including a download that trickles a few
    /// hundred bytes a poll without ever moving a whole piece -- the measured shape behind
    /// tracker item 81's follow-up. This floor catches that case by judging cumulative movement
    /// over a whole stall window against a share of TotalSize, instead of judging each poll on
    /// its own.
    /// </summary>
    [Trait("Name", "StalledDownloadFloorTests")]
    [Trait("Category", "DownloadMonitorService")]
    public class StalledDownloadFloorTests : BaseTests
    {
        private const string Hash = "fedcba9876543210fedcba9876543210fedcba9";
        private const int TimeoutHours = 2;
        private const long TotalSize = 1_000_000L;
        private static readonly TimeSpan PollEvery = TimeSpan.FromMinutes(10);

        private readonly AdjustableTimeProvider _clock = new();
        private DownloadMonitorService _monitor = null!;
        private QbittorrentApiMock _qbittorrent = null!;
        private DownloadClientConfiguration _client = null!;

        public StalledDownloadFloorTests()
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
        [Trait("Scenario", "A byte-only trickle clears every 8-signal condition forever, so the floor must catch it")]
        public async Task TrickleBelowTheFloor_IsFailed_AfterOneStallWindow()
        {
            // Mirrors the measured shape behind this item's follow-up (AudiobookId 643): a torrent
            // whose downloaded bytes move a few hundred bytes every poll, but never a whole piece,
            // so Progress never changes. 300 bytes x 12 polls/window = 3,600 of 1,000,000 bytes,
            // 0.36% -- below the 1% default floor.
            await SaveSettingsAsync(hours: TimeoutHours, floorPercent: 1m);
            var download = await AddTorrentDownloadAsync();

            var downloaded = 0L;
            for (var poll = 0; poll < 13; poll++)
            {
                downloaded += 300;
                ReportTorrent(progress: 0.1, downloaded: downloaded);
                await PollAsync();

                if (poll < 12)
                {
                    Assert.Equal(
                        DownloadStatus.Downloading,
                        (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
                }

                _clock.Advance(PollEvery);
            }

            var row = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.Equal(DownloadStatus.Failed, row!.Status);
            Assert.Contains("moved", row.ErrorMessage);
            Assert.NotNull(_qbittorrent.LastDeleteForm);
        }

        [Fact]
        [Trait("Scenario", "A download clearing the floor every window is never failed")]
        public async Task SteadyAboveTheFloor_IsNeverFailed()
        {
            // 2,000 bytes x 12 polls/window = 24,000 of 1,000,000 bytes, 2.4% a window -- clears
            // the 1% default floor with headroom, for twenty hours (ten windows).
            await SaveSettingsAsync(hours: TimeoutHours, floorPercent: 1m);
            var download = await AddTorrentDownloadAsync();

            var downloaded = 0L;
            for (var poll = 0; poll < 120; poll++)
            {
                downloaded += 2_000;
                ReportTorrent(progress: 0.1, downloaded: downloaded);
                await PollAsync();
                _clock.Advance(PollEvery);
            }

            var row = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.Equal(DownloadStatus.Downloading, row!.Status);
            Assert.Null(_qbittorrent.LastDeleteForm);
        }

        [Fact]
        [Trait("Scenario", "A genuine standstill still fires through the pre-existing 8-signal check, unchanged")]
        public async Task GenuineStandstill_StillFiresThroughTheExisting8SignalCheck()
        {
            // Zero byte movement, zero progress movement, the floor enabled at its default. If
            // the floor had quietly become the only path to a standstill failure, or had
            // weakened the 8-signal check, this would either not fail or fail with the floor's
            // own message instead of the 8-signal check's.
            await SaveSettingsAsync(hours: TimeoutHours, floorPercent: 1m);
            var download = await AddTorrentDownloadAsync();
            ReportTorrent(progress: 0.25, downloaded: 250);

            await PollAsync();
            await PollEveryUntilAsync(TimeSpan.FromHours(TimeoutHours));

            var row = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.Equal(DownloadStatus.Failed, row!.Status);
            Assert.Contains("No download progress", row.ErrorMessage);
            Assert.DoesNotContain("moved", row.ErrorMessage);
        }

        [Theory]
        [Trait("Scenario", "Floor at zero is fully off: identical to the pre-floor code for both shapes")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task FloorAtZero_IsFullyDisabled_ForBothTrickleAndStandstill(bool trickle)
        {
            await SaveSettingsAsync(hours: TimeoutHours, floorPercent: 0m);
            var download = await AddTorrentDownloadAsync();

            var downloaded = 0L;
            for (var poll = 0; poll < 120; poll++)
            {
                if (trickle)
                {
                    downloaded += 300;
                }

                ReportTorrent(progress: trickle ? 0.1 : 0.25, downloaded: trickle ? downloaded : 250);
                await PollAsync();
                _clock.Advance(PollEvery);
            }

            var row = await _downloadRepository.GetByIdAsync(download.Id);
            if (trickle)
            {
                // The pre-floor gap: a byte-only trickle is never failed by the 8-signal check
                // alone, however long it runs.
                Assert.Equal(DownloadStatus.Downloading, row!.Status);
                Assert.Null(_qbittorrent.LastDeleteForm);
            }
            else
            {
                // A genuine standstill is unaffected either way: the 8-signal check alone fails
                // it well before twenty hours, with or without the floor.
                Assert.Equal(DownloadStatus.Failed, row!.Status);
                Assert.Contains("No download progress", row.ErrorMessage);
            }
        }

        private async Task SaveSettingsAsync(int hours, decimal floorPercent)
        {
            var settings = new ApplicationSettingsBuilder()
                .WithoutCompletionStabilityWindow()
                .WithStalledDownloadTimeoutHours(hours)
                .WithStalledDownloadFloorPercent(floorPercent)
                .WithFailedDownloadHandling()
                .Build();
            await _applicationSettingsRepository.SaveAsync(settings);
        }

        private async Task<Download> AddTorrentDownloadAsync()
        {
            var audiobook = await CreateAudiobook();
            var download = new DownloadBuilder()
                .WithDownloading(0)
                .WithTitle("A Trickling Listing")
                .WithAudiobook(audiobook)
                .WithExternalId(Hash)
                .WithDownloadClientConfiguration(_client)
                .Build();
            download.Metadata[ReleaseIdentity.MetadataKey] = ReleaseIdentity.For(Hash, null, null, null)!;
            return await _downloadRepository.AddAsync(download);
        }

        /// <summary>
        /// <paramref name="progress"/> is the raw qBittorrent fraction (0 to 1), matching
        /// StalledDownloadDetectionTests's own ReportTorrent convention.
        /// </summary>
        private void ReportTorrent(double progress, long downloaded)
        {
            _qbittorrent.InfoResponseOverride = $$"""
            [
                {
                    "hash": "{{Hash}}",
                    "name": "A Trickling Listing",
                    "progress": {{progress.ToString(CultureInfo.InvariantCulture)}},
                    "size": {{TotalSize}},
                    "downloaded": {{downloaded}},
                    "state": "downloading",
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
