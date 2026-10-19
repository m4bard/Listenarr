using System.Data.Common;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

// Review probe only, not for upstream.
[Trait("Area", "Library")]
[Trait("Name", "ParkedLoggingReviewProbeTests")]
[Trait("Category", "Infrastructure")]
public sealed class ParkedLoggingReviewProbeTests : BaseTests
{
    // Mode: "none" = interceptor never parks (control), "one" = a concurrent actor parks the first
    // selected row just before this call's UPDATE, "all" = concurrent actor parks every selected row.
    [Theory]
    [InlineData("none")]
    [InlineData("one")]
    [InlineData("all")]
    public async Task Probe_ProtocolPass_ConcurrentPartialParking(string mode)
    {
        var root = FileService.GetTempDirectory("probe-parked-" + mode);
        var dbPath = Path.Join(root, "probe.db");
        var plain = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={dbPath};Pooling=False").Options;
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var concurrentlyParked = mode switch
        {
            "one" => new[] { ids[0] },
            "all" => ids,
            _ => Array.Empty<Guid>()
        };
        var interceptor = new ParkBeforeUpdateInterceptor(plain, concurrentlyParked);
        var intercepted = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={dbPath};Pooling=False")
            .AddInterceptors(interceptor).Options;

        await using (var db = new ListenArrDbContext(plain))
        {
            await db.Database.EnsureCreatedAsync();
            var created = DateTime.UtcNow;
            foreach (var (id, index) in ids.Select((id, index) => (id, index)))
            {
                db.FileMutationJournals.Add(new FileMutationJournal
                {
                    OperationId = id,
                    ProtocolVersion = FileMutationProtocol.Current + 1,
                    Action = FileAction.Move,
                    SourcePath = Path.Join(FileService.GetTempPath(), $"p-{id:N}-s.m4b"),
                    DestinationPath = Path.Join(FileService.GetTempPath(), $"p-{id:N}-t.m4b"),
                    SourcePhysicalObjectIdentity = "s",
                    TargetPhysicalObjectIdentity = "t",
                    SourceLength = 1,
                    State = FileMutationJournalState.Planned,
                    CreatedAt = created.AddSeconds(index)
                });
            }
            await db.SaveChangesAsync();
        }

        var logger = new CapturingLogger();
        var factory = new Factory(intercepted);
        await new FileRegistrationRecoveryService(
            factory,
            new FileMover(NullLogger<FileMover>.Instance, dbContextFactory: factory, timeProvider: TimeProvider.System),
            TimeProvider.System,
            logger).ReconcileAsync();
        // Second pass: already-parked rows must not warn again.
        var warningsAfterFirstPass = logger.Warnings.Count;
        await new FileRegistrationRecoveryService(
            factory,
            new FileMover(NullLogger<FileMover>.Instance, dbContextFactory: factory, timeProvider: TimeProvider.System),
            TimeProvider.System,
            logger).ReconcileAsync();
        Console.WriteLine($"PROBE[{mode}] warningsAfterFirstPass={warningsAfterFirstPass} afterSecondPass={logger.Warnings.Count}");

        await using var verify = new ListenArrDbContext(plain);
        var rows = await verify.FileMutationJournals.AsNoTracking().ToListAsync();
        foreach (var row in rows)
        {
            Console.WriteLine($"PROBE[{mode}] row {row.OperationId} v={row.ProtocolVersion} state={row.State} error={row.Error} concurrentlyParked={concurrentlyParked.Contains(row.OperationId)}");
        }
        Console.WriteLine($"PROBE[{mode}] interceptorFired={interceptor.Fired}");
        foreach (var warning in logger.Warnings)
        {
            Console.WriteLine($"PROBE[{mode}] WARN {warning}");
        }

        Assert.All(rows, row => Assert.Equal(FileMutationJournalState.NeedsAttention, row.State));
        // Every row this call itself parked must be named in a Warning by this call.
        foreach (var id in ids.Except(concurrentlyParked))
        {
            Assert.Single(logger.Warnings, warning => warning.Contains(id.ToString()));
        }
    }

    // Does a seeded ProtocolVersion of 0 survive insertion on each provider?
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Probe_SeededVersionZero_PersistedVersion(bool relational)
    {
        var root = FileService.GetTempDirectory("probe-v0");
        var options = relational
            ? new DbContextOptionsBuilder<ListenArrDbContext>().UseSqlite($"Data Source={Path.Join(root, "v0.db")};Pooling=False").Options
            : new DbContextOptionsBuilder<ListenArrDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var id = Guid.NewGuid();
        await using (var db = new ListenArrDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.FileMutationJournals.Add(new FileMutationJournal
            {
                OperationId = id, ProtocolVersion = 0, Action = FileAction.Move,
                SourcePath = Path.Join(FileService.GetTempPath(), "v0-s.m4b"),
                DestinationPath = Path.Join(FileService.GetTempPath(), "v0-t.m4b"),
                SourcePhysicalObjectIdentity = "s", TargetPhysicalObjectIdentity = "t", SourceLength = 1,
                State = FileMutationJournalState.Planned
            });
            await db.SaveChangesAsync();
        }
        var factory = new Factory(options);
        await new FileRegistrationRecoveryService(factory,
            new FileMover(NullLogger<FileMover>.Instance, dbContextFactory: factory, timeProvider: TimeProvider.System),
            TimeProvider.System, NullLogger<FileRegistrationRecoveryService>.Instance).ReconcileAsync();
        await using var verify = new ListenArrDbContext(options);
        var row = await verify.FileMutationJournals.AsNoTracking().SingleAsync();
        Console.WriteLine($"PROBEV0[relational={relational}] persistedVersion={row.ProtocolVersion} state={row.State} error={row.Error}");
    }

    private sealed class ParkBeforeUpdateInterceptor(
        DbContextOptions<ListenArrDbContext> plain,
        Guid[] toPark) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired && toPark.Length > 0
                && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("FileMutationJournals"))
            {
                Fired = true;
                await using var other = new ListenArrDbContext(plain);
                await other.FileMutationJournals
                    .Where(journal => toPark.Contains(journal.OperationId))
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(journal => journal.State, FileMutationJournalState.NeedsAttention),
                        cancellationToken);
            }
            return result;
        }
    }

    private sealed class CapturingLogger : ILogger<FileRegistrationRecoveryService>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<string> Warnings =>
            _entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToList();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class Factory(DbContextOptions<ListenArrDbContext> options) : IDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext() => new(options);

        public Task<ListenArrDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ListenArrDbContext(options));
    }
}
