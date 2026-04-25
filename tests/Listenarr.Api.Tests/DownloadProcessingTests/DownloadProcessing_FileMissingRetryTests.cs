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
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using Listenarr.Api.Services;
using Listenarr.Api.Services.Metadata;
using Microsoft.Extensions.Logging;

namespace Listenarr.Api.Tests
{
    [Trait("Category", "DownloadProcessing")]
    public class DownloadProcessing_FileMissingRetryTests : BaseTests
    {
        [Fact]
        public async Task ProcessMoveOrCopy_IfSourceMissing_SchedulesRetryAndRecordsMetric()
        {
            var destRoot = GetTempDirectory("dl-dest");
            var sourceFile = await GetFileAsync(GetTempPath(), "dl-missing.mp3");

            var dl = new Download
            {
                Id = "missing-test-1",
                Status = DownloadStatus.Completed,
                DownloadPath = sourceFile,
                FinalPath = sourceFile,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };

            var services = MockUtils.InitServiceCollection();

            var configMock = new Mock<IConfigurationService>();
            configMock.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(new ApplicationSettings
            {
                OutputPath = destRoot,
                CompletedFileAction = "Move",
                EnableMetadataProcessing = false
            });
            services.AddSingleton<IConfigurationService>(configMock.Object);

            // Mock IImportItemResolutionService - just returns the preliminary item unchanged
            var importResolutionMock = new Mock<IImportItemResolutionService>();
            importResolutionMock
                .Setup(x => x.ResolveImportItemAsync(It.IsAny<Download>(), It.IsAny<QueueItem>(), It.IsAny<QueueItem>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Download d, QueueItem q, QueueItem? p, CancellationToken ct) => q);
            services.AddScoped<IImportItemResolutionService>(_ => importResolutionMock.Object);
            
            var metricsMock = new Mock<IAppMetricsService>();
            services.AddSingleton<IAppMetricsService>(metricsMock.Object);

            var provider = services.BuildServiceProvider();
            var downloadRepository = provider.GetRequiredService<IDownloadRepository>();

            await downloadRepository.AddAsync(dl);

            // Enqueue the job pointing to the source file
            var queueService = provider.GetRequiredService<IDownloadProcessingQueueService>();
            var jobId = await queueService.QueueDownloadProcessingAsync(dl.Id, sourceFile, null);
            var job = await queueService.GetJobAsync(jobId);

            // Delete the source to simulate disappearance before processing
            try { File.Delete(sourceFile); } catch (IOException ex) { _ = ex; } catch (UnauthorizedAccessException ex) { _ = ex; }

            // Create the background service instance (no longer needs importItemResolution in constructor)
            var svc = new DownloadProcessingBackgroundService(
                provider.GetRequiredService<IServiceScopeFactory>(), 
                provider.GetRequiredService<ILogger<DownloadProcessingBackgroundService>>(), 
                metricsMock.Object);

            // Set job to processing (the outer loop normally does this)
            job.Status = ProcessingJobStatus.Processing;
            job.StartedAt = DateTime.UtcNow;
            await queueService.UpdateJobAsync(job);

            using var scope = provider.CreateScope();

            var method = typeof(DownloadProcessingBackgroundService).GetMethod("ProcessMoveOrCopyJobAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            // Invoke and await the returned Task
            var task = (Task)method!.Invoke(svc, new object[] { job, scope, CancellationToken.None })!;
            await task;

            // Persist job updates (ProcessQueueAsync usually updates job at the end)
            await queueService.UpdateJobAsync(job);

            // Reload job
            var updated = await queueService.GetJobAsync(job.Id);
            Assert.NotNull(updated);
            Assert.Equal(ProcessingJobStatus.Retry, updated!.Status);
            Assert.True(updated.RetryCount >= 1);
            Assert.Contains("not found", updated.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            // Ensure metrics increment was recorded for missing source
            metricsMock.Verify(m => m.Increment("processing.source_missing", It.IsAny<double>()), Times.AtLeastOnce);

            // Cleanup created temp destination dir
            try { Directory.Delete(destRoot, true); }
            catch (IOException) { /* Best-effort cleanup for temp test directories. */ }
            catch (UnauthorizedAccessException) { /* Best-effort cleanup for temp test directories. */ }
        }

        [Fact]
        public async Task ProcessMoveOrCopy_DirectorySource_FinalizesOnceAfterCompanionFilesAreCopied()
        {
            var sourceDir = GetTempDirectory("dl-dir");
            var destRoot = GetTempDirectory("dl-dest");

            var audioPath = await GetFileAsync(sourceDir, "book.m4b");
            var coverPath = await GetFileAsync(sourceDir, "cover.jpg");
            var txtPath = await GetFileAsync(sourceDir, "book.txt");

            var download = new Download
            {
                Id = "dir-batch-test-1",
                Status = DownloadStatus.Completed,
                DownloadPath = sourceDir,
                FinalPath = sourceDir,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };

            var job = new DownloadProcessingJob
            {
                Id = "job-dir-batch-1",
                DownloadId = download.Id,
                JobType = ProcessingJobType.MoveOrCopyFile,
                Status = ProcessingJobStatus.Processing,
                SourcePath = sourceDir
            };

            var services = MockUtils.InitServiceCollection();

            var configMock = new Mock<IConfigurationService>();
            configMock.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(new ApplicationSettings
            {
                OutputPath = destRoot,
                CompletedFileAction = "Copy",
                EnableMetadataProcessing = false
            });
            services.AddSingleton<IConfigurationService>(configMock.Object);

            var downloadServiceMock = new Mock<IDownloadService>();
            services.AddSingleton<IDownloadService>(downloadServiceMock.Object);


            var provider = services.BuildServiceProvider();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            var svc = new DownloadProcessingBackgroundService(
                provider.GetRequiredService<IServiceScopeFactory>(), 
                provider.GetRequiredService<ILogger<DownloadProcessingBackgroundService>>(),
                provider.GetRequiredService<IAppMetricsService>());

            var downloadRepository = provider.GetRequiredService<IDownloadRepository>();

            await downloadRepository.AddAsync(download);
            
            string? processedPath = null;
            downloadServiceMock
                .Setup(d => d.ProcessCompletedDownloadAsync(download.Id, It.IsAny<string>()))
                .Callback<string, string>(async (id, path) =>
                {
                    processedPath = path;
                    var tracked = await downloadRepository.GetByIdAsync(id);
                    if (tracked != null)
                    {
                        tracked.Status = DownloadStatus.Moved;
                        tracked.FinalPath = path;
                        await downloadRepository.UpdateAsync(tracked);
                    }
                })
                .Returns(Task.CompletedTask);

            using var scope = provider.CreateScope();

            var method = typeof(DownloadProcessingBackgroundService).GetMethod("ProcessMoveOrCopyJobAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var expectedAudioDest = Path.Join(destRoot, "book.m4b");
            var expectedCoverDest = Path.Join(destRoot, "cover.jpg");
            var expectedTxtDest = Path.Join(destRoot, "book.txt");

            Assert.False(File.Exists(expectedAudioDest));
            Assert.False(File.Exists(expectedCoverDest));
            Assert.False(File.Exists(expectedTxtDest));
            Assert.Null(processedPath);
            Assert.Null(job.DestinationPath);

            var task = (Task)method!.Invoke(svc, [job, scope, CancellationToken.None])!;
            await task;

            Assert.True(File.Exists(expectedAudioDest));
            Assert.True(File.Exists(expectedCoverDest));
            Assert.True(File.Exists(expectedTxtDest));
            Assert.Equal(sourceDir, processedPath);
            Assert.Equal(destRoot, job.DestinationPath);

            downloadServiceMock.Verify(d => d.ProcessCompletedDownloadAsync(download.Id, sourceDir), Times.Once);
        }

        [Fact]
        [Trait("Method", "ProcessMoveOrCopyJobAsync")]
        public async Task ProcessMoveOrCopy_DirectorySource_UsesClientReportedFilesToExcludeUnrelatedFiles()
        {
            var sourceDir = GetTempDirectory($"dl-dir");
            var destRoot = GetTempDirectory($"dl-dest");

            var audioPath = await GetFileAsync(sourceDir, "book.m4b");
            var coverPath = await GetFileAsync(sourceDir, "cover.jpg");
            var txtPath = await GetFileAsync(sourceDir, "book.txt");
            var unrelatedPath = await GetFileAsync(sourceDir, "unrelated.txt");

            var download = new Download
            {
                Id = "dir-batch-client-scope-1",
                Status = DownloadStatus.Completed,
                DownloadPath = sourceDir,
                FinalPath = sourceDir,
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
                DownloadClientId = "client-1",
                Metadata = new Dictionary<string, object>
                {
                    ["TorrentHash"] = "ABC123"
                }
            };

            var job = new DownloadProcessingJob
            {
                Id = "job-dir-batch-client-scope-1",
                DownloadId = download.Id,
                JobType = ProcessingJobType.MoveOrCopyFile,
                Status = ProcessingJobStatus.Processing,
                SourcePath = sourceDir
            };

            var services = MockUtils.InitServiceCollection();

            var configMock = new Mock<IConfigurationService>();
            configMock.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(new ApplicationSettings
            {
                OutputPath = destRoot,
                CompletedFileAction = "Copy",
                EnableMetadataProcessing = false
            });
            services.AddSingleton<IConfigurationService>(configMock.Object);

            var importResolverMock = new Mock<IImportItemResolutionService>();
            importResolverMock
                .Setup(r => r.ResolveImportItemAsync(
                    It.Is<Download>(d => d.Id == download.Id),
                    It.IsAny<QueueItem>(),
                    It.IsAny<QueueItem?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Download download, QueueItem queueItem, QueueItem? previousAttempt, CancellationToken ct) =>
                {
                    queueItem.SourceFiles = [audioPath, coverPath, txtPath];
                    return queueItem;
                });
            services.AddSingleton<IImportItemResolutionService>(importResolverMock.Object);

            var downloadServiceMock = new Mock<IDownloadService>();
            services.AddSingleton<IDownloadService>(downloadServiceMock.Object);
            
            var provider = services.BuildServiceProvider();

            var svc = new DownloadProcessingBackgroundService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<ILogger<DownloadProcessingBackgroundService>>(),
                provider.GetRequiredService<IAppMetricsService>());

            var downloadRepository = provider.GetRequiredService<IDownloadRepository>();

            await downloadRepository.AddAsync(download);
            
            string? processedPath = null;
            downloadServiceMock
                .Setup(d => d.ProcessCompletedDownloadAsync(download.Id, It.IsAny<string>()))
                .Callback<string, string>(async (id, path) =>
                {
                    processedPath = path;
                    var tracked = await downloadRepository.GetByIdAsync(id);
                    if (tracked != null)
                    {
                        tracked.Status = DownloadStatus.Moved;
                        tracked.FinalPath = path;
                        await downloadRepository.UpdateAsync(tracked);
                    }
                })
                .Returns(Task.CompletedTask);

            using var scope = provider.CreateScope();
            var method = typeof(DownloadProcessingBackgroundService).GetMethod("ProcessMoveOrCopyJobAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var task = (Task)method!.Invoke(svc, [job, scope, CancellationToken.None])!;
            await task;

            Assert.True(File.Exists(Path.Join(destRoot, "book.m4b")));
            Assert.True(File.Exists(Path.Join(destRoot, "cover.jpg")));
            Assert.True(File.Exists(Path.Join(destRoot, "book.txt")));
            Assert.False(File.Exists(Path.Join(destRoot, "unrelated.txt")));
            Assert.Equal(sourceDir, processedPath);
        }
    }
}

