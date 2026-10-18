using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

// Probe only (not for upstream): seeds journals whose ProtocolVersion is outside
// [1, Current], which is the input EnsureCurrentRecoveryProtocolAsync handles.
[Trait("Name", "FileRegistrationRecoveryUnsupportedProtocolProbeTests")]
[Trait("Category", "Infrastructure")]
public sealed class FileRegistrationRecoveryUnsupportedProtocolProbeTests : BaseTests
{
    private const string ProtocolReason =
        "This file publication uses an unsupported operation-evidence protocol. Its artifacts were preserved and only this operation requires repair.";

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Records { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Records.Add((logLevel, formatter(state, exception)));
    }

    private sealed record Seed(Guid Id, int Version, FileMutationJournalState State);

    private async Task<(List<Seed> Seeded, List<FileMutationJournal> After, CapturingLogger<FileRegistrationRecoveryService> Logger)> RunAsync(params int[] versions)
    {
        Init();
        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        var seeded = new List<Seed>();
        var states = new[] { FileMutationJournalState.Planned, FileMutationJournalState.TargetVerified, FileMutationJournalState.SourceDeleted };
        await using (var db = await factory.CreateDbContextAsync())
        {
            var created = DateTime.UtcNow.AddMinutes(-30);
            for (var i = 0; i < versions.Length; i++)
            {
                var seed = new Seed(Guid.NewGuid(), versions[i], states[i % states.Length]);
                seeded.Add(seed);
                db.FileMutationJournals.Add(new FileMutationJournal
                {
                    OperationId = seed.Id, Action = FileAction.HardlinkCopy, State = seed.State,
                    ProtocolVersion = seed.Version,
                    SourcePath = $"/incoming/book-{i}.m4b", DestinationPath = $"/library/book-{i}.m4b",
                    CreatedAt = created.AddSeconds(i), UpdatedAt = created.AddSeconds(i)
                });
            }
            await db.SaveChangesAsync();
        }
        var logger = new CapturingLogger<FileRegistrationRecoveryService>();
        var service = new FileRegistrationRecoveryService(factory, Mock.Of<IFileMover>(), TimeProvider.System, logger);
        await service.ReconcileAsync();
        await using var check = await factory.CreateDbContextAsync();
        var after = await check.FileMutationJournals.AsNoTracking().ToListAsync();
        return (seeded, after, logger);
    }

    // Apparatus control: the rows really went through the protocol path (its fixed reason), and the logger captures.
    [Fact]
    public async Task Control_UnsupportedVersions_ParkedByProtocolPath_AndLoggerCaptures()
    {
        var (_, after, logger) = await RunAsync(0, 0, FileMutationProtocol.Current + 1);
        Assert.All(after, row => Assert.Equal(FileMutationJournalState.NeedsAttention, row.State));
        Assert.All(after, row => Assert.Equal(ProtocolReason, row.Error));
        Assert.Contains(logger.Records, record => record.Message.Contains("3 need attention"));
    }

    // Comparison: a supported-but-older version (2) does not reach the protocol path.
    [Fact]
    public async Task Comparison_SupportedOlderVersion_IsNotHandledByProtocolPath()
    {
        var (_, after, _) = await RunAsync(FileMutationProtocol.ParentGenerationMarkerlessDatabaseState);
        Assert.NotEqual(ProtocolReason, Assert.Single(after).Error);
    }

    [Fact]
    public async Task Gap_LogNamesEveryParkedOperation()
    {
        var (seeded, _, logger) = await RunAsync(0, 0, FileMutationProtocol.Current + 1);
        var log = string.Join("\n", logger.Records.Select(r => r.Message));
        Assert.All(seeded, seed => Assert.Contains(seed.Id.ToString(), log, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Gap_StateReachedSurvivesInLogOrError()
    {
        var (seeded, after, logger) = await RunAsync(0, 0, FileMutationProtocol.Current + 1);
        var visible = string.Join("\n", logger.Records.Select(r => r.Message).Concat(after.Select(r => r.Error ?? "")));
        Assert.All(seeded, seed => Assert.Contains(seed.State.ToString(), visible));
    }

    [Fact]
    public async Task Gap_ParkingIsLoggedAboveInformation()
    {
        var (_, _, logger) = await RunAsync(0);
        Assert.Contains(logger.Records, record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Diagnostic_Dump()
    {
        var (seeded, after, logger) = await RunAsync(0, FileMutationProtocol.Current + 1, FileMutationProtocol.ParentGenerationMarkerlessDatabaseState);
        Assert.Fail("SEEDED: " + string.Join(" | ", seeded.Select(s => $"v{s.Version} {s.State}"))
            + " LOG: " + string.Join(" || ", logger.Records.Select(r => $"{r.Level}: {r.Message}"))
            + " ROWS: " + string.Join(" || ", after.OrderBy(r => r.CreatedAt).Select(r => $"v{r.ProtocolVersion} {r.State} '{r.Error}'")));
    }
}
