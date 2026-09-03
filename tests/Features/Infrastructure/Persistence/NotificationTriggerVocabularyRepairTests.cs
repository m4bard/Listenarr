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
using Listenarr.Infrastructure.DependencyInjection;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// The stored half of the vocabulary change, exercised through the startup path that actually
/// runs it. Every database here is a throwaway in-memory SQLite connection created by the test.
/// </summary>
[Trait("Name", "NotificationTriggerVocabularyRepairTests")]
[Trait("Category", "Persistence")]
public sealed class NotificationTriggerVocabularyRepairTests : BaseTests
{
    [Fact]
    public void Startup_RewritesLegacyTriggerNamesInBothStoredColumns()
    {
        using var connection = OpenDatabase();
        SeedSettings(
            connection,
            enabledTriggers: "book-added|book-downloading|book-available|book-completed",
            webhooksJson: """
                [{"Id":"row-1","Name":"Configured","Url":"https://listenarr.example.com/hook",
                  "Type":"Discord","Triggers":["book-added","book-available"],"IsEnabled":true}]
                """);

        RunStartup(connection);

        var settings = ReadSettings(connection);
        Assert.Equal(
            [
                NotificationTriggers.Added,
                NotificationTriggers.Downloading,
                NotificationTriggers.Available,
                NotificationTriggers.Completed
            ],
            settings.EnabledNotificationTriggers);

        var webhook = Assert.Single(settings.Webhooks!);
        Assert.Equal(
            [NotificationTriggers.Added, NotificationTriggers.Available],
            webhook.Triggers);
        Assert.Equal("row-1", webhook.Id);
        Assert.Equal("Configured", webhook.Name);
        Assert.True(webhook.IsEnabled);
    }

    [Fact]
    public void Startup_LeavesUnrecognisedAndAlreadyCanonicalValuesUntouched()
    {
        using var connection = OpenDatabase();
        SeedSettings(
            connection,
            enabledTriggers: "Added|something-a-user-typed|BOOK-ADDED",
            webhooksJson: """
                [{"Id":"row-1","Name":"Mixed","Url":"https://listenarr.example.com/hook",
                  "Type":"Slack","Triggers":["Imported","book-available","who-knows"],"IsEnabled":true}]
                """);

        RunStartup(connection);

        var settings = ReadSettings(connection);
        Assert.Equal(
            [NotificationTriggers.Added, "something-a-user-typed", "BOOK-ADDED"],
            settings.EnabledNotificationTriggers);
        Assert.Equal(
            [NotificationTriggers.Imported, NotificationTriggers.Available, "who-knows"],
            Assert.Single(settings.Webhooks!).Triggers);
    }

    [Fact]
    public void Startup_IsIdempotentOverAlreadyMigratedData()
    {
        using var connection = OpenDatabase();
        SeedSettings(
            connection,
            enabledTriggers: "book-added|book-completed",
            webhooksJson: """
                [{"Id":"row-1","Name":"Configured","Url":"https://listenarr.example.com/hook",
                  "Type":"Discord","Triggers":["book-downloading","kept-verbatim"],"IsEnabled":true}]
                """);

        RunStartup(connection);
        var afterFirstRun = ReadSettings(connection);

        RunStartup(connection);
        RunStartup(connection);
        var afterThirdRun = ReadSettings(connection);

        Assert.Equal(
            afterFirstRun.EnabledNotificationTriggers,
            afterThirdRun.EnabledNotificationTriggers);
        Assert.Equal(
            Assert.Single(afterFirstRun.Webhooks!).Triggers,
            Assert.Single(afterThirdRun.Webhooks!).Triggers);
        Assert.Equal(
            [NotificationTriggers.Added, NotificationTriggers.Completed],
            afterThirdRun.EnabledNotificationTriggers);
        Assert.Equal(
            [NotificationTriggers.Downloading, "kept-verbatim"],
            Assert.Single(afterThirdRun.Webhooks!).Triggers);
    }

    [Fact]
    public void Startup_CollapsesADuplicateThatTheRenameIntroduces()
    {
        using var connection = OpenDatabase();
        SeedSettings(
            connection,
            enabledTriggers: "book-added|Added",
            webhooksJson: null);

        RunStartup(connection);

        Assert.Equal(
            [NotificationTriggers.Added],
            ReadSettings(connection).EnabledNotificationTriggers);
    }

    [Fact]
    public void Startup_ToleratesSettingsWithNoConfiguredWebhooks()
    {
        using var connection = OpenDatabase();
        SeedSettings(connection, enabledTriggers: "book-available", webhooksJson: null);

        RunStartup(connection);

        var settings = ReadSettings(connection);
        Assert.Equal([NotificationTriggers.Available], settings.EnabledNotificationTriggers);
        Assert.True(settings.Webhooks == null || settings.Webhooks.Count == 0);
    }

    private static SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static ServiceProvider BuildProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<ListenArrDbContext>(options =>
            options.UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name)));
        return services.BuildServiceProvider();
    }

    private static void RunStartup(SqliteConnection connection)
    {
        using var provider = BuildProvider(connection);
        provider.ApplyListenarrDatabaseMigrations();
    }

    /// <summary>
    /// Writes a settings row, then overwrites the two trigger columns with raw SQL so the stored
    /// text is exactly the pre-canonical form rather than whatever the current model would write.
    /// </summary>
    private static void SeedSettings(
        SqliteConnection connection,
        string enabledTriggers,
        string? webhooksJson)
    {
        using var provider = BuildProvider(connection);
        provider.ApplyListenarrDatabaseMigrations();

        using var context = provider
            .GetRequiredService<IDbContextFactory<ListenArrDbContext>>()
            .CreateDbContext();
        if (!context.ApplicationSettings.Any())
        {
            context.ApplicationSettings.Add(new ApplicationSettings());
            context.SaveChanges();
        }

        context.Database.ExecuteSqlInterpolated($"""
            UPDATE "ApplicationSettings"
            SET "EnabledNotificationTriggers" = {enabledTriggers},
                "Webhooks" = {webhooksJson}
            """);
    }

    private static ApplicationSettings ReadSettings(SqliteConnection connection)
    {
        using var provider = BuildProvider(connection);
        using var context = provider
            .GetRequiredService<IDbContextFactory<ListenArrDbContext>>()
            .CreateDbContext();
        return context.ApplicationSettings.AsNoTracking().Single();
    }
}
