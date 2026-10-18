using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

[Trait("Name", "FileRegistrationRecoveryProtocolPortedTests")]
[Trait("Category", "Infrastructure")]
public sealed class FileRegistrationRecoveryProtocolPortedTests : BaseTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Records { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Records.Add((logLevel, formatter(state, exception)));
    }

    private async Task<(IDbContextFactory<ListenArrDbContext> Factory, List<(Guid Id, FileMutationJournalState State)> Seeded, CapturingLogger<FileRegistrationRecoveryService> Logger)> RunAsync(int count)
    {
        Init();
        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        var seeded = new List<(Guid, FileMutationJournalState)>();
        await using (var seed = await factory.CreateDbContextAsync())
        {
            var created = DateTime.UtcNow.AddMinutes(-30);
            for (var i = 0; i < count; i++)
            {
                var id = Guid.NewGuid();
                var state = i % 2 == 0 ? FileMutationJournalState.Planned : FileMutationJournalState.RegistrationCommitted;
                seeded.Add((id, state));
                seed.FileMutationJournals.Add(new FileMutationJournal
                {
                    OperationId = id, Action = FileAction.HardlinkCopy, State = state,
                    ProtocolVersion = FileMutationProtocol.Current - 1,
                    SourcePath = $"/incoming/book-{i}.m4b", DestinationPath = $"/library/book-{i}.m4b",
                    CreatedAt = created.AddSeconds(i), UpdatedAt = created.AddSeconds(i)
                });
            }
            await seed.SaveChangesAsync();
        }
        var logger = new CapturingLogger<FileRegistrationRecoveryService>();
        var service = new FileRegistrationRecoveryService(factory, Mock.Of<IFileMover>(), TimeProvider.System, logger);
        await service.ReconcileAsync();
        return (factory, seeded, logger);
    }

    // Control for the apparatus: new contract, nothing throws, rows are parked.
    [Fact]
    public async Task Control_DoesNotThrow_AllRowsParkedNeedsAttention()
    {
        var (factory, seeded, _) = await RunAsync(3);
        await using var check = await factory.CreateDbContextAsync();
        var states = await check.FileMutationJournals.AsNoTracking().Select(j => j.State).ToListAsync();
        Assert.Equal(3, states.Count);
        Assert.All(states, s => Assert.Equal(FileMutationJournalState.NeedsAttention, s));
    }

    // Ported from NamesEveryOneAndHowMany: the operator-facing output (log) names every id and the count.
    [Fact]
    public async Task Ported_LogNamesEveryJournalAndCount()
    {
        var (_, seeded, logger) = await RunAsync(3);
        var all = string.Join("\n", logger.Records.Select(r => r.Message));
        Assert.Contains("3", all);
        foreach (var (id, _) in seeded)
        {
            Assert.Contains(id.ToString(), all, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Ported from NamesTheStateTheMutationReached: the state reached is recoverable from some operator-visible place
    // (log text or the row's Error), since the row State is overwritten.
    [Fact]
    public async Task Ported_StateReachedIsRecoverable()
    {
        var (factory, seeded, logger) = await RunAsync(2);
        await using var check = await factory.CreateDbContextAsync();
        var errors = await check.FileMutationJournals.AsNoTracking().Select(j => j.Error).ToListAsync();
        var visible = string.Join("\n", logger.Records.Select(r => r.Message).Concat(errors.Select(e => e ?? "")));
        Assert.Contains("Planned", visible);
        Assert.Contains("RegistrationCommitted", visible);
    }

    // Ported from the cap tests: 13 journals, whole set reachable from the log.
    [Fact]
    public async Task Ported_ThirteenJournals_WholeSetInLog()
    {
        var (_, seeded, logger) = await RunAsync(13);
        var all = string.Join("\n", logger.Records.Select(r => r.Message));
        foreach (var (id, _) in seeded)
        {
            Assert.Contains(id.ToString(), all, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Diagnostic (not a port): what is logged at all, written to the test output as a failure message.
    [Fact]
    public async Task Diagnostic_DumpLogAndErrors()
    {
        var (factory, _, logger) = await RunAsync(2);
        await using var check = await factory.CreateDbContextAsync();
        var errors = await check.FileMutationJournals.AsNoTracking().Select(j => j.Error).ToListAsync();
        Assert.Fail("LOG: " + string.Join(" || ", logger.Records.Select(r => $"{r.Level}: {r.Message}")) + " ERRORS: " + string.Join(" || ", errors));
    }
}
