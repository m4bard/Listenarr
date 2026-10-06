using System.Text.Json;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.SignalR;

namespace Listenarr.Tests.Features.Infrastructure.Library.Scanning
{
    [Trait("Name", "ScanJobProcessorNoFilesAcceptedTests")]
    [Trait("Category", "BackgroundWorkers")]
    public class ScanJobProcessorNoFilesAcceptedTests : BaseTests
    {
        private readonly List<(string Method, JsonElement Payload)> _broadcasts = [];

        public override async Task InitializeAsync()
        {
            // Metadata is unavailable (null), the case where nothing per-file is
            // recorded and only the job-level status can say why files are missing.
            _services.AddSingleton(new Mock<IMetadataService>().Object);
            var proxy = new Mock<IClientProxy>();
            proxy.Setup(p => p.SendCoreAsync(
                    It.IsAny<string>(),
                    It.IsAny<object?[]>(),
                    It.IsAny<CancellationToken>()))
                .Callback((string method, object?[] args, CancellationToken _) =>
                {
                    lock (_broadcasts)
                    {
                        _broadcasts.Add((
                            method,
                            JsonSerializer.SerializeToElement(args.FirstOrDefault())));
                    }
                })
                .Returns(Task.CompletedTask);
            var hubClients = new Mock<IHubClients>();
            hubClients.Setup(c => c.All).Returns(proxy.Object);
            var hubContext = new Mock<IHubContext<DownloadHub>>();
            hubContext.Setup(h => h.Clients).Returns(hubClients.Object);
            _services.AddSingleton(hubContext.Object);
            Init();
            await _applicationSettingsRepository.SaveAsync(
                new ApplicationSettingsBuilder()
                    .WithOutputPath(FileService.GetTempPath())
                    .Build());
        }

        [Fact]
        public async Task ProcessJobAsync_CandidatesFoundButNoneAccepted_ReportsCompletedNoFilesAccepted()
        {
            var basePath = FileService.GetTempDirectory("scan-processor-none-accepted");
            await FileService.GetFileAsync(basePath, "Track 01.m4b", "audio");
            await FileService.GetFileAsync(basePath, "Track 02.m4b", "audio");
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Expected Title Nowhere On Disk")
                .WithBasePath(basePath)
                .Build());
            var (queue, job) = await CreateQueuedScanJobAsync(audiobook, "scan:none-accepted");

            await _provider.GetRequiredService<IScanJobProcessor>()
                .ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = GetRequiredJob(queue, job.Id);
            Assert.Equal("CompletedNoFilesAccepted", updatedJob.Status);
            Assert.Equal(
                "Found 2 audio files in the scan folder that this audiobook could claim, but none were added to it. Check the files' names, tags and permissions, then rescan.",
                updatedJob.Error);
            Assert.Empty(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));

            var history = Assert.Single(
                await _historyRepository.GetByCorrelationIdAsync("scan:none-accepted"),
                entry => entry.EventType == HistoryEvents.ScanCompleted);
            Assert.Equal(HistoryOutcome.Skipped, history.Outcome);
            Assert.Contains("none", history.Message, StringComparison.OrdinalIgnoreCase);

            var update = Assert.Single(_broadcasts, b =>
                b.Method == "ScanJobUpdate"
                && b.Payload.GetProperty("jobId").GetString() == job.Id.ToString()
                && b.Payload.GetProperty("status").GetString() != "Processing");
            Assert.Equal(
                "CompletedNoFilesAccepted",
                update.Payload.GetProperty("status").GetString());
            Assert.False(string.IsNullOrWhiteSpace(
                update.Payload.GetProperty("error").GetString()));
            Assert.Contains(_broadcasts, b => b.Method == "AudiobookUpdate");

            var metricsMock = _provider.GetRequiredService<Mock<IAppMetricsService>>();
            metricsMock.Verify(m => m.Increment(
                "worker.scan.job.completed_no_files_accepted",
                It.IsAny<double>()), Times.Once);
            metricsMock.Verify(m => m.Increment(
                "worker.scan.job.skipped",
                It.IsAny<double>()), Times.Never);
        }

        [Fact]
        public async Task ProcessJobAsync_ReplayedNoFilesAcceptedJob_KeepsStatusWithoutDuplicateHistory()
        {
            var basePath = FileService.GetTempDirectory("scan-processor-none-accepted-replay");
            await FileService.GetFileAsync(basePath, "Track 01.m4b", "audio");
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Another Missing Title")
                .WithBasePath(basePath)
                .Build());
            var (queue, job) = await CreateQueuedScanJobAsync(audiobook, "scan:none-accepted-replay");

            var processor = _provider.GetRequiredService<IScanJobProcessor>();
            await processor.ProcessJobAsync(job, CancellationToken.None);
            await processor.ProcessJobAsync(job, CancellationToken.None);

            Assert.Equal("CompletedNoFilesAccepted", GetRequiredJob(queue, job.Id).Status);
            Assert.Single(
                await _historyRepository.GetByCorrelationIdAsync("scan:none-accepted-replay"),
                entry => entry.EventType == HistoryEvents.ScanCompleted);
        }

        [Fact]
        public async Task ProcessJobAsync_FolderWithNoAudioFiles_ReportsPlainCompleted()
        {
            var basePath = FileService.GetTempDirectory("scan-processor-empty-folder");
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Empty Folder Book")
                .WithBasePath(basePath)
                .Build());
            var (queue, job) = await CreateQueuedScanJobAsync(audiobook);

            await _provider.GetRequiredService<IScanJobProcessor>()
                .ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = GetRequiredJob(queue, job.Id);
            Assert.Equal("Completed", updatedJob.Status);
            Assert.Null(updatedJob.Error);
        }

        [Fact]
        public async Task ProcessJobAsync_RescanOfFullyRegisteredBook_ReportsPlainCompleted()
        {
            var basePath = FileService.GetTempDirectory("scan-processor-registered-rescan");
            await FileService.GetFileAsync(basePath, "Registered Book.m4b", "audio");
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Registered Book")
                .WithBasePath(basePath)
                .Build());
            var processor = _provider.GetRequiredService<IScanJobProcessor>();
            var (firstQueue, firstJob) = await CreateQueuedScanJobAsync(audiobook);
            await processor.ProcessJobAsync(firstJob, CancellationToken.None);
            Assert.Equal("Completed", GetRequiredJob(firstQueue, firstJob.Id).Status);
            Assert.Single(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));

            var (queue, job) = await CreateQueuedScanJobAsync(audiobook);
            Assert.NotEqual(firstJob.Id, job.Id);
            await processor.ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = GetRequiredJob(queue, job.Id);
            Assert.Equal("Completed", updatedJob.Status);
            Assert.Null(updatedJob.Error);
            Assert.Single(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));
        }

        [Fact]
        public async Task ProcessJobAsync_ScanRootHoldsOnlyAnotherBooksFiles_ReportsPlainCompleted()
        {
            var sharedPath = FileService.GetTempDirectory("scan-processor-other-owner");
            await FileService.GetFileAsync(sharedPath, "Owned Book.m4b", "audio");
            var owner = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Owned Book")
                .WithBasePath(sharedPath)
                .Build());
            var processor = _provider.GetRequiredService<IScanJobProcessor>();
            var (ownerQueue, ownerJob) = await CreateQueuedScanJobAsync(owner);
            await processor.ProcessJobAsync(ownerJob, CancellationToken.None);
            // Control: the file is claimed by its own book first.
            Assert.Equal("Completed", GetRequiredJob(ownerQueue, ownerJob.Id).Status);
            Assert.Single(await _audiobookFileRepository.GetByAudiobookIdAsync(owner.Id));

            var other = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Unrelated Other Book")
                .Build());
            var authorization = await _provider
                .GetRequiredService<IScanPathAuthorizationService>()
                .AuthorizeAsync(sharedPath);
            Assert.True(authorization.IsAuthorized, authorization.Error);
            var queue = Assert.IsType<ScanQueueService>(
                _provider.GetRequiredService<IScanQueueService>());
            var jobId = await queue.EnqueueScanAsync(new ScanEnqueueCommand(
                other,
                sharedPath,
                authorization.Identity,
                authorization.PhysicalIdentity,
                AuthorizationMode: ScanAuthorizationMode.PreauthorizedPath));
            Assert.True(queue.Reader.TryRead(out var job));
            Assert.Equal(jobId, job.Id);

            await processor.ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = GetRequiredJob(queue, job.Id);
            Assert.Equal("Completed", updatedJob.Status);
            Assert.Null(updatedJob.Error);
            Assert.Empty(await _audiobookFileRepository.GetByAudiobookIdAsync(other.Id));
        }

        [Fact]
        public async Task ProcessJobAsync_BookFolderHoldsAnotherBooksTaggedFile_ReportsCompletedNoFilesAccepted()
        {
            // The book's own folder carries its ASIN, so Discover() attributes anything inside
            // it on the folder name alone. The file's embedded tags name a different book by a
            // different author; that content evidence has to override the folder match.
            var metadata = new Mock<IMetadataService>();
            metadata.Setup(service => service.ExtractFileMetadataAsync(
                    It.IsAny<MetadataFileSource>()))
                .ReturnsAsync(new AudioMetadata
                {
                    Title = "Unrelated Title",
                    Album = "Unrelated Title",
                    Artist = "Unrelated Author",
                    AlbumArtist = "Unrelated Author",
                    Duration = TimeSpan.FromSeconds(1),
                    Format = "m4b"
                });
            _services.AddSingleton(metadata.Object);
            Init();
            await _applicationSettingsRepository.SaveAsync(
                new ApplicationSettingsBuilder()
                    .WithOutputPath(FileService.GetTempPath())
                    .Build());
            var basePath = Path.Join(
                FileService.GetTempDirectory("scan-processor-wrong-book-tags"),
                "Expected Title [B012345678]");
            Directory.CreateDirectory(basePath);
            await FileService.GetFileAsync(basePath, "borrowed.m4b", "audio");
            var audiobookToAdd = new AudiobookBuilder()
                .WithTitle("Expected Title")
                .WithAuthor("Expected Author")
                .WithBasePath(basePath)
                .Build();
            audiobookToAdd.Asin = "B012345678";
            var audiobook = await _audiobookRepository.AddAsync(audiobookToAdd);
            var (queue, job) = await CreateQueuedScanJobAsync(audiobook, "scan:wrong-book-tags");

            await _provider.GetRequiredService<IScanJobProcessor>()
                .ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = GetRequiredJob(queue, job.Id);
            Assert.Equal("CompletedNoFilesAccepted", updatedJob.Status);
            Assert.Equal(
                "Found 1 audio file in the scan folder that this audiobook could claim, but none were added to it. Check the files' names, tags and permissions, then rescan.",
                updatedJob.Error);
            Assert.Empty(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));
        }

        [Fact]
        public async Task ProcessJobAsync_FilesAttributedButEveryClaimRejected_ReportsCompletedNoFilesAccepted()
        {
            // Every ownership claim is refused, as an IdentityUnavailable or similar
            // rejection would be: attribution succeeds but nothing becomes durable.
            var fileService = new Mock<IAudiobookFileService>();
            fileService.Setup(service => service.EnsureAudiobookFileAsync(
                    It.IsAny<Audiobook>(),
                    It.IsAny<IAudiobookFileRegistrationLease>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            _services.AddSingleton(fileService.Object);
            Init();
            await _applicationSettingsRepository.SaveAsync(
                new ApplicationSettingsBuilder()
                    .WithOutputPath(FileService.GetTempPath())
                    .Build());
            var basePath = FileService.GetTempDirectory("scan-processor-claim-rejected");
            await FileService.GetFileAsync(basePath, "Rejected Claim Book.m4b", "audio");
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("Rejected Claim Book")
                .WithBasePath(basePath)
                .Build());
            var (queue, job) = await CreateQueuedScanJobAsync(audiobook);

            await _provider.GetRequiredService<IScanJobProcessor>()
                .ProcessJobAsync(job, CancellationToken.None);

            fileService.Verify(service => service.EnsureAudiobookFileAsync(
                It.IsAny<Audiobook>(),
                It.IsAny<IAudiobookFileRegistrationLease>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()), Times.Once);
            var updatedJob = GetRequiredJob(queue, job.Id);
            Assert.Equal("CompletedNoFilesAccepted", updatedJob.Status);
            Assert.Equal(
                "Found 1 audio file in the scan folder that this audiobook could claim, but none were added to it. Check the files' names, tags and permissions, then rescan.",
                updatedJob.Error);
            Assert.Empty(await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id));
        }

        [Fact]
        public async Task ProcessJobAsync_MetadataContradictsPathDiagnostic_LogsSanitizedPathNeverRawPath()
        {
            // Same wrong-book-tags fixture as the test above: folder attribution fires,
            // content verification declines it, and the AudiobookScanDiagnostic this
            // produces carries the RAW candidate path. ScanJobProcessor must re-log it
            // through LogRedaction before it reaches the job-level warning, never as-is.
            var metadata = new Mock<IMetadataService>();
            metadata.Setup(service => service.ExtractFileMetadataAsync(
                    It.IsAny<MetadataFileSource>()))
                .ReturnsAsync(new AudioMetadata
                {
                    Title = "Unrelated Title",
                    Album = "Unrelated Title",
                    Artist = "Unrelated Author",
                    AlbumArtist = "Unrelated Author",
                    Duration = TimeSpan.FromSeconds(1),
                    Format = "m4b"
                });
            _services.AddSingleton(metadata.Object);
            var capturingLogger = new CapturingLogger<ScanJobProcessor>();
            _services.AddSingleton<ILogger<ScanJobProcessor>>(capturingLogger);
            Init();
            await _applicationSettingsRepository.SaveAsync(
                new ApplicationSettingsBuilder()
                    .WithOutputPath(FileService.GetTempPath())
                    .Build());
            var basePath = Path.Join(
                FileService.GetTempDirectory("scan-processor-diagnostic-logging"),
                "Expected Title [B023456789]");
            Directory.CreateDirectory(basePath);
            await FileService.GetFileAsync(basePath, "borrowed.m4b", "audio");
            var audiobookToAdd = new AudiobookBuilder()
                .WithTitle("Expected Title")
                .WithAuthor("Expected Author")
                .WithBasePath(basePath)
                .Build();
            audiobookToAdd.Asin = "B023456789";
            var audiobook = await _audiobookRepository.AddAsync(audiobookToAdd);
            var (queue, job) = await CreateQueuedScanJobAsync(audiobook, "scan:diagnostic-logging");

            await _provider.GetRequiredService<IScanJobProcessor>()
                .ProcessJobAsync(job, CancellationToken.None);

            var updatedJob = GetRequiredJob(queue, job.Id);
            Assert.Equal("CompletedNoFilesAccepted", updatedJob.Status);

            // The raw candidate path, exactly as AudiobookScanDiagnostic.Path carries it,
            // and the directory component that SanitizeFilePath strips from it.
            var rawCandidatePath = Path.Join(basePath, "borrowed.m4b");
            Assert.Contains(basePath, rawCandidatePath, StringComparison.Ordinal);

            var diagnosticEntry = Assert.Single(capturingLogger.Entries, entry =>
                entry.Message.Contains("MetadataContradictsPath", StringComparison.Ordinal));
            Assert.Contains("borrowed.m4b", diagnosticEntry.Message, StringComparison.Ordinal);
            Assert.Contains(
                "different book and author",
                diagnosticEntry.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(basePath, diagnosticEntry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(
                rawCandidatePath,
                diagnosticEntry.Message,
                StringComparison.Ordinal);

            // No captured log entry at all leaks the directory component, not just the
            // one we expect to carry it.
            Assert.All(capturingLogger.Entries, entry =>
                Assert.DoesNotContain(basePath, entry.Message, StringComparison.Ordinal));

            // History.Data stays composed from counts only: Found/Created/Discovered/
            // Path (the scan root), unchanged in shape by this fix.
            var history = Assert.Single(
                await _historyRepository.GetByCorrelationIdAsync("scan:diagnostic-logging"),
                entry => entry.EventType == HistoryEvents.ScanCompleted);
            var data = JsonSerializer.Deserialize<JsonElement>(history.Data!);
            var dataProperties = data.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(
                new[] { "Created", "Discovered", "Found", "Path", "ScanJobId" },
                dataProperties);
            Assert.Equal(basePath, data.GetProperty("Path").GetString());
            Assert.DoesNotContain("borrowed.m4b", history.Data, StringComparison.Ordinal);
        }

        private static ScanJob GetRequiredJob(ScanQueueService queue, Guid jobId)
        {
            Assert.True(queue.TryGetJob(jobId, out var job));
            return Assert.IsType<ScanJob>(job);
        }

        private async Task<(ScanQueueService Queue, ScanJob Job)> CreateQueuedScanJobAsync(
            Audiobook audiobook,
            string? correlationId = null)
        {
            var queue = Assert.IsType<ScanQueueService>(_provider.GetRequiredService<IScanQueueService>());
            var jobId = await queue.EnqueueScanAsync(audiobook, correlationId: correlationId);
            Assert.True(queue.Reader.TryRead(out var job));
            Assert.Equal(jobId, job.Id);
            return (queue, job);
        }

        private sealed class CapturingLogger<T> : ILogger<T>
        {
            public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
