using System.Data.Common;
using Listenarr.Application.Search.Discovery;
using Listenarr.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// A SQLite file database built by the real migration chain, with indexers seeded, for the
/// discovery store tests. Pooling is off so the file can be removed with the test's temp folder.
/// </summary>
internal sealed class DiscoveryStoreTestDatabase
{
    private readonly string _connectionString;

    private DiscoveryStoreTestDatabase(string connectionString, IReadOnlyList<int> indexerIds)
    {
        _connectionString = connectionString;
        IndexerIds = indexerIds;
    }

    public IReadOnlyList<int> IndexerIds { get; }

    public static async Task<DiscoveryStoreTestDatabase> CreateAsync(string directory, int indexerCount = 1)
    {
        var path = Path.Join(directory, $"discovery-store-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path};Pooling=False";
        var indexers = Enumerable.Range(1, indexerCount)
            .Select(number => new Indexer
            {
                Name = $"Feed {number}",
                Type = "Torrent",
                Implementation = "Torznab",
                Url = $"https://feed-{number}.example"
            })
            .ToList();
        await using (var db = new ListenArrDbContext(BuildOptions(connectionString, [])))
        {
            await db.Database.MigrateAsync();
            db.Indexers.AddRange(indexers);
            await db.SaveChangesAsync();
        }

        return new DiscoveryStoreTestDatabase(
            connectionString,
            indexers.Select(indexer => indexer.Id).ToList());
    }

    public EfDiscoveryStore CreateStore(params IInterceptor[] interceptors) =>
        new(new OptionsDbContextFactory(BuildOptions(_connectionString, interceptors)));

    public ListenArrDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(BuildOptions(_connectionString, interceptors));

    public static IReadOnlyList<DiscoveredReleaseCandidate> Releases(int first, int count, DateTime newestPublishedUtc) =>
        Enumerable.Range(first, count)
            .Select(number => new DiscoveredReleaseCandidate(
                $"release-{number:D4}",
                newestPublishedUtc.AddMinutes(-(number - first)),
                $"Release {number:D4}",
                $"{{\"n\":{number}}}"))
            .ToList();

    private static DbContextOptions<ListenArrDbContext> BuildOptions(
        string connectionString,
        IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connectionString, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
            .AddInterceptors(interceptors)
            .Options;

    private sealed class OptionsDbContextFactory(DbContextOptions<ListenArrDbContext> options)
        : IDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext() => new(options);
    }
}

/// <summary>
/// Throws on the Nth write whose SQL names <c>tableMarker</c>, standing in for a storage
/// failure part-way through a poll's commit.
/// </summary>
internal sealed class FailingWriteInterceptor(string tableMarker, int failOnOccurrence) : DbCommandInterceptor
{
    private int _seen;

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains(tableMarker, StringComparison.Ordinal)
            && ++_seen == failOnOccurrence)
        {
            throw new InjectedStorageFailureException();
        }

        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}

internal sealed class InjectedStorageFailureException() : Exception("Injected storage failure.");

/// <summary>Records the text of every command a context sends.</summary>
internal sealed class CommandTextRecorder : DbCommandInterceptor
{
    public List<string> Commands { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}
