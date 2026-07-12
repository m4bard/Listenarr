using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Scanning;

public sealed class MoveScanHandoffRecoveryServiceTests : BaseTests
{
    [Fact]
    public async Task RecoverAsync_PendingMoveScanHandoff_EnqueuesScan()
    {
        var audiobook = await _audiobookRepository.AddAsync(new Audiobook
        {
            Title = "Recover Pending Move Scan",
            BasePath = FileService.GetTempDirectory("move-scan-handoff-pending")
        });
        const string correlationId = "move:pending-scan-handoff";
        await _historyRepository.AddAsync(new History
        {
            AudiobookId = audiobook.Id,
            AudiobookTitle = audiobook.Title,
            EventType = HistoryEvents.ScanQueued,
            Outcome = HistoryOutcome.Requested,
            Source = "Move",
            Message = "Post-move library scan requested",
            CorrelationId = correlationId
        });
        var scanQueue = new Mock<IScanQueueService>();
        scanQueue.Setup(service => service.EnqueueRecoveredScanAsync(
                It.IsAny<Audiobook>(),
                correlationId,
                It.IsAny<Func<Task<bool>>>()))
            .ReturnsAsync(Guid.NewGuid());
        var service = new MoveScanHandoffRecoveryService(
            scanQueue.Object,
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MoveScanHandoffRecoveryService>.Instance);

        await service.RecoverAsync(CancellationToken.None);

        scanQueue.Verify(service => service.EnqueueRecoveredScanAsync(
            It.Is<Audiobook>(candidate => candidate.Id == audiobook.Id),
            correlationId,
            It.IsAny<Func<Task<bool>>>()), Times.Once);
    }

    [Fact]
    public async Task RecoverAsync_HandoffWithoutAudiobook_RecordsTerminalFailure()
    {
        const string correlationId = "move:missing-audiobook-handoff";
        await _historyRepository.AddAsync(new History
        {
            AudiobookId = null,
            AudiobookTitle = "Deleted Audiobook",
            EventType = HistoryEvents.ScanQueued,
            Outcome = HistoryOutcome.Requested,
            Source = "Move",
            Message = "Post-move library scan requested",
            CorrelationId = correlationId
        });
        var scanQueue = new Mock<IScanQueueService>(MockBehavior.Strict);
        var service = new MoveScanHandoffRecoveryService(
            scanQueue.Object,
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MoveScanHandoffRecoveryService>.Instance);

        await service.RecoverAsync(CancellationToken.None);

        scanQueue.VerifyNoOtherCalls();
        var correlated = await _historyRepository.GetByCorrelationIdAsync(correlationId);
        Assert.Single(correlated, entry =>
            entry.EventType == HistoryEvents.ScanFailed
            && entry.Outcome == HistoryOutcome.Failed);
    }

    [Theory]
    [InlineData(HistoryEvents.ScanCompleted, HistoryOutcome.Succeeded)]
    [InlineData(HistoryEvents.ScanFailed, HistoryOutcome.Failed)]
    public async Task RecoverAsync_TerminalMoveScanHandoff_DoesNotEnqueue(
        string terminalEventType,
        HistoryOutcome terminalOutcome)
    {
        var audiobook = await _audiobookRepository.AddAsync(new Audiobook
        {
            Title = "Terminal Move Scan",
            BasePath = FileService.GetTempDirectory("move-scan-handoff-terminal")
        });
        var correlationId = $"move:terminal-{terminalEventType}";
        await _historyRepository.AddAsync(new History
        {
            AudiobookId = audiobook.Id,
            AudiobookTitle = audiobook.Title,
            EventType = HistoryEvents.ScanQueued,
            Outcome = HistoryOutcome.Requested,
            Source = "Move",
            CorrelationId = correlationId
        });
        await _historyRepository.AddAsync(new History
        {
            AudiobookId = audiobook.Id,
            AudiobookTitle = audiobook.Title,
            EventType = terminalEventType,
            Outcome = terminalOutcome,
            Source = "LibraryScan",
            CorrelationId = correlationId
        });
        var scanQueue = new Mock<IScanQueueService>(MockBehavior.Strict);
        var service = new MoveScanHandoffRecoveryService(
            scanQueue.Object,
            _provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MoveScanHandoffRecoveryService>.Instance);

        await service.RecoverAsync(CancellationToken.None);

        scanQueue.VerifyNoOtherCalls();
    }
}
