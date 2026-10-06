using System.Reflection;
using System.Xml.Linq;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;
using Listenarr.Tests.Mocks.Api;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Monitoring
{
    [Trait("Name", "DownloadMonitorServiceTests")]
    [Trait("Category", "DownloadMonitorService")]
    public class DownloadMonitorServiceTests : BaseTests
    {
        private DownloadMonitorService downloadMonitorService = null!;
        private MethodInfo monitorDownloadsAsync = null!;
        private DownloadClientConfiguration client = null!;
        private DownloadClientConfiguration disabledClient = null!;

        public override async Task InitializeAsync()
        {
            downloadMonitorService = _provider.GetRequiredService<DownloadMonitorService>();
            var method = typeof(DownloadMonitorService).GetMethod("MonitorDownloadsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            monitorDownloadsAsync = method;

            client = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithType("mock")
                .WithName("Mock")
                .Build());

            disabledClient = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithType("mock")
                .WithName("Mock")
                .WithDisabled()
                .Build());
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_NoDownload_NoResult()
        {
            var downloads = await _downloadRepository.GetAllAsync();
            Assert.Empty(downloads);

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            downloads = await _downloadRepository.GetAllAsync();
            Assert.Empty(downloads);

            var downloadProcessingJobService = _provider.GetRequiredService<IDownloadProcessingJobService>();
            var job = await downloadProcessingJobService.GetNextJobAsync();
            Assert.Null(job);
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_CompletedStaysCompleted()
        {
            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithCompletedStatus(DateTime.UtcNow)
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Completed, download.Status);

            var downloadProcessingJobService = _provider.GetRequiredService<IDownloadProcessingJobService>();
            var job = await downloadProcessingJobService.GetNextJobAsync();
            Assert.Null(job);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Completed, download.Status);
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_DownloadingBecomesCompleted()
        {
            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithExternalId("1")
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Downloading, download.Status);
            Assert.True(download.Progress > 50);

            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Downloading, download.Status);
            Assert.True(download.Progress > 80);

            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Completed, download.Status);
            Assert.True(download.Progress >= 100);

            var downloadProcessingJobService = _provider.GetRequiredService<IDownloadProcessingJobService>();
            var job = await downloadProcessingJobService.GetNextJobAsync();
            Assert.NotNull(job);
            Assert.Equal(download.Id, job.DownloadId);
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_LiveSnapshot_RemovesEligibleOrphanedDownload()
        {
            var orphan = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithId("orphaned-download")
                .WithDownloading(0)
                .WithExternalId("missing-client-id")
                .WithStartDate(DateTime.UtcNow.AddMinutes(-10))
                .WithDownloadClientConfiguration(client)
                .Build());
            var present = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithId("present-download")
                .WithDownloading(0)
                .WithExternalId("1")
                .WithStartDate(DateTime.UtcNow.AddMinutes(-10))
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            Assert.Null(await _downloadRepository.GetByIdAsync(orphan.Id));
            Assert.NotNull(await _downloadRepository.GetByIdAsync(present.Id));
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_LiveSnapshot_RemovesOldActiveClientDownloadWithoutExternalId()
        {
            var unlinked = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithId("unlinked-download")
                .WithDownloading(0)
                .WithStartDate(DateTime.UtcNow.AddMinutes(-10))
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            Assert.Null(await _downloadRepository.GetByIdAsync(unlinked.Id));
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_OrphanCleanup_DoesNotRunEveryCycle()
        {
            var adapter = _provider.GetServices<IDownloadClientAdapter>()
                .OfType<DownloadCLientAdapterMock>()
                .Single();
            await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithExternalId("1")
                .WithStartDate(DateTime.UtcNow.AddMinutes(-10))
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            Assert.Equal(2, adapter.FilteredQueueRequestCount);
            Assert.Equal(1, adapter.FullSnapshotQueueRequestCount);
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_OrphanCleanup_RunsAgainAfterThrottleInterval()
        {
            var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
            Init(services => services.WithSingleton<TimeProvider>(timeProvider));
            downloadMonitorService = _provider.GetRequiredService<DownloadMonitorService>();
            client = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithType("mock")
                .WithName("Mock")
                .Build());
            var adapter = _provider.GetServices<IDownloadClientAdapter>()
                .OfType<DownloadCLientAdapterMock>()
                .Single();
            await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithExternalId("1")
                .WithStartDate(DateTime.UtcNow.AddMinutes(-10))
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            timeProvider.Advance(TimeSpan.FromMinutes(9));
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            timeProvider.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(1)));
            downloadMonitorService.ScheduleNextClientPoll(client, -100);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            Assert.Equal(3, adapter.FilteredQueueRequestCount);
            Assert.Equal(2, adapter.FullSnapshotQueueRequestCount);
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_RespectSchedulingInterval()
        {
            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithExternalId("1")
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Downloading, download.Status);
            Assert.True(download.Progress == 10);

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Downloading, download.Status);
            Assert.True(download.Progress == 10);

            var downloadProcessingJobService = _provider.GetRequiredService<IDownloadProcessingJobService>();
            var job = await downloadProcessingJobService.GetNextJobAsync();
            Assert.Null(job);
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        public async Task MonitorDownloadsAsync_DisabledClientDownload_DoesNotUpdate()
        {
            await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithExternalId("DISABLED_1")
                .WithDownloadClientConfiguration(disabledClient)
                .Build());
            await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithExternalId("DISABLED_2")
                .WithDownloadClientConfiguration(disabledClient)
                .Build());
            await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithDownloading(0)
                .WithExternalId("1")
                .WithDownloadClientConfiguration(client)
                .Build());

            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            var downloads = await _downloadRepository.GetAllAsync();
            Assert.Equal(3, downloads.Count);

            foreach (Download download in downloads)
            {
                if (download.DownloadClientId == client.Id)
                {
                    Assert.Equal(10, download.Progress);
                }
                else
                {
                    Assert.Equal(0, download.Progress);
                    Assert.Equal(DownloadStatus.Downloading, download.Status);
                }
            }

            var downloadProcessingJobService = _provider.GetRequiredService<IDownloadProcessingJobService>();
            var job = await downloadProcessingJobService.GetNextJobAsync();
            Assert.Null(job);
        }

        [Fact]
        public void OnDownloadFailed_Nzbget_DoesNotRemoveClientHistory_WhenFailedHandlingEnabled()
        {
            var client = new DownloadClientConfiguration
            {
                Id = "nzbget-1",
                Type = "nzbget"
            };

            Assert.False(DownloadMonitorProcessor.ShouldRemoveFailedClientItem(client));
        }

        [Fact]
        public void OnDownloadFailed_Qbittorrent_RemovesFailedClientItem_WhenFailedHandlingEnabled()
        {
            var client = new DownloadClientConfiguration
            {
                Id = "qbittorrent-1",
                Type = "qbittorrent"
            };

            Assert.True(DownloadMonitorProcessor.ShouldRemoveFailedClientItem(client));
        }

        [Fact]
        public void OnDownloadFailed_NzbgetMoveFailure_SuppressesImmediateAutoSearch()
        {
            var client = new DownloadClientConfiguration
            {
                Id = "nzbget-1",
                Type = "nzbget"
            };
            var download = new DownloadBuilder().Build();
            download.Metadata["ClientFailureReason"] = "FAILURE/MOVE";

            Assert.True(DownloadMonitorProcessor.ShouldSuppressFailedDownloadAutoSearch(
                client,
                download,
                "NZBGet failed during post-processing or final move."));
        }

        [Fact]
        public void OnDownloadFailed_NzbgetUnpackFailure_AllowsImmediateAutoSearch()
        {
            var client = new DownloadClientConfiguration
            {
                Id = "nzbget-1",
                Type = "nzbget"
            };
            var download = new DownloadBuilder().Build();
            download.Metadata["ClientFailureReason"] = "FAILURE/UNPACK";

            Assert.False(DownloadMonitorProcessor.ShouldSuppressFailedDownloadAutoSearch(
                client,
                download,
                "NZBGet failed while unpacking."));
        }

        [Fact]
        [Trait("Method", "OnDownloadFailed")]
        public async Task OnDownloadFailed_WithFailedDownloadHandlingOff_WritesNoBlocklistEntry()
        {
            // A blocklist entry is durable state with no expiry and, before the delete endpoints,
            // no way out at all. Writing one while the operator has failed-download handling
            // switched off accumulates permanent bans that nothing in the UI hints at.
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithoutFailedDownloadHandling()
                .Build());

            var download = await AddFailedDownloadAsync("handling-off");
            await InvokeOnDownloadFailedAsync(download);

            var blocklist = _provider.GetRequiredService<IBlocklistService>();
            Assert.Empty(await blocklist.GetForAudiobookAsync(download.AudiobookId!.Value));
        }

        [Fact]
        [Trait("Method", "OnDownloadFailed")]
        public async Task OnDownloadFailed_WithFailedDownloadHandlingOn_WritesTheBlocklistEntry()
        {
            // The control for the test above. Without it, a BlockAsync that had stopped being
            // called at all, or an identity that came back null, would read as a pass there.
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithFailedDownloadHandling()
                .Build());

            var download = await AddFailedDownloadAsync("handling-on");
            await InvokeOnDownloadFailedAsync(download);

            var blocklist = _provider.GetRequiredService<IBlocklistService>();
            var entry = Assert.Single(await blocklist.GetForAudiobookAsync(download.AudiobookId!.Value));
            Assert.Equal("The Failing Listing", entry.Title);
        }

        [Fact]
        [Trait("Method", "OnDownloadFailed")]
        public async Task OnDownloadFailed_WhenBlocklistThrowsUnexpectedException_StillRemovesFromClientAndStillAutoSearches()
        {
            // BlocklistService.BlockAsync only swallows the one expected race (two near-
            // simultaneous failures for the same release). Anything else it throws must not
            // abort the client removal and auto-search that follow, even though History for
            // this failure was already written before this method runs.
            var gatewayMock = new DownloadClientGatewayMock { RemoveResult = true };

            var blocklistMock = new Mock<IBlocklistService>();
            blocklistMock
                .Setup(service => service.BlockAsync(
                    It.IsAny<int>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<long?>(),
                    It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("simulated unexpected blocklist failure"));

            var downloadServiceMock = new Mock<IDownloadService>();
            downloadServiceMock
                .Setup(service => service.SearchAndDownloadAsync(It.IsAny<int>()))
                .ReturnsAsync(new SearchAndDownloadResult { Success = true });

            // Everything that needs to be visible to the freshly built provider's repositories
            // is created after this call: Init() swaps in a new ServiceProvider, and data saved
            // through repositories resolved from the old one is not guaranteed to be visible to
            // DbContext instances resolved from the new one.
            Init(services => services
                .WithSingleton<IDownloadClientGateway>(gatewayMock)
                .WithSingleton<IBlocklistService>(blocklistMock.Object)
                .WithSingleton<IDownloadService>(downloadServiceMock.Object));

            client = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithType("mock")
                .WithName("Mock")
                .Build());

            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithFailedDownloadHandling()
                .WithFailedDownloadAutoSearch()
                .Build());

            var audiobook = await CreateAudiobook();

            var download = new DownloadBuilder()
                .WithId("blocklist-throws")
                .WithStatus(DownloadStatus.Failed)
                .WithTitle("The Failing Listing")
                .WithAudiobook(audiobook)
                .WithDownloadClientConfiguration(client)
                .WithExternalId("ext-blocklist-throws")
                .Build();
            download.Metadata[ReleaseIdentity.MetadataKey] =
                ReleaseIdentity.For("1234567890ABCDEF1234567890ABCDEF12345678", null, null, null)!;
            download = await _downloadRepository.AddAsync(download);

            await InvokeOnDownloadFailedAsync(download);

            Assert.Equal(1, gatewayMock.GetCallCount(nameof(IDownloadClientGateway.RemoveAsync)));
            downloadServiceMock.Verify(
                service => service.SearchAndDownloadAsync(audiobook.Id),
                Times.Once);
        }

        [Fact]
        [Trait("Method", "OnDownloadFailed")]
        public async Task OnDownloadFailed_WhenAutoSearchThrows_DoesNotPropagateAndEarlierStepsAlreadyCompleted()
        {
            // The auto-search step already had its own try/catch before this fix. This
            // confirms that isolation still holds (and that bumping its log level to Warning
            // did not change the behaviour): a throwing auto-search must not undo, or be seen
            // as undoing, the blocklist entry and the client removal that ran before it.
            var gatewayMock = new DownloadClientGatewayMock { RemoveResult = true };

            var downloadServiceMock = new Mock<IDownloadService>();
            downloadServiceMock
                .Setup(service => service.SearchAndDownloadAsync(It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("simulated auto-search failure"));

            // See the comment on the equivalent Init() call above: data has to be created after
            // this, against the repositories the new provider hands back.
            Init(services => services
                .WithSingleton<IDownloadClientGateway>(gatewayMock)
                .WithSingleton<IDownloadService>(downloadServiceMock.Object));

            client = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithType("mock")
                .WithName("Mock")
                .Build());

            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithFailedDownloadHandling()
                .WithFailedDownloadAutoSearch()
                .Build());

            var audiobook = await CreateAudiobook();

            var download = new DownloadBuilder()
                .WithId("autosearch-throws")
                .WithStatus(DownloadStatus.Failed)
                .WithTitle("The Failing Listing")
                .WithAudiobook(audiobook)
                .WithDownloadClientConfiguration(client)
                .WithExternalId("ext-autosearch-throws")
                .Build();
            download.Metadata[ReleaseIdentity.MetadataKey] =
                ReleaseIdentity.For("ABCDEF1234567890ABCDEF1234567890ABCDEF99", null, null, null)!;
            download = await _downloadRepository.AddAsync(download);

            // Must not throw: the auto-search failure is swallowed, same as before this fix.
            await InvokeOnDownloadFailedAsync(download);

            var blocklist = _provider.GetRequiredService<IBlocklistService>();
            Assert.Single(await blocklist.GetForAudiobookAsync(audiobook.Id));
            Assert.Equal(1, gatewayMock.GetCallCount(nameof(IDownloadClientGateway.RemoveAsync)));
            // Without this, a lookup failure that skipped the auto-search branch entirely (never
            // reaching the throw) would read as a pass here for the wrong reason.
            downloadServiceMock.Verify(
                service => service.SearchAndDownloadAsync(audiobook.Id),
                Times.Once);
        }

        // AC: tracker#333. These three exercise the real pipeline end to end -
        // NzbgetApiMock's XML-RPC "history" response, through the real NzbgetAdapter and
        // NzbgetHistoryEnrichmentWorkflow (ClassifyOutcome), through the real
        // DownloadClientGateway and DownloadMonitorProcessor, into the real BlocklistService
        // - rather than asserting classification alone. Covers both halves the task calls
        // for: the NZBGet-reported delete family reaching a Failed Download row, and that
        // Failed transition actually reaching a written blocklist entry (or, for the benign
        // MANUAL case, confirming no entry is written at all).
        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        [Trait("Third-Party", "Nzbget")]
        public async Task MonitorDownloadsAsync_NzbgetDeletedCopyHistory_FailsDownloadAndBlocksRelease()
        {
            var nzbgetClient = await SetUpNzbgetClientAsync();
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithFailedDownloadHandling()
                .Build());
            var audiobook = await CreateAudiobook();
            var download = await AddActiveNzbgetDownloadAsync(
                "nzbget-deleted-copy",
                "901",
                audiobook,
                nzbgetClient,
                "1111111111111111111111111111111111111111");
            QueueNzbgetHistoryPoll(
                nzbId: "901",
                title: "Audiobook 901",
                status: "DELETED/COPY",
                deleteStatus: "COPY",
                markStatus: "NONE");

            var downloadMonitorService = _provider.GetRequiredService<DownloadMonitorService>();
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            var updated = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(updated);
            Assert.Equal(DownloadStatus.Failed, updated!.Status);

            var blocklist = _provider.GetRequiredService<IBlocklistService>();
            Assert.Single(await blocklist.GetForAudiobookAsync(audiobook.Id));
        }

        // DUPE, HEALTH and SCAN are covered at the classification level
        // (HistoryReader_DeleteStatusFamily_ClassifiesFailedOrIgnoredLikeSonarrReadarr in
        // NzbgetAdapterTests.cs) rather than repeated here: the task brief allows reusing
        // either the pipeline-level or the classification-level assertion for that case,
        // and COPY above already proves the DeleteStatus family reaches the blocklist end
        // to end, so repeating the full DI pipeline for every sibling value would only be
        // retesting the same wiring four times over.
        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        [Trait("Third-Party", "Nzbget")]
        public async Task MonitorDownloadsAsync_NzbgetDeletedManualWithoutBadMark_LeavesDownloadActiveAndWritesNoBlocklistEntry()
        {
            var nzbgetClient = await SetUpNzbgetClientAsync();
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithFailedDownloadHandling()
                .Build());
            var audiobook = await CreateAudiobook();
            var download = await AddActiveNzbgetDownloadAsync(
                "nzbget-deleted-manual-benign",
                "902",
                audiobook,
                nzbgetClient,
                "2222222222222222222222222222222222222222");
            QueueNzbgetHistoryPoll(
                nzbId: "902",
                title: "Audiobook 902",
                status: "DELETED/MANUAL",
                deleteStatus: "MANUAL",
                markStatus: "NONE");

            var downloadMonitorService = _provider.GetRequiredService<DownloadMonitorService>();
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            var updated = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(updated);
            Assert.Equal(DownloadStatus.Downloading, updated!.Status);

            var blocklist = _provider.GetRequiredService<IBlocklistService>();
            Assert.Empty(await blocklist.GetForAudiobookAsync(audiobook.Id));
        }

        [Fact]
        [Trait("Method", "MonitorDownloadsAsync")]
        [Trait("Third-Party", "Nzbget")]
        public async Task MonitorDownloadsAsync_NzbgetDeletedManualMarkedBad_FailsDownloadAndBlocksRelease()
        {
            var nzbgetClient = await SetUpNzbgetClientAsync();
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithFailedDownloadHandling()
                .Build());
            var audiobook = await CreateAudiobook();
            var download = await AddActiveNzbgetDownloadAsync(
                "nzbget-deleted-manual-bad",
                "903",
                audiobook,
                nzbgetClient,
                "3333333333333333333333333333333333333333");
            QueueNzbgetHistoryPoll(
                nzbId: "903",
                title: "Audiobook 903",
                status: "DELETED/MANUAL",
                deleteStatus: "MANUAL",
                markStatus: "BAD");

            var downloadMonitorService = _provider.GetRequiredService<DownloadMonitorService>();
            await downloadMonitorService.MonitorDownloadsAsync(CancellationToken.None);

            var updated = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(updated);
            Assert.Equal(DownloadStatus.Failed, updated!.Status);

            var blocklist = _provider.GetRequiredService<IBlocklistService>();
            Assert.Single(await blocklist.GetForAudiobookAsync(audiobook.Id));
        }

        private async Task<DownloadClientConfiguration> SetUpNzbgetClientAsync()
        {
            return await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithType("nzbget")
                .WithName("NZBGet")
                .WithHost("localhost")
                .WithPort(6789)
                .WithApiKey("apiKey")
                .Build());
        }

        private async Task<Download> AddActiveNzbgetDownloadAsync(
            string id,
            string externalId,
            Audiobook audiobook,
            DownloadClientConfiguration nzbgetClient,
            string releaseIdentityHash)
        {
            var download = new DownloadBuilder()
                .WithId(id)
                .WithStatus(DownloadStatus.Downloading)
                .WithTitle($"Audiobook {externalId}")
                .WithAudiobook(audiobook)
                .WithDownloadClientConfiguration(nzbgetClient)
                .WithExternalId(externalId)
                .Build();
            download.Metadata[ReleaseIdentity.MetadataKey] =
                ReleaseIdentity.For(releaseIdentityHash, null, null, null)!;
            return await _downloadRepository.AddAsync(download);
        }

        // Queues one round of listgroups (always empty - no active telemetry to merge
        // against) + history for FetchDownloadsAsync's poll, and a second, identical round
        // for the orphan-cleanup poll that runs later in the same MonitorDownloadsAsync
        // cycle. By the time that second poll runs the Download under test has already
        // transitioned (or not) in the first round, so its content does not change either
        // assertion; it only needs to exist so NzbgetApiMock does not 404 the second
        // listgroups call.
        private void QueueNzbgetHistoryPoll(
            string nzbId,
            string title,
            string status,
            string deleteStatus,
            string markStatus)
        {
            var apiMock = _provider.GetRequiredService<NzbgetApiMock>();
            var historyEntry = HistoryEntryValue(nzbId, title, status, deleteStatus, markStatus);
            for (var round = 0; round < 2; round++)
            {
                apiMock.QueueXmlRpcResponse(
                    "listgroups",
                    NzbgetApiMock.CreateListGroupsResponse(string.Empty));
                apiMock.QueueXmlRpcResponse(
                    "history",
                    NzbgetApiMock.CreateHistoryResponse(historyEntry));
            }
        }

        private static string HistoryEntryValue(
            string nzbId,
            string title,
            string status,
            string deleteStatus,
            string markStatus)
        {
            var members = new[]
            {
                HistoryMember("NZBID", nzbId),
                HistoryMember("NZBName", title),
                HistoryMember("Category", string.Empty),
                HistoryMember("Status", status),
                HistoryMember("DeleteStatus", deleteStatus),
                HistoryMember("MarkStatus", markStatus),
                HistoryMember("FinalDir", string.Empty),
                HistoryMember("DestDir", string.Empty),
                HistoryMember("FileSizeMB", "100"),
                HistoryMember("DownloadedSizeMB", "100")
            };

            return $"<value><struct>{string.Concat(members)}</struct></value>";
        }

        private static string HistoryMember(string name, string value)
        {
            return new XElement(
                "member",
                new XElement("name", name),
                new XElement("value", new XElement("string", value)))
                .ToString(SaveOptions.DisableFormatting);
        }

        private async Task<Download> AddFailedDownloadAsync(string id)
        {
            var audiobook = await CreateAudiobook();
            var download = new DownloadBuilder()
                .WithId(id)
                .WithStatus(DownloadStatus.Failed)
                .WithTitle("The Failing Listing")
                .WithAudiobook(audiobook)
                .WithDownloadClientConfiguration(client)
                .Build();
            // The builder sets no external id, so the client-removal branch below the gate stays
            // out of this.
            download.Metadata[ReleaseIdentity.MetadataKey] =
                ReleaseIdentity.For("ABCDEF1234567890ABCDEF1234567890ABCDEF12", null, null, null)!;
            return await _downloadRepository.AddAsync(download);
        }

        private async Task InvokeOnDownloadFailedAsync(Download download)
        {
            // The failure handling lives on the processor rather than on the hosted service, and
            // the only public route to it is a whole poll cycle against a mock client that would
            // have to be persuaded to report a failure. Reflection keeps the test about the one
            // branch it is asking after, the way MonitorDownloadsAsync is reached above.
            var processor = _provider.GetRequiredService<IDownloadMonitorProcessor>();
            var method = processor.GetType().GetMethod(
                "OnDownloadFailed",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            await (Task)method.Invoke(
                processor,
                [download, client, "simulated client failure", CancellationToken.None])!;
        }

        private sealed class MutableTimeProvider(DateTimeOffset currentTime) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => currentTime;

            public void Advance(TimeSpan value) => currentTime = currentTime.Add(value);
        }
    }
}
