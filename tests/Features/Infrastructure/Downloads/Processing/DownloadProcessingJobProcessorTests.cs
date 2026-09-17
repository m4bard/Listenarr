using System.Text.Json;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Processing
{
    [Trait("Name", "DownloadProcessingJobProcessorTests")]
    [Trait("Category", "DownloadProcessingJob")]
    public class DownloadProcessingJobProcessorTests : BaseTests
    {
        private readonly Mock<IDownloadImportService> downloadImportServiceMock = new();
        private readonly DownloadClientGatewayMock downloadClientGatewayMock = new();

        public override async Task InitializeAsync()
        {
            _services.AddSingleton<IDownloadClientGateway>(downloadClientGatewayMock);
            Init();
            await AddAuthorizedRootAsync(FileService.GetTempPath());
        }

        [Fact]
        public async Task CompletedDownload_With_NoPathFails()
        {
            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath("")
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            var downloadProcessingJobProcessor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await downloadProcessingJobProcessor.ProcessQueueAsync(CancellationToken.None);

            downloadImportServiceMock.Verify(m => m.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()),
                Times.Never);

            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Failed, job.Status);
        }

        [Theory]
        [InlineData("directoryMissing", false)]
        [InlineData("directoryExists", true)]
        public async Task CompletedDownload_With_MissingSource(string path, bool pathExists)
        {
            var sourceDirectory = FileService.GetTempDirectory("source-directory");
            path = Path.Join(sourceDirectory, path);
            if (pathExists)
            {
                Directory.CreateDirectory(path);
            }

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(path)
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // First try
            var downloadProcessingJobProcessor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await downloadProcessingJobProcessor.ProcessQueueAsync(CancellationToken.None);

            downloadImportServiceMock.Verify(m => m.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()),
                Times.Never);

            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.Equal(1, job.RetryCount);
            Assert.NotEmpty(job.ErrorMessage);

            download = await _downloadRepository.FindAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.ImportPending, download.Status);

            job.RetryCount = job.MaxRetries;
            await TestUtils.CancelJobRetryWait(_downloadProcessingJobRepository, job);

            // Last try
            await downloadProcessingJobProcessor.ProcessQueueAsync(CancellationToken.None);

            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Failed, job.Status);

            download = await _downloadRepository.FindAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.ImportBlocked, download.Status);
            Assert.NotNull(download.ImportBlockMessages);
            Assert.Contains(download.ImportBlockMessages, m => m.Contains($"job {job.Id}", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(download.ImportBlockMessages, m => m.Contains("{job.Id}", StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [InlineData(0, ProcessingJobStatus.Failed)]
        [InlineData(3, ProcessingJobStatus.Pending)]
        [Trait("Scenario", "The configured retry budget decides whether a first failure is terminal")]
        public async Task MissingSource_RespectsTheConfiguredRetryBudget(int maxRetries, ProcessingJobStatus expected)
        {
            // Settings > Download exposes this as Missing-source Max Retries. With a budget of zero
            // the first failure is terminal; with the default of three it schedules a retry. The
            // second case is the control: without it this would also pass against an implementation
            // that always failed immediately.
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithMissingSourceMaxRetries(maxRetries)
                .Build());

            var sourceDirectory = FileService.GetTempDirectory("budget-source");
            var missing = Path.Join(sourceDirectory, "notThere");

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(missing)
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            await _provider.GetRequiredService<DownloadProcessingJobProcessor>()
                .ProcessQueueAsync(CancellationToken.None);

            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(maxRetries, job!.MaxRetries);
            Assert.Equal(expected, job.Status);
        }

        [Fact]
        [Trait("Scenario", "The configured initial delay decides when the first retry falls due")]
        public async Task MissingSource_RespectsTheConfiguredRetryInitialDelay()
        {
            // Settings > Download exposes this as Missing-source Retry Initial Delay. Nothing
            // else asserts that the processor reads it. The domain tests hand ScheduleRetry a
            // delay directly, so a processor that ignored the setting and let the parameter
            // default to thirty seconds would keep every one of them green. Ten minutes is far
            // enough from that default that the two cannot be mistaken for each other.
            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithMissingSourceRetryInitialDelaySeconds(600)
                .Build());

            var sourceDirectory = FileService.GetTempDirectory("delay-source");
            var missing = Path.Join(sourceDirectory, "notThere");

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(missing)
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            var before = DateTime.UtcNow;
            await _provider.GetRequiredService<DownloadProcessingJobProcessor>()
                .ProcessQueueAsync(CancellationToken.None);

            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Pending, job!.Status);
            Assert.NotNull(job.NextRetryAt);
            Assert.InRange((job.NextRetryAt!.Value - before).TotalSeconds, 570, 660);
        }

        [Fact]
        [Trait("Scenario", "ExternalImportResolverRecoversStaleDownloadPath")]
        public async Task Import_ExternalClientStaleDownloadPath_UsesResolvedSourceFiles()
        {
            // Arrange
            var source = FileService.GetTempDirectory("source");
            var filePath = await FileService.GetFileAsync(source, "audiobook.mp3");
            var stalePath = Path.Join(FileService.GetTempDirectory("stale-source"), "missing-client-path");

            downloadClientGatewayMock.SourceFiles = [filePath];

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithCompletedStatus(at: DateTime.UtcNow)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithAudiobook(await CreateAudiobook())
                .WithPath(stalePath)
                .Build());

            await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            // Assert
            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Moved, download.Status);
            Assert.Equal(1, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.GetQueueItemAsync)));
        }

        [Fact]
        [Trait("Scenario", "DirectDownloadMissingStagedFileRetries")]
        public async Task Import_DirectDownloadMissingStagedFile_RetriesWithoutExternalClientRecovery()
        {
            // Arrange
            var missingPath = Path.Join(FileService.GetTempDirectory("ddl-source"), "missing.m4b");
            var audiobook = await CreateAudiobook();
            var download = await _downloadRepository.AddAsync(new Download
            {
                Id = $"ddl-{Guid.NewGuid():N}",
                AudiobookId = audiobook.Id,
                Title = "DDL Book",
                Artist = "DDL Author",
                Album = "DDL Book",
                DownloadClientId = DirectDownloadMetadataKeys.ClientId,
                Status = DownloadStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                CompletedAt = DateTime.UtcNow,
                DownloadPath = missingPath,
                Metadata = new Dictionary<string, object>
                {
                    [DirectDownloadMetadataKeys.DownloadType] = DirectDownloadMetadataKeys.ClientId
                }
            });

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            // Assert
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.Equal(1, job.RetryCount);
            Assert.Contains("Direct-download source path not found", job.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.GetQueueItemAsync)));
        }

        [Fact]
        public async Task Import_DirectDownloadArchivePlan_ForcesArchiveExtraction()
        {
            // Given
            var importService = new Mock<IDownloadImportService>();
            importService
                .Setup(service => service.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()))
                .ReturnsAsync((Audiobook _, List<string> files, CancellationToken _, DownloadImportOptions? _) =>
                    [ImportResult.ImportSuccess(FileAction.Copy, files[0], files[0], wasRegisteredToAudiobook: true)]);
            Init(builder => builder.WithSingleton<IDownloadImportService>(importService.Object));
            var sourceDirectory = FileService.GetTempDirectory("ddl-archive-source");
            var archivePath = await FileService.GetFileAsync(sourceDirectory, "book.zip");
            var audiobook = await CreateAudiobook();
            var download = await _downloadRepository.AddAsync(new Download
            {
                Id = $"ddl-{Guid.NewGuid():N}",
                AudiobookId = audiobook.Id,
                Title = "DDL Archive Book",
                Artist = "DDL Author",
                Album = "DDL Archive Book",
                DownloadClientId = DirectDownloadMetadataKeys.ClientId,
                Status = DownloadStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                CompletedAt = DateTime.UtcNow,
                DownloadPath = archivePath,
                Metadata = new Dictionary<string, object>
                {
                    [DirectDownloadMetadataKeys.DownloadType] = DirectDownloadMetadataKeys.ClientId,
                    [DirectDownloadMetadataKeys.RequiresArchiveExtraction] = true
                }
            });
            await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // When
            await _provider.GetRequiredService<DownloadProcessingJobProcessor>()
                .ProcessQueueAsync(CancellationToken.None);

            // Then
            importService.Verify(service => service.ImportDownloadFilesAsync(
                It.Is<Audiobook>(item => item.Id == audiobook.Id),
                It.Is<List<string>>(files => files.Contains(archivePath)),
                It.IsAny<CancellationToken>(),
                It.Is<DownloadImportOptions>(options => options.ForceArchiveExtraction)), Times.Once);
        }

        [Fact]
        public async Task Import_FailedPublication_PersistsFailureContractInHistory()
        {
            var importService = new Mock<IDownloadImportService>();
            var sourceDirectory = FileService.GetTempDirectory("failed-publication-source");
            var sourcePath = await FileService.GetFileAsync(sourceDirectory, "book.m4b");
            var finalPath = Path.Join(FileService.GetTempDirectory("failed-publication-destination"), "book.m4b");
            const string warningCode = "weak-storage-copy-retained";
            const string message = "Move was reduced to copy and the source was retained";
            importService
                .Setup(service => service.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()))
                .ReturnsAsync([
                    new ImportResult
                    {
                        Success = false,
                        Action = FileAction.Copy,
                        RequestedAction = FileAction.Move,
                        EffectiveAction = FileAction.Copy,
                        SourceDisposition = ImportSourceDisposition.Retained,
                        WarningCode = warningCode,
                        SourcePath = sourcePath,
                        FinalPath = finalPath,
                        Message = message
                    }
                ]);
            Init(builder => builder.WithSingleton<IDownloadImportService>(importService.Object));
            var audiobook = await CreateAudiobook();
            var download = await _downloadRepository.AddAsync(new Download
            {
                Id = $"ddl-{Guid.NewGuid():N}",
                AudiobookId = audiobook.Id,
                Title = "Failed Publication Book",
                Artist = "DDL Author",
                Album = "Failed Publication Book",
                DownloadClientId = DirectDownloadMetadataKeys.ClientId,
                Status = DownloadStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                CompletedAt = DateTime.UtcNow,
                DownloadPath = sourcePath,
                Metadata = new Dictionary<string, object>
                {
                    [DirectDownloadMetadataKeys.DownloadType] = DirectDownloadMetadataKeys.ClientId
                }
            });
            // Start with the retry budget spent, so this exercises the attempt that gives up.
            // A failed publication is retried now rather than blocking on the first attempt, and
            // the FailedResults contract below is written by the terminal attempt, which is the
            // one this test is about.
            var seed = new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build();
            seed.RetryCount = seed.MaxRetries;
            var job = await _downloadProcessingJobRepository.AddAsync(seed);

            await _provider.GetRequiredService<DownloadProcessingJobProcessor>()
                .ProcessQueueAsync(CancellationToken.None);

            job = (await _downloadProcessingJobRepository.GetByIdAsync(job.Id))!;
            Assert.Equal(ProcessingJobStatus.Failed, job.Status);
            var page = await _historyRepository.QueryAsync(new HistoryQuery
            {
                DownloadId = download.Id.ToUpperInvariant(),
                Limit = 100
            });
            var failedImport = Assert.Single(page.Records, history =>
                history.EventType == HistoryEvents.ImportFailed);
            using var details = JsonDocument.Parse(failedImport.Data!);
            var failedResult = Assert.Single(details.RootElement
                .GetProperty("FailedResults")
                .EnumerateArray());
            Assert.Equal((int)FileAction.Copy, failedResult.GetProperty("Action").GetInt32());
            Assert.Equal((int)FileAction.Move, failedResult.GetProperty("RequestedAction").GetInt32());
            Assert.Equal((int)FileAction.Copy, failedResult.GetProperty("EffectiveAction").GetInt32());
            Assert.Equal((int)ImportSourceDisposition.Retained,
                failedResult.GetProperty("SourceDisposition").GetInt32());
            Assert.Equal(warningCode, failedResult.GetProperty("WarningCode").GetString());
            // The History API returns this row whole (issue #975), so the directories that
            // held the source and destination must not survive into it: only the filename.
            Assert.Equal(Path.GetFileName(sourcePath), failedResult.GetProperty("SourcePath").GetString());
            Assert.Equal(Path.GetFileName(finalPath), failedResult.GetProperty("FinalPath").GetString());
            Assert.DoesNotContain(Path.GetDirectoryName(sourcePath)!, failedImport.Data);
            Assert.DoesNotContain(Path.GetDirectoryName(finalPath)!, failedImport.Data);
            // This result carries no FailureClass (built directly rather than through a
            // classifying factory), and its Message is already a safe, descriptive sentence
            // with no path or exception text, so it survives unchanged.
            Assert.Equal(message, failedResult.GetProperty("Message").GetString());
        }

        [Fact]
        public async Task Import_FailedWithPathBearingException_ClassifiesTheFailureWithoutLeakingThePath()
        {
            // A distinctive fragment standing in for anything on a real host's directory
            // layout (a share name, a username) that must never survive into an API response.
            const string leakCanary = "path-leak-canary-marker";
            var importService = new Mock<IDownloadImportService>();
            var sourceDirectory = FileService.GetTempDirectory(leakCanary);
            var sourcePath = await FileService.GetFileAsync(sourceDirectory, "book.m4b");
            // Shaped like a real .NET DirectoryNotFoundException: it quotes the full path.
            // This is the control input a naive fix (e.g. just running the message through
            // LogRedaction.SanitizeText) would fail against, since SanitizeText strips control
            // characters and truncates but does not redact path content.
            var directoryException = new DirectoryNotFoundException(
                $"Could not find a part of the path '{sourcePath}'.");
            importService
                .Setup(service => service.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()))
                .ReturnsAsync([ImportResult.Exception(directoryException, sourcePath)]);
            Init(builder => builder.WithSingleton<IDownloadImportService>(importService.Object));
            var audiobook = await CreateAudiobook();
            var download = await _downloadRepository.AddAsync(new Download
            {
                Id = $"ddl-{Guid.NewGuid():N}",
                AudiobookId = audiobook.Id,
                Title = "Path Leak Canary Book",
                Artist = "DDL Author",
                Album = "Path Leak Canary Book",
                DownloadClientId = DirectDownloadMetadataKeys.ClientId,
                Status = DownloadStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                CompletedAt = DateTime.UtcNow,
                DownloadPath = sourcePath,
                Metadata = new Dictionary<string, object>
                {
                    [DirectDownloadMetadataKeys.DownloadType] = DirectDownloadMetadataKeys.ClientId
                }
            });
            // Start with the retry budget spent. An import whose every file failed is retried
            // now rather than blocking on the first attempt, and the ImportFailed row this
            // test inspects is written by the terminal attempt.
            var seed = new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build();
            seed.RetryCount = seed.MaxRetries;
            var job = await _downloadProcessingJobRepository.AddAsync(seed);

            await _provider.GetRequiredService<DownloadProcessingJobProcessor>()
                .ProcessQueueAsync(CancellationToken.None);

            var page = await _historyRepository.QueryAsync(new HistoryQuery
            {
                DownloadId = download.Id.ToUpperInvariant(),
                Limit = 100
            });
            var failedImport = Assert.Single(page.Records, history =>
                history.EventType == HistoryEvents.ImportFailed);

            // The control: the History API returns this row whole, so neither the
            // directory nor the canary fragment inside it may appear anywhere in the row.
            Assert.DoesNotContain(sourceDirectory, failedImport.Data, StringComparison.Ordinal);
            Assert.DoesNotContain(leakCanary, failedImport.Data, StringComparison.Ordinal);
            Assert.DoesNotContain(leakCanary, failedImport.Message ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(leakCanary, failedImport.Error ?? string.Empty, StringComparison.Ordinal);

            using var details = JsonDocument.Parse(failedImport.Data!);
            var failedResult = Assert.Single(details.RootElement
                .GetProperty("FailedResults")
                .EnumerateArray());
            // DirectoryNotFoundException classifies to a fixed sentence: the exception's
            // own text (which quoted the path) never reaches the row.
            Assert.Equal(
                "Failed to import file, destination unavailable",
                failedResult.GetProperty("Message").GetString());
            // The filename alone is retained; only the directory is stripped.
            Assert.Equal(Path.GetFileName(sourcePath), failedResult.GetProperty("SourcePath").GetString());
        }

        [Fact]
        public async Task Import_SingleFile_UpdatesStatus()
        {
            // Arrange
            var source = FileService.GetTempDirectory("source");
            var filePath = await FileService.GetFileAsync(source, "audiobook.mp3");

            downloadClientGatewayMock.SourceFiles = [filePath];

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithCompletedStatus(at: DateTime.UtcNow)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithAudiobook(await CreateAudiobook())
                .WithPath(source)
                .Build());

            await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            // Assert
            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.True(download.Status == DownloadStatus.Moved, $"Expected Moved, got {download.Status}");
        }

        [Fact]
        [Trait("Scenario", "ImportSuccessEnqueuesLibraryScan")]
        public async Task Import_Success_EnqueuesScanForAudiobookLibraryPath()
        {
            // Arrange
            var source = FileService.GetTempDirectory("source");
            var filePath = await FileService.GetFileAsync(source, "audiobook.mp3");

            downloadClientGatewayMock.SourceFiles = [filePath];

            var audiobook = await CreateAudiobook();
            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithCompletedStatus(at: DateTime.UtcNow)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithAudiobook(audiobook)
                .WithPath(source)
                .Build());

            await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            // Assert
            var scanQueue = Assert.IsType<ScanQueueService>(_provider.GetRequiredService<IScanQueueService>());
            Assert.True(scanQueue.Reader.TryRead(out var scanJob));
            Assert.Equal(audiobook.Id, scanJob.AudiobookId);
            Assert.Null(scanJob.Path);
        }

        [Fact]
        [Trait("Scenario", "StaleMovedImportJobIsIdempotent")]
        public async Task ProcessQueue_AlreadyMovedDownload_CompletesJobWithoutImportOrScan()
        {
            // Arrange
            var source = FileService.GetTempDirectory("source");
            await FileService.GetFileAsync(source, "audiobook.mp3");

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithStatus(DownloadStatus.Moved)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithAudiobook(await CreateAudiobook())
                .WithPath(source)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            // Assert
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Completed, job.Status);
            Assert.Contains(job.ProcessingLog, m => m.Contains("already imported", StringComparison.OrdinalIgnoreCase));

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Moved, download.Status);

            downloadImportServiceMock.Verify(m => m.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()),
                Times.Never);

            Assert.Equal(0, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.GetQueueItemAsync)));
            var scanQueue = Assert.IsType<ScanQueueService>(_provider.GetRequiredService<IScanQueueService>());
            Assert.False(scanQueue.Reader.TryRead(out _));
        }

        [Fact]
        public async Task Import_MultipleFiles_UpdatesStatus()
        {
            // Arrange
            var source = FileService.GetTempDirectory("source");
            var filePath1 = await FileService.GetFileAsync(source, "audiobook1.mp3");
            var filePath2 = await FileService.GetFileAsync(source, "audiobook2.mp3");
            var filePath3 = await FileService.GetFileAsync(source, "audiobook3.mp3");

            downloadClientGatewayMock.SourceFiles = [
                filePath1,
                filePath2,
                filePath3
            ];

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithCompletedStatus(at: DateTime.UtcNow)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithAudiobook(await CreateAudiobook())
                .WithPath(source)
                .Build());

            await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            // Assert
            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.True(download.Status == DownloadStatus.Moved, $"Expected Moved, got {download.Status}");
        }

        [Fact]
        public async Task Import_OnlyRelevantFiles()
        {
            var basePath = FileService.GetTempDirectory("destination");
            var sourcePath = FileService.GetTempDirectory("downloads");
            var targetAudioPath = await FileService.GetFileAsync(sourcePath, "Target Book.m4b");
            var coverPath = await FileService.GetFileAsync(sourcePath, "cover.jpg");
            await FileService.GetFileAsync(sourcePath, "Different Book.m4b");

            downloadClientGatewayMock.SourceFiles = [
                targetAudioPath,
                coverPath
            ];

            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithBasePath(basePath)
                .Build());

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(audiobook)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithCompletedStatus(at: DateTime.UtcNow)
                .WithPath(sourcePath)
                .Build());

            await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            Assert.True(File.Exists(Path.Join(basePath, "Target Book.m4b")));
            Assert.True(File.Exists(Path.Join(basePath, "cover.jpg")));
            Assert.False(File.Exists(Path.Join(basePath, "Different Book.m4b")));
        }

        [Fact]
        public async Task RetryJob_IsNotProcessedBeforeTheRetryTimerExpires()
        {
            var sourceDirectory = FileService.GetTempDirectory("source-directory");
            var path = Path.Join(sourceDirectory, "missing");

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(path)
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // First try: Should trigger a retry
            var downloadProcessingJobProcessor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await downloadProcessingJobProcessor.ProcessQueueAsync(CancellationToken.None);

            downloadImportServiceMock.Verify(m => m.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()),
                Times.Never);

            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.Equal(1, job.RetryCount);
            Assert.NotEmpty(job.ErrorMessage);

            download = await _downloadRepository.FindAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.ImportPending, download.Status);

            // Retry immediately
            await downloadProcessingJobProcessor.ProcessQueueAsync(CancellationToken.None);

            // Job is not modified
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.Equal(1, job.RetryCount);

            // Retry after timer expires
            await TestUtils.CancelJobRetryWait(_downloadProcessingJobRepository, job);
            await downloadProcessingJobProcessor.ProcessQueueAsync(CancellationToken.None);

            // Job is retried (and refailed)
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.Equal(2, job.RetryCount);
        }

        [Fact]
        public async Task ProcessJob_MarkItemImported()
        {
            var sourceDirectory = FileService.GetTempDirectory("source-directory");
            var file1 = await FileService.GetFileAsync(sourceDirectory, "Target Book.m4b");

            downloadClientGatewayMock.SourceFiles = [file1];

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(sourceDirectory)
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            Assert.Equal(0, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.MarkItemAsImportedAsync)));

            // Process the job
            var downloadProcessingJobProcessor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await downloadProcessingJobProcessor.ProcessQueueAsync(CancellationToken.None);

            // Job is retried (and refailed)
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Completed, job.Status);

            Assert.Equal(1, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.MarkItemAsImportedAsync)));
        }

        [Fact]
        public async Task FinalizationRetry_DoesNotRepeatCompletedFileImport()
        {
            var sourceDirectory = FileService.GetTempDirectory("checkpoint-source");
            var file = await FileService.GetFileAsync(sourceDirectory, "Checkpoint Book.m4b");
            downloadClientGatewayMock.SourceFiles = [file];
            downloadClientGatewayMock.MarkImportedResult = false;

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(sourceDirectory)
                .WithCompletedStatus(DateTime.UtcNow)
                .Build());
            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            job = (await _downloadProcessingJobRepository.GetByIdAsync(job.Id))!;
            Assert.True(job.HasCheckpoint("FilesImported"));
            Assert.False(job.HasCheckpoint("ClientMarkedImported"));
            Assert.Equal(DownloadStatus.ImportPending, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
            Assert.Equal(1, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.GetQueueItemAsync)));

            downloadClientGatewayMock.MarkImportedResult = true;
            await TestUtils.CancelJobRetryWait(_downloadProcessingJobRepository, job);
            await processor.ProcessQueueAsync(CancellationToken.None);

            Assert.Equal(DownloadStatus.Moved, (await _downloadRepository.GetByIdAsync(download.Id))!.Status);
            Assert.Equal(1, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.GetQueueItemAsync)));
            Assert.Equal(2, downloadClientGatewayMock.GetCallCount(nameof(downloadClientGatewayMock.MarkItemAsImportedAsync)));
        }

        // Listenarr#890. A batch whose only file carries an extension outside
        // FileUtils.AudioExtensions plans zero audio imports, so every file is demoted to a
        // companion and skipped, nothing is registered, and the job is failed. .mp4 is the
        // extension the reporters hit: it is not blacklisted, and the bytes may be the same
        // MP4/AAC a .m4b would carry, but no gate looks past Path.GetExtension.
        // The two exact strings are asserted because a generic "the job failed" assertion is
        // what let this reach users. The control for this case is the test below it, where the
        // same batch against an audiobook that already has a file row does not block.
        [Fact]
        [Trait("Scenario", "NoAudioCandidateBlocksImport")]
        public async Task Import_BatchWithNoAudioExtension_BlocksDownloadWithNoFilesRegisteredReason()
        {
            // Given
            var sourceDirectory = FileService.GetTempDirectory("no-audio-candidate-source");
            var filePath = await FileService.GetFileAsync(sourceDirectory, "Target Book.mp4");
            Assert.False(FileUtils.IsAudioFile(filePath));
            downloadClientGatewayMock.SourceFiles = [filePath];

            var audiobook = await CreateAudiobook();
            Assert.Empty(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(audiobook)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(sourceDirectory)
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // When
            await _provider.GetRequiredService<DownloadProcessingJobProcessor>()
                .ProcessQueueAsync(CancellationToken.None);

            // Then
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Failed, job.Status);
            Assert.Equal("No audio files were registered after file import", job.ErrorMessage);
            Assert.Contains(
                job.ProcessingLog,
                entry => entry.Contains("No successful audio import in batch", StringComparison.Ordinal));
            Assert.Contains(
                job.ProcessingLog,
                entry => entry.Contains(
                    "Job failed: No audio files were registered after file import",
                    StringComparison.Ordinal));
            Assert.Empty(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.ImportBlocked, download.Status);
            Assert.Equal("Unable to import the download", download.ImportBlockReason);
            Assert.NotNull(download.ImportBlockMessages);
            Assert.Contains(
                download.ImportBlockMessages,
                message => message == $"See the log of job {job.Id} for more information");
        }

        // The control for the test above, and the half that must come out differently. The
        // no-audio-registered failure is conditional on the audiobook having no AudiobookFile
        // rows, so the identical batch against an audiobook that already holds a file imports
        // nothing and still does not block. If both cases blocked, or neither did, the test
        // above would prove nothing about the condition.
        [Fact]
        [Trait("Scenario", "NoAudioCandidateDoesNotBlockWhenAudiobookHasAFile")]
        public async Task Import_BatchWithNoAudioExtension_DoesNotBlockWhenAudiobookAlreadyHasAFile()
        {
            // Given
            var sourceDirectory = FileService.GetTempDirectory("no-audio-candidate-existing-source");
            var filePath = await FileService.GetFileAsync(sourceDirectory, "Target Book.mp4");
            Assert.False(FileUtils.IsAudioFile(filePath));
            downloadClientGatewayMock.SourceFiles = [filePath];

            var audiobook = await CreateAudiobook();
            var existingFile = await FileService.GetFileAsync(
                FileService.GetTempDirectory("existing-library-file"),
                "Existing Book.m4b");
            await _audiobookFileRepository.AddAsync(new AudiobookFileBuilder()
                .WithAudiobook(audiobook)
                .WithPath(existingFile)
                .WithFormat("m4b")
                .WithSize(4)
                .Build());
            Assert.NotEmpty(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(audiobook)
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(sourceDirectory)
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // When
            await _provider.GetRequiredService<DownloadProcessingJobProcessor>()
                .ProcessQueueAsync(CancellationToken.None);

            // Then
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Completed, job.Status);
            Assert.DoesNotContain(
                job.ProcessingLog,
                entry => entry.Contains(
                    "No audio files were registered after file import",
                    StringComparison.Ordinal));

            // The same demotion ran, so only the existing-file condition separates this case
            // from the blocked one. Without this entry the two tests would not be comparable.
            Assert.Contains(
                job.ProcessingLog,
                entry => entry.Contains("No successful audio import in batch", StringComparison.Ordinal));

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.Moved, download.Status);
            Assert.Null(download.ImportBlockReason);
            Assert.Empty(download.ImportBlockMessages ?? []);

            // Nothing new was imported, which is the part both halves share. Only the existing
            // row is still there.
            Assert.Single(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));
        }

        // The no-audio-registered path calls FailImportAsync rather than ScheduleRetryAsync, so
        // it spends none of the MaxRetries budget and flips the download to Import Blocked on the
        // first processing cycle. That is what separates it from the retrying gates above, where
        // the same download sits at Pending with RetryCount 1 and only blocks once the budget is
        // exhausted, and it is why the time between Completed and Import Blocked is diagnostic.
        [Fact]
        [Trait("Scenario", "NoAudioCandidateFailsWithoutSpendingTheRetryBudget")]
        public async Task Import_BatchWithNoAudioExtension_FailsWithoutSchedulingARetry()
        {
            // Given
            var sourceDirectory = FileService.GetTempDirectory("no-audio-candidate-noretry-source");
            var filePath = await FileService.GetFileAsync(sourceDirectory, "Target Book.mp4");
            downloadClientGatewayMock.SourceFiles = [filePath];

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(sourceDirectory)
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());

            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());
            Assert.Equal(3, job.MaxRetries);

            // When
            await _provider.GetRequiredService<DownloadProcessingJobProcessor>()
                .ProcessQueueAsync(CancellationToken.None);

            // Then
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Failed, job.Status);
            Assert.Equal(0, job.RetryCount);
            Assert.Null(job.NextRetryAt);
            Assert.DoesNotContain(
                job.ProcessingLog,
                entry => entry.Contains("Scheduled for retry", StringComparison.Ordinal));
            Assert.NotEqual("Max retries (3) exceeded", job.ErrorMessage);

            download = await _downloadRepository.GetByIdAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.ImportBlocked, download.Status);
        }

        [Fact]
        [Trait("Scenario", "FailedFileImportRetriesBeforeBlocking")]
        public async Task Import_FileImportFailure_RetriesBeforeBlockingTheDownload()
        {
            // Arrange
            var source = FileService.GetTempDirectory("failing-source");
            var filePath = await FileService.GetFileAsync(source, "audiobook.mp3");
            downloadClientGatewayMock.SourceFiles = [filePath];

            var importService = new Mock<IDownloadImportService>();
            importService
                .Setup(service => service.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()))
                .ReturnsAsync((Audiobook _, List<string> files, CancellationToken _, DownloadImportOptions? _) =>
                    [ImportResult.ImportFailure(FileAction.Copy, files[0], files[0])]);
            Init(builder => builder.WithSingleton<IDownloadImportService>(importService.Object));

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(source)
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());
            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            // Act
            var processor = _provider.GetRequiredService<DownloadProcessingJobProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);

            // Assert: the first failure is retried, not treated as terminal
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.Equal(1, job.RetryCount);

            download = await _downloadRepository.FindAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.ImportPending, download.Status);

            // Act: spend the remaining attempts
            job.RetryCount = job.MaxRetries;
            await TestUtils.CancelJobRetryWait(_downloadProcessingJobRepository, job);
            await processor.ProcessQueueAsync(CancellationToken.None);

            // Assert: an import that keeps failing still ends up blocked
            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Failed, job.Status);

            download = await _downloadRepository.FindAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.ImportBlocked, download.Status);
        }

        [Fact]
        [Trait("Scenario", "PartialImportFailureStaysTerminal")]
        public async Task Import_PartialFileImportFailure_BlocksOnTheFirstAttemptAndKeepsFailedResults()
        {
            // The other half of the change above, and the reason it is scoped to the all-failed
            // case. Retrying a partial failure buys nothing: the retry re-enters the import block
            // from the top, and under the Move completed-file action the file that did import is
            // no longer in the download directory, so the count guard fails the job on a mismatch
            // caused by the earlier success. The history entry would then carry that mismatch
            // instead of the per-file FailedResults asserted below.
            var source = FileService.GetTempDirectory("partial-failure-source");
            var importedPath = await FileService.GetFileAsync(source, "one.mp3");
            var failedPath = await FileService.GetFileAsync(source, "two.mp3");
            downloadClientGatewayMock.SourceFiles = [importedPath, failedPath];

            var importService = new Mock<IDownloadImportService>();
            importService
                .Setup(service => service.ImportDownloadFilesAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<DownloadImportOptions?>()))
                .ReturnsAsync([
                    ImportResult.ImportSuccess(FileAction.Move, importedPath, importedPath, wasRegisteredToAudiobook: true),
                    ImportResult.ImportFailure(FileAction.Move, failedPath, failedPath)
                ]);
            Init(builder => builder.WithSingleton<IDownloadImportService>(importService.Object));

            var download = await _downloadRepository.AddAsync(new DownloadBuilder()
                .WithAudiobook(await CreateAudiobook())
                .WithDownloadClientConfiguration(await CreateDownloadClientConfiguration())
                .WithPath(source)
                .WithCompletedStatus(at: DateTime.UtcNow)
                .Build());
            var job = await _downloadProcessingJobRepository.AddAsync(new DownloadProcessingJobBuilder()
                .WithDownload(download)
                .Build());

            await _provider.GetRequiredService<DownloadProcessingJobProcessor>()
                .ProcessQueueAsync(CancellationToken.None);

            job = await _downloadProcessingJobRepository.GetByIdAsync(job.Id);
            Assert.NotNull(job);
            Assert.Equal(ProcessingJobStatus.Failed, job.Status);
            Assert.Equal(0, job.RetryCount);

            download = await _downloadRepository.FindAsync(download.Id);
            Assert.NotNull(download);
            Assert.Equal(DownloadStatus.ImportBlocked, download.Status);

            var page = await _historyRepository.QueryAsync(new HistoryQuery
            {
                DownloadId = download.Id.ToUpperInvariant(),
                Limit = 100
            });
            var failedImport = Assert.Single(page.Records, history =>
                history.EventType == HistoryEvents.ImportFailed);
            using var details = JsonDocument.Parse(failedImport.Data!);
            var failedResult = Assert.Single(details.RootElement
                .GetProperty("FailedResults")
                .EnumerateArray());
            // History rows never carry an absolute filesystem path (issue #975); SourcePath is
            // reduced to its filename before it reaches this JSON.
            Assert.Equal(Path.GetFileName(failedPath), failedResult.GetProperty("SourcePath").GetString());
        }
    }
}
