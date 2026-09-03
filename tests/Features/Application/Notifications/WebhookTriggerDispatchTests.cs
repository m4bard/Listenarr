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
using System.Net;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Notifications;

/// <summary>
/// Drives the real dispatch path with the trigger names the settings UI stores, which is
/// the check that was missing: every existing notification test supplied the enabled-trigger
/// list from the trigger under test, so the gate could never reject anything.
/// </summary>
[Trait("Name", "WebhookTriggerDispatchTests")]
[Trait("Category", "Notifications")]
public sealed class WebhookTriggerDispatchTests : BaseTests
{
    private static (NotificationService Service, List<Uri?> Posted) BuildService(
        params WebhookConfiguration[] webhooks)
    {
        var posted = new List<Uri?>();
        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => posted.Add(request.RequestUri))
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK));

        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.GetWebhookConfigurationsAsync())
            .ReturnsAsync(webhooks.ToList());
        configurationService
            .Setup(service => service.GetStartupConfigAsync())
            .ReturnsAsync(new StartupConfig { UrlBase = "https://listenarr.example.com" });

        var services = new ServiceCollection();
        services.AddSingleton<INotificationPayloadBuilder, NotificationPayloadBuilderAdapter>();
        var payloadBuilder = services.BuildServiceProvider().GetRequiredService<INotificationPayloadBuilder>();

        var service = new NotificationService(
            new HttpClient(handler.Object),
            Mock.Of<ILogger<NotificationService>>(),
            configurationService.Object,
            payloadBuilder,
            Mock.Of<IRequestContextAccessor>());

        return (service, posted);
    }

    private static WebhookConfiguration Webhook(params string[] triggers) => new()
    {
        Name = "Test webhook",
        Url = "https://webhook.example.test/hook",
        Type = "Zapier",
        IsEnabled = true,
        Triggers = [.. triggers]
    };

    private static Download ImportedDownload() => new()
    {
        Title = "A Synthetic Audiobook",
        Status = DownloadStatus.Moved
    };

    [Fact]
    public async Task ImportedNotification_ReachesAWebhookSavedByTheSettingsUi()
    {
        // The settings UI now writes WebhookTriggerVocabulary.Offered names, so a
        // webhook the operator ticked must actually receive the event.
        var (service, posted) = BuildService(Webhook(WebhookTriggerVocabulary.ImportedTrigger));

        await service.OnDownloadImportedAsync(ImportedDownload());

        Assert.Single(posted);
    }

    [Fact]
    public async Task FailedNotification_ReachesAWebhookSavedByTheSettingsUi()
    {
        var (service, posted) = BuildService(Webhook(WebhookTriggerVocabulary.FailedTrigger));

        await service.OnDownloadFailedAsync(new Download
        {
            Title = "A Synthetic Audiobook",
            Status = DownloadStatus.Failed,
            ErrorMessage = "synthetic failure"
        });

        Assert.Single(posted);
    }

    [Fact]
    public async Task EveryOfferedTrigger_ReachesAWebhookRow()
    {
        // Every name the settings UI offers must actually arrive at a row in
        // settings.Webhooks. Moved is raised by the move pipeline rather than by
        // NotificationService, so it is covered at its own gate by
        // WebhookTriggerVocabularyTests.WebhookRowDispatchSites_IterateTheWebhookList.
        var reachable = new List<string>();

        var (imported, importedPosts) = BuildService(Webhook(WebhookTriggerVocabulary.ImportedTrigger));
        await imported.OnDownloadImportedAsync(ImportedDownload());
        if (importedPosts.Count > 0) reachable.Add(WebhookTriggerVocabulary.ImportedTrigger);

        var (failed, failedPosts) = BuildService(Webhook(WebhookTriggerVocabulary.FailedTrigger));
        await failed.OnDownloadFailedAsync(new Download { Title = "A Synthetic Audiobook" });
        if (failedPosts.Count > 0) reachable.Add(WebhookTriggerVocabulary.FailedTrigger);

        reachable.Add(WebhookTriggerVocabulary.MovedTrigger);

        Assert.Equal(
            WebhookTriggerVocabulary.Offered.OrderBy(trigger => trigger, StringComparer.Ordinal).ToList(),
            reachable.OrderBy(trigger => trigger, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task SystemTrigger_IsGatedButNothingRaisesIt_SoItIsNotOffered()
    {
        // The gate works when called directly, which is why "System" looks live in source.
        // Nothing in production calls SendSystemNotificationAsync, so the settings UI does
        // not offer it. WebhookTriggerVocabularyTests.SystemTrigger_StillHasNoProductionCaller
        // is the tripwire for that changing.
        var (service, posted) = BuildService(Webhook(WebhookTriggerVocabulary.SystemTrigger));

        await service.SendSystemNotificationAsync("Synthetic title", "Synthetic message");

        Assert.Single(posted);
        Assert.DoesNotContain(
            WebhookTriggerVocabulary.SystemTrigger,
            WebhookTriggerVocabulary.Offered,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task LegacyTriggerNames_StillReachNothing_WhichIsWhyStoredRowsAreMigrated()
    {
        // A row persisted by the previous settings UI. Dispatch does not read these names
        // and this shape does not teach it to, so the stored row has to be rewritten
        // instead. WebhookTriggerMigratorTests covers that rewrite.
        var (service, posted) = BuildService(Webhook(
            WebhookTriggerVocabulary.LegacyBookAdded,
            WebhookTriggerVocabulary.LegacyBookDownloading,
            WebhookTriggerVocabulary.LegacyBookAvailable,
            WebhookTriggerVocabulary.LegacyBookCompleted));

        await service.OnDownloadImportedAsync(ImportedDownload());
        await service.OnDownloadFailedAsync(new Download { Title = "A Synthetic Audiobook" });

        Assert.Empty(posted);
    }

    [Fact]
    public async Task SendNotification_SendsNothingWhenTheTriggerIsNotInTheEnabledList()
    {
        // The enabled-trigger gate has to be able to say no. Tests that build the enabled
        // list from the trigger under test can never observe that, which is how a UI writing
        // names dispatch does not read went unnoticed.
        var (service, posted) = BuildService();

        await service.SendNotificationAsync(
            WebhookTriggerVocabulary.LegacyBookAdded,
            new { title = "A Synthetic Audiobook" },
            "https://webhook.example.test/hook",
            WebhookTriggerVocabulary.Offered.ToList());

        Assert.Empty(posted);
    }

    [Fact]
    public async Task SendNotification_SendsWhenTheTriggerIsInTheEnabledList()
    {
        var (service, posted) = BuildService();

        await service.SendNotificationAsync(
            WebhookTriggerVocabulary.LegacyBookAdded,
            new { title = "A Synthetic Audiobook" },
            "https://webhook.example.test/hook",
            WebhookTriggerVocabulary.LegacyGlobal.ToList());

        Assert.Single(posted);
    }

    [Fact]
    public async Task DisabledWebhook_ReceivesNothing()
    {
        var webhook = Webhook(WebhookTriggerVocabulary.ImportedTrigger);
        webhook.IsEnabled = false;
        var (service, posted) = BuildService(webhook);

        await service.OnDownloadImportedAsync(ImportedDownload());

        Assert.Empty(posted);
    }

    [Fact]
    public async Task WebhookSubscribedToADifferentEvent_ReceivesNothing()
    {
        var (service, posted) = BuildService(Webhook(WebhookTriggerVocabulary.FailedTrigger));

        await service.OnDownloadImportedAsync(ImportedDownload());

        Assert.Empty(posted);
    }
}
