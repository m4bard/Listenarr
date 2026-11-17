using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Shared fixtures for the operator-review tests: a real SQLite database, real files and
/// a journal row shaped like the parked registration publications the review releases.
/// </summary>
public static class FileRegistrationOperatorReviewHarness
{
    public const string SourceContent = "complete original";
    public const string ParkedError = "The committed file-registration destination no longer matches the published content proof.";

    public sealed class Factory(DbContextOptions<ListenArrDbContext> options) : IDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext() => new(options);
    }

    public sealed record Seeded(
        Factory Factory,
        DbContextOptions<ListenArrDbContext> Options,
        string Root,
        Guid OperationId,
        int AudiobookId,
        string Source,
        string Destination);

    public static DbContextOptions<ListenArrDbContext> SqliteOptions(string root, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={Path.Join(root, "review.db")};Pooling=False");
        if (interceptors.Length > 0) builder.AddInterceptors(interceptors);
        return builder.Options;
    }

    public static FileRegistrationRecoveryService CreateService(
        IDbContextFactory<ListenArrDbContext> factory, ILogger<FileRegistrationRecoveryService>? logger = null) =>
        new(factory, new FileMover(NullLogger<FileMover>.Instance, dbContextFactory: factory),
            TimeProvider.System, logger ?? NullLogger<FileRegistrationRecoveryService>.Instance);

    /// <summary>
    /// Seeds a parked HardlinkCopy publication whose retained source is present.
    /// The destination is a different file, absent, or absent along with its parent directory.
    /// </summary>
    public static async Task<Seeded> SeedAsync(string root, DestinationShape destination = DestinationShape.Partial,
        FileMutationJournalState state = FileMutationJournalState.NeedsAttention,
        Action<FileMutationJournal>? configure = null, bool seedJournal = true,
        DbContextOptions<ListenArrDbContext>? options = null)
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Join(root, "source")).FullName;
        var destinationDirectory = Path.Join(root, "destination");
        var source = Path.Join(sourceDirectory, "book.m4b");
        var target = Path.Join(destinationDirectory, "book.m4b");
        await File.WriteAllTextAsync(source, SourceContent);
        if (destination != DestinationShape.MissingParent)
        {
            Directory.CreateDirectory(destinationDirectory);
        }
        if (destination == DestinationShape.Partial)
        {
            await File.WriteAllTextAsync(target, "partial");
        }
        else if (destination == DestinationShape.Matching)
        {
            await File.WriteAllTextAsync(target, SourceContent);
        }

        options ??= SqliteOptions(root);
        var factory = new Factory(options);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var operationId = Guid.NewGuid();
        if (seedJournal)
        {
            var journal = new FileMutationJournal
            {
                OperationId = operationId,
                ProtocolVersion = FileMutationProtocol.ParentGenerationMarkerlessDatabaseState,
                Action = FileAction.HardlinkCopy,
                SourcePath = source,
                DestinationPath = target,
                SourceLength = SourceContent.Length,
                SourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SourceContent))),
                AudiobookId = 865,
                State = state,
                Error = state == FileMutationJournalState.NeedsAttention ? ParkedError : null,
                UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
            };
            configure?.Invoke(journal);
            db.FileMutationJournals.Add(journal);
            await db.SaveChangesAsync();
        }
        return new Seeded(factory, options, root, operationId, 865, source, target);
    }

    public enum DestinationShape { Partial, Matching, MissingFile, MissingParent }

    public static async Task<FileMutationJournal> LoadJournalAsync(Seeded seeded)
    {
        await using var db = seeded.Factory.CreateDbContext();
        return await db.FileMutationJournals.AsNoTracking().SingleAsync(j => j.OperationId == seeded.OperationId);
    }

    /// <summary>Serialises every row of the named tables, or of every other table, as one comparable string.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> SnapshotAsync(Seeded seeded, bool journalsOnly,
        Guid? exceptOperationId = null)
    {
        await using var db = seeded.Factory.CreateDbContext();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = journalsOnly
                ? "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'FileMutationJournals'"
                : "SELECT name FROM sqlite_master WHERE type = 'table' AND name <> 'FileMutationJournals' ORDER BY name";
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            await using var select = connection.CreateCommand();
            select.CommandText = exceptOperationId == null
                ? $"SELECT * FROM \"{table}\""
                : $"SELECT * FROM \"{table}\" WHERE OperationId <> $operationId";
            if (exceptOperationId != null)
            {
                var parameter = select.CreateParameter();
                parameter.ParameterName = "$operationId";
                parameter.Value = exceptOperationId.Value.ToString().ToUpperInvariant();
                select.Parameters.Add(parameter);
            }
            snapshot[table] = await ReadRowsAsync(select);
        }
        return snapshot;
    }

    private static async Task<string> ReadRowsAsync(DbCommand select)
    {
        await using var reader = await select.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Add(string.Join("|", values.Select(value =>
                Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))));
        }
        rows.Sort(StringComparer.Ordinal);
        return string.Join("\n", rows);
    }

    public static async Task<string> HashTreeAsync(string root)
    {
        var entries = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".db", StringComparison.Ordinal) && !path.Contains(".db-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path);
            entries.Add(Directory.Exists(path)
                ? $"{relative}/"
                : $"{relative}:{new FileInfo(path).Length}:" + Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))));
        }
        return string.Join("\n", entries);
    }

    /// <summary>
    /// Runs one write against the same database on a second connection immediately before
    /// the first UPDATE on FileMutationJournals, the point a concurrent writer would land.
    /// </summary>
    public sealed class ConcurrentWriteBeforeJournalUpdate(
        DbContextOptions<ListenArrDbContext> plain,
        Func<ListenArrDbContext, CancellationToken, Task> write) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("FileMutationJournals"))
            {
                Fired = true;
                await using var other = new ListenArrDbContext(plain);
                await write(other, cancellationToken);
            }
            return result;
        }
    }
}
