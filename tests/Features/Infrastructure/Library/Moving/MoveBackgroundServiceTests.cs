using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Moving;

public sealed class MoveBackgroundServiceTests
{
    [Fact]
    public async Task HeartbeatLeaseLoss_CancelsInFlightProcessing()
    {
        var jobs = Channel.CreateUnbounded<MoveJob>();
        var job = new MoveJob { Id = Guid.NewGuid(), AudiobookId = 42 };
        await jobs.Writer.WriteAsync(job);
        var processingCanceled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new Mock<IMoveQueueService>();
        queue.SetupGet(service => service.Reader).Returns(jobs.Reader);
        queue.Setup(service => service.RecoverActiveJobsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        queue.Setup(service => service.TryClaimJobAsync(
                job.Id,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        queue.Setup(service => service.HeartbeatJobAsync(
                job.Id,
                It.IsAny<string>(),
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var processor = new Mock<IMoveJobProcessor>();
        processor.Setup(service => service.ProcessJobAsync(job, It.IsAny<CancellationToken>()))
            .Returns(async (MoveJob _, CancellationToken cancellationToken) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    processingCanceled.TrySetResult();
                    throw;
                }
            });
        var worker = new MoveBackgroundService(
            queue.Object,
            processor.Object,
            NullLogger<MoveBackgroundService>.Instance,
            heartbeatInterval: TimeSpan.FromMilliseconds(10));

        await worker.StartAsync(CancellationToken.None);
        await processingCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(1, job.LeaseGeneration);
        queue.Verify(service => service.HeartbeatJobAsync(
            job.Id,
            It.IsAny<string>(),
            1,
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
