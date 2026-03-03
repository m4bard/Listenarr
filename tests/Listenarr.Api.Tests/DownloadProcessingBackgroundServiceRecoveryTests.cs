using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Api.Services;
using Listenarr.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    [Trait("Area", "ProcessingBackgroundRecovery")]
    public class DownloadProcessingBackgroundServiceRecoveryTests
    {
        [Fact]
        [Trait("Scenario", "StartupResetRequeuesStuckProcessingJobs")]
        public async Task ResetStuckJobsAsync_ProcessingJobs_AreResetToPending()
        {
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            using var db = new ListenArrDbContext(options);
            db.DownloadProcessingJobs.AddRange(
                new DownloadProcessingJob
                {
                    Id = "job-processing-1",
                    DownloadId = "dl-1",
                    JobType = ProcessingJobType.MoveOrCopyFile,
                    Status = ProcessingJobStatus.Processing,
                    SourcePath = "C:/tmp/a.mp3"
                },
                new DownloadProcessingJob
                {
                    Id = "job-pending-1",
                    DownloadId = "dl-2",
                    JobType = ProcessingJobType.MoveOrCopyFile,
                    Status = ProcessingJobStatus.Pending,
                    SourcePath = "C:/tmp/b.mp3"
                });
            await db.SaveChangesAsync();

            var services = new ServiceCollection();
            services.AddSingleton(db);
            services.AddScoped<IDownloadProcessingQueueService, DownloadProcessingQueueService>();
            services.AddLogging();

            var provider = services.BuildServiceProvider();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<DownloadProcessingBackgroundService>>();
            var metricsMock = new Mock<IAppMetricsService>();

            var service = new DownloadProcessingBackgroundService(scopeFactory, loggerMock.Object, metricsMock.Object);

            var method = typeof(DownloadProcessingBackgroundService)
                .GetMethod("ResetStuckJobsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var task = (Task?)method!.Invoke(service, new object[] { CancellationToken.None });
            Assert.NotNull(task);
            await task!;

            var processingJob = await db.DownloadProcessingJobs.FindAsync("job-processing-1");
            var pendingJob = await db.DownloadProcessingJobs.FindAsync("job-pending-1");

            Assert.NotNull(processingJob);
            Assert.Equal(ProcessingJobStatus.Pending, processingJob!.Status);
            Assert.Contains(processingJob.ProcessingLog, m => m.Contains("stuck Processing state", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(pendingJob);
            Assert.Equal(ProcessingJobStatus.Pending, pendingJob!.Status);
        }
    }
}
