/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Net;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks.Api;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Cleanup
{
    /// <summary>
    /// Deferred cleanup composed with the real qBittorrent adapter over the HTTP mock, so the
    /// processor's reading of RemoveAsync's result is exercised against what the adapter returns
    /// for each presence-check answer.
    /// </summary>
    [Trait("Name", "MovedDownloadCleanupQbittorrentTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class MovedDownloadCleanupQbittorrentTests : BaseTests
    {
        private const string TorrentHash = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

        public override Task InitializeAsync()
        {
            _services.AddSingleton<IMovedDownloadCleanupProcessor, MovedDownloadCleanupProcessor>();
            Init();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task RunCycleAsync_WhenTorrentPresent_DeletesItAndRemovesRecord()
        {
            var apiMock = _provider.GetRequiredService<QbittorrentApiMock>();
            apiMock.InfoResponseOverride = $$"""[{"hash":"{{TorrentHash.ToLowerInvariant()}}","name":"Book"}]""";
            var download = await AddImportedTorrentDownloadAsync();
            apiMock.ResetRequestHistory();

            await _provider.GetRequiredService<IMovedDownloadCleanupProcessor>()
                .RunCycleAsync(CancellationToken.None);

            Assert.Null(await _downloadRepository.GetByIdAsync(download.Id));
            Assert.NotNull(apiMock.LastDeleteForm);
            Assert.Equal(TorrentHash, apiMock.LastDeleteForm!["hashes"]);
        }

        [Fact]
        public async Task RunCycleAsync_WhenTorrentAlreadyAbsentFromClient_RemovesRecordWithoutDelete()
        {
            var apiMock = _provider.GetRequiredService<QbittorrentApiMock>();
            apiMock.InfoResponseOverride = "[]";
            var download = await AddImportedTorrentDownloadAsync();
            apiMock.ResetRequestHistory();

            await _provider.GetRequiredService<IMovedDownloadCleanupProcessor>()
                .RunCycleAsync(CancellationToken.None);

            Assert.Null(await _downloadRepository.GetByIdAsync(download.Id));
            Assert.Contains(apiMock.RequestHistory,
                request => request.RequestUri.AbsolutePath.EndsWith("/api/v2/torrents/info", StringComparison.Ordinal));
            Assert.Null(apiMock.LastDeleteForm);
            var history = await GetCleanupHistoryAsync(download.Id);
            Assert.Contains(history.Records, entry => entry.EventType == HistoryEvents.CleanupSucceeded);
            Assert.DoesNotContain(history.Records, entry => entry.EventType == HistoryEvents.CleanupFailed);
        }

        [Fact]
        public async Task RunCycleAsync_WhenPresenceCannotBeDetermined_RetainsRecordWithoutDelete()
        {
            var apiMock = _provider.GetRequiredService<QbittorrentApiMock>();
            apiMock.InfoStatusCode = HttpStatusCode.InternalServerError;
            var download = await AddImportedTorrentDownloadAsync();
            apiMock.ResetRequestHistory();

            await _provider.GetRequiredService<IMovedDownloadCleanupProcessor>()
                .RunCycleAsync(CancellationToken.None);

            Assert.NotNull(await _downloadRepository.GetByIdAsync(download.Id));
            Assert.Null(apiMock.LastDeleteForm);
            var history = await GetCleanupHistoryAsync(download.Id);
            Assert.Contains(history.Records, entry => entry.EventType == HistoryEvents.CleanupFailed);
            Assert.DoesNotContain(history.Records, entry => entry.EventType == HistoryEvents.CleanupSucceeded);
        }

        private async Task<Download> AddImportedTorrentDownloadAsync()
        {
            var client = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithHost("localhost")
                .WithPort(8080)
                .WithUsername("admin")
                .WithPassword("admin")
                .WithType("qbittorrent")
                .Build());
            client.RemoveCompletedDownloads = "remove";
            client = await _downloadClientConfigurationRepository.SaveAsync(client);

            var download = new DownloadBuilder()
                .WithDownloadClientConfiguration(client)
                .WithCompletedStatus(DateTime.UtcNow.AddMinutes(-5))
                .WithTorrentHash(TorrentHash)
                .Build();
            download.Status = DownloadStatus.Moved;
            download.Metadata["CanBeRemoved"] = true;
            download = await _downloadRepository.AddAsync(download);

            var job = new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .WithCompleted(DateTime.UtcNow)
                .Build();
            job.JobData["SourceRetained"] = false;
            await _downloadProcessingJobRepository.AddAsync(job);

            return download;
        }

        private Task<HistoryPage> GetCleanupHistoryAsync(string downloadId) =>
            _historyRepository.QueryAsync(new HistoryQuery
            {
                DownloadId = downloadId.ToUpperInvariant(),
                Limit = 100
            });
    }
}
