using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// SQLite has no datetime type, so a DateTime is stored as text with no zone marker and
/// materialises with Kind=Unspecified whatever Kind it was written with.
/// AudiobookFile.ApplyPhysicalObjectIdentity requires Kind=Utc, and both
/// AudiobookFileService.ClonePhysicalGeneration and
/// EfAudiobookFileRepository.ApplyPhysicalGeneration hand a loaded
/// PhysicalIdentityObservedAtUtc straight back to it.
///
/// These run against real SQLite on purpose. The shared test harness uses the in-memory
/// provider, which hands back the same CLR instance and so preserves Kind for free, which
/// is why the tests that already drive RefreshPhysicalGenerationAsync never see this.
/// </summary>
[Trait("Name", "PersistedPhysicalIdentityObservedAtKindTests")]
[Trait("Category", "Infrastructure")]
public sealed class PersistedPhysicalIdentityObservedAtKindTests : BaseTests
{
    private const string Identity = "generation-one";

    [Fact]
    public async Task ObservedAtUtc_WrittenThroughEfAndLoadedBack_KeepsUtcKind()
    {
        await using var connection = await OpenDatabaseAsync();
        var options = CreateOptions(connection);
        var written = await SeedFileAsync(options);

        await using var read = new ListenArrDbContext(options);
        var loaded = await read.AudiobookFiles.AsNoTracking().SingleAsync();

        Assert.Equal(DateTimeKind.Utc, loaded.PhysicalIdentityObservedAtUtc!.Value.Kind);
        Assert.Equal(written, loaded.PhysicalIdentityObservedAtUtc!.Value);
    }

    [Fact]
    public async Task ObservedAtUtc_LoadedFromTextWithoutAZoneMarker_KeepsUtcKind()
    {
        // A row written before this fix, or by anything else that stores the column the
        // way the provider does. No backfill should be needed to read it correctly.
        await using var connection = await OpenDatabaseAsync();
        var options = CreateOptions(connection);
        await SeedFileAsync(options);
        await OverwriteObservedAtTextAsync(connection, "2026-08-24 13:41:27.4246796");

        await using var read = new ListenArrDbContext(options);
        var loaded = await read.AudiobookFiles.AsNoTracking().SingleAsync();

        Assert.Equal(DateTimeKind.Utc, loaded.PhysicalIdentityObservedAtUtc!.Value.Kind);
        Assert.Equal(
            new DateTime(2026, 8, 24, 13, 41, 27, DateTimeKind.Utc).AddTicks(4246796),
            loaded.PhysicalIdentityObservedAtUtc!.Value);
    }

    [Fact]
    public async Task CarryingIdentityForwardFromALoadedFile_DoesNotThrow()
    {
        // The shape reported in #911: the scanner recognises a tracked file at a new path,
        // and AudiobookFileService clones the loaded row to keep a rollback predecessor.
        await using var connection = await OpenDatabaseAsync();
        var options = CreateOptions(connection);
        await SeedFileAsync(options);

        await using var read = new ListenArrDbContext(options);
        var loaded = await read.AudiobookFiles.AsNoTracking().SingleAsync();
        var clone = AudiobookFile.CreateUnresolved(loaded.Path);

        clone.ApplyPhysicalObjectIdentity(
            loaded.PhysicalObjectIdentity!,
            loaded.PhysicalIdentityObservedAtUtc!.Value);

        Assert.Equal(Identity, clone.PhysicalObjectIdentity);
        Assert.Equal(
            loaded.PhysicalIdentityObservedAtUtc,
            clone.PhysicalIdentityObservedAtUtc);
    }

    [Fact]
    public async Task StoredTextStillCarriesNoZoneMarker()
    {
        // Control. The conversion is on the read side only, so the stored format is
        // untouched and no migration is involved. It also shows the three tests above
        // do not pass because the written text changed shape.
        await using var connection = await OpenDatabaseAsync();
        await SeedFileAsync(CreateOptions(connection));

        var stored = await ReadObservedAtTextAsync(connection);

        Assert.DoesNotContain("Z", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("+", stored, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyPhysicalObjectIdentity_StillRejectsALocalTimestamp()
    {
        // Control. Reading a persisted timestamp back as Utc must not come from relaxing
        // the domain guard. A Local timestamp names a different instant depending on the
        // host, so it must keep throwing.
        var file = AudiobookFile.CreateUnresolved(Path.Join("library", "Book.m4b"));

        var exception = Assert.Throws<ArgumentException>(() =>
            file.ApplyPhysicalObjectIdentity(
                Identity,
                DateTime.SpecifyKind(
                    new DateTime(2026, 8, 24, 13, 41, 27),
                    DateTimeKind.Local)));

        Assert.Equal("observedAtUtc", exception.ParamName);
    }

    private static async Task<DateTime> SeedFileAsync(
        DbContextOptions<ListenArrDbContext> options)
    {
        var boundary = Path.GetFullPath(Path.Join("library", "ObservedAtKind"));
        var filePath = Path.Join(boundary, "Book.m4b");
        await using var context = new ListenArrDbContext(options);
        context.Audiobooks.Add(
            new Audiobook { Id = 1, Title = "Book", BasePath = boundary });
        var file = AudiobookFile.CreateUnresolved(filePath);
        file.AudiobookId = 1;
        file.ApplyPathIdentity(
            filePath,
            AudiobookFilePathIdentity.CreateValid(
                filePath,
                FileSystemPathSemantics.CurrentHostDefault,
                FileSystemCaseSensitivityMode.Auto,
                boundary));
        var observedAt = DateTime.UtcNow;
        file.ApplyPhysicalObjectIdentity(Identity, observedAt);
        context.AudiobookFiles.Add(file);
        await context.SaveChangesAsync();
        return observedAt;
    }

    private static async Task<string> ReadObservedAtTextAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT PhysicalIdentityObservedAtUtc FROM AudiobookFiles";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task OverwriteObservedAtTextAsync(
        SqliteConnection connection,
        string text)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE AudiobookFiles SET PhysicalIdentityObservedAtUtc = $value";
        command.Parameters.AddWithValue("$value", text);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<SqliteConnection> OpenDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ListenArrDbContext(CreateOptions(connection));
        await context.Database.EnsureCreatedAsync();
        return connection;
    }

    private static DbContextOptions<ListenArrDbContext> CreateOptions(
        SqliteConnection connection) =>
        new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(connection)
            .Options;
}
