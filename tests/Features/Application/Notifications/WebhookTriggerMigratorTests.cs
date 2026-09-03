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
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Notifications;

/// <summary>
/// Covers what happens to webhook rows that upgrading installs already hold. Changing only
/// what the settings UI offers would leave those rows subscribed to names dispatch never
/// reads, so they are rewritten once at startup.
/// </summary>
[Trait("Name", "WebhookTriggerMigratorTests")]
[Trait("Category", "Notifications")]
public sealed class WebhookTriggerMigratorTests : BaseTests
{
    private static (WebhookTriggerMigrator Migrator, Mock<IConfigurationService> Configuration, List<ApplicationSettings> Saved)
        BuildMigrator(ApplicationSettings settings)
    {
        var saved = new List<ApplicationSettings>();
        var configuration = new Mock<IConfigurationService>();
        configuration
            .Setup(service => service.GetApplicationSettingsAsync())
            .ReturnsAsync(settings);
        configuration
            .Setup(service => service.SaveApplicationSettingsAsync(It.IsAny<ApplicationSettings>()))
            .Callback<ApplicationSettings>(saved.Add)
            .Returns(Task.CompletedTask);

        var migrator = new WebhookTriggerMigrator(
            configuration.Object,
            NullLogger<WebhookTriggerMigrator>.Instance);

        return (migrator, configuration, saved);
    }

    private static ApplicationSettings SettingsWith(params WebhookConfiguration[] webhooks) => new()
    {
        Webhooks = [.. webhooks]
    };

    private static WebhookConfiguration Webhook(string name, params string[] triggers) => new()
    {
        Name = name,
        Url = "https://webhook.example.test/hook",
        Type = "Zapier",
        IsEnabled = true,
        Triggers = [.. triggers]
    };

    [Fact]
    public async Task Migrate_RewritesARowSavedByThePreviousSettingsUi()
    {
        var webhook = Webhook(
            "Everything ticked",
            WebhookTriggerVocabulary.LegacyBookAdded,
            WebhookTriggerVocabulary.LegacyBookDownloading,
            WebhookTriggerVocabulary.LegacyBookAvailable,
            WebhookTriggerVocabulary.LegacyBookCompleted);

        var (migrator, _, saved) = BuildMigrator(SettingsWith(webhook));

        await migrator.MigrateAsync();

        Assert.Equal([WebhookTriggerVocabulary.ImportedTrigger], webhook.Triggers);
        Assert.Single(saved);
    }

    [Fact]
    public async Task Migrate_LeavesAlreadyCorrectRowsUntouchedAndDoesNotWrite()
    {
        var webhook = Webhook(
            "Already current",
            WebhookTriggerVocabulary.ImportedTrigger,
            WebhookTriggerVocabulary.MovedTrigger);

        var (migrator, configuration, saved) = BuildMigrator(SettingsWith(webhook));

        await migrator.MigrateAsync();

        Assert.Equal(
            [WebhookTriggerVocabulary.ImportedTrigger, WebhookTriggerVocabulary.MovedTrigger],
            webhook.Triggers);
        Assert.Empty(saved);
        configuration.Verify(
            service => service.SaveApplicationSettingsAsync(It.IsAny<ApplicationSettings>()),
            Times.Never);
    }

    [Fact]
    public async Task Migrate_IsIdempotent()
    {
        var webhook = Webhook("Twice", WebhookTriggerVocabulary.LegacyBookAvailable);
        var (migrator, _, saved) = BuildMigrator(SettingsWith(webhook));

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        Assert.Equal([WebhookTriggerVocabulary.ImportedTrigger], webhook.Triggers);
        Assert.Single(saved);
    }

    [Fact]
    public async Task Migrate_EmptiesARowThatOnlySubscribedToNamesWithNoCounterpart()
    {
        // This is the honest cost of the shape: a webhook that only wanted "book added" has
        // no event to be moved onto, and it was never receiving anything anyway. The row is
        // left empty and the settings UI flags it rather than the operator finding out from
        // silence.
        var webhook = Webhook("Added only", WebhookTriggerVocabulary.LegacyBookAdded);
        var (migrator, _, saved) = BuildMigrator(SettingsWith(webhook));

        await migrator.MigrateAsync();

        Assert.Empty(webhook.Triggers);
        Assert.Single(saved);
    }

    [Fact]
    public async Task Migrate_RewritesEveryRowInOneSave()
    {
        var first = Webhook("First", WebhookTriggerVocabulary.LegacyBookAvailable);
        var second = Webhook("Second", WebhookTriggerVocabulary.LegacyBookCompleted);
        var third = Webhook("Third", WebhookTriggerVocabulary.FailedTrigger);

        var (migrator, _, saved) = BuildMigrator(SettingsWith(first, second, third));

        await migrator.MigrateAsync();

        Assert.Equal([WebhookTriggerVocabulary.ImportedTrigger], first.Triggers);
        Assert.Equal([WebhookTriggerVocabulary.ImportedTrigger], second.Triggers);
        Assert.Equal([WebhookTriggerVocabulary.FailedTrigger], third.Triggers);
        Assert.Single(saved);
    }

    [Fact]
    public async Task Migrate_DoesNothingWhenNoWebhooksAreConfigured()
    {
        var (migrator, _, saved) = BuildMigrator(new ApplicationSettings { Webhooks = [] });

        await migrator.MigrateAsync();

        Assert.Empty(saved);
    }

    [Fact]
    public async Task Migrate_SwallowsConfigurationFailures()
    {
        var configuration = new Mock<IConfigurationService>();
        configuration
            .Setup(service => service.GetApplicationSettingsAsync())
            .ThrowsAsync(new InvalidOperationException("settings unavailable"));

        var migrator = new WebhookTriggerMigrator(
            configuration.Object,
            NullLogger<WebhookTriggerMigrator>.Instance);

        await migrator.MigrateAsync();
    }
}
