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

namespace Listenarr.Tests.Features.Infrastructure.Notifications.Delivery;

/// <summary>
/// Exercises the fan-out that carries a trigger to every subscribed channel. No request leaves
/// the process: the transport is a mocked <see cref="HttpMessageHandler"/>.
/// </summary>
[Trait("Name", "NotificationServiceDispatchTests")]
[Trait("Category", "NotificationService")]
public sealed class NotificationServiceDispatchTests : BaseTests
{
    private const string ConfiguredWebhookUrl = "https://discord.com/api/webhooks/configured-row";
    private const string LegacySingleWebhookUrl = "https://discord.com/api/webhooks/legacy-single";

    /// <summary>
    /// The half of the defect that a vocabulary change alone does not reach. A book lifecycle
    /// event has to arrive at a webhook configured in the settings UI, not only at the legacy
    /// single webhook URL. The legacy URL is left unset here so the only way a request can be
    /// observed is through the configured row.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_DeliversABookLifecycleEventToAConfiguredWebhookRow()
    {
        var requestedUrls = new List<string>();
        var service = BuildService(
            requestedUrls,
            legacySingleWebhookUrl: string.Empty,
            legacyEnabledTriggers: [],
            webhooks:
            [
                new WebhookConfiguration
                {
                    Name = "Configured in the settings UI",
                    Url = ConfiguredWebhookUrl,
                    Triggers = [NotificationTriggers.Added],
                    IsEnabled = true
                }
            ]);

        await service.DispatchAsync(
            NotificationTriggers.Added,
            new { title = "Dispatch Test Book", authors = new[] { "Test Author" } });

        Assert.Equal([ConfiguredWebhookUrl], DestinationsInOrder(requestedUrls));
    }

    [Fact]
    public async Task DispatchAsync_DeliversToTheLegacyWebhookAndTheConfiguredRowTogether()
    {
        var requestedUrls = new List<string>();
        var service = BuildService(
            requestedUrls,
            legacySingleWebhookUrl: LegacySingleWebhookUrl,
            legacyEnabledTriggers: [NotificationTriggers.Added],
            webhooks:
            [
                new WebhookConfiguration
                {
                    Name = "Configured in the settings UI",
                    Url = ConfiguredWebhookUrl,
                    Triggers = [NotificationTriggers.Added],
                    IsEnabled = true
                }
            ]);

        await service.DispatchAsync(NotificationTriggers.Added, new { title = "Both Channels" });

        Assert.Equal(
            [LegacySingleWebhookUrl, ConfiguredWebhookUrl],
            DestinationsInOrder(requestedUrls));
    }

    [Fact]
    public async Task DispatchAsync_SkipsAWebhookThatDidNotSubscribeToTheTrigger()
    {
        var requestedUrls = new List<string>();
        var service = BuildService(
            requestedUrls,
            legacySingleWebhookUrl: string.Empty,
            legacyEnabledTriggers: [],
            webhooks:
            [
                new WebhookConfiguration
                {
                    Name = "Subscribed to something else",
                    Url = ConfiguredWebhookUrl,
                    Triggers = [NotificationTriggers.Failed],
                    IsEnabled = true
                }
            ]);

        await service.DispatchAsync(NotificationTriggers.Added, new { title = "Not Subscribed" });

        Assert.Empty(requestedUrls);
    }

    [Fact]
    public async Task DispatchAsync_SkipsADisabledWebhook()
    {
        var requestedUrls = new List<string>();
        var service = BuildService(
            requestedUrls,
            legacySingleWebhookUrl: string.Empty,
            legacyEnabledTriggers: [],
            webhooks:
            [
                new WebhookConfiguration
                {
                    Name = "Disabled",
                    Url = ConfiguredWebhookUrl,
                    Triggers = [NotificationTriggers.Added],
                    IsEnabled = false
                }
            ]);

        await service.DispatchAsync(NotificationTriggers.Added, new { title = "Disabled" });

        Assert.Empty(requestedUrls);
    }

    /// <summary>
    /// A row still holding the pre-canonical spelling must not be dispatched to under that name.
    /// The stored-data repair is what moves such a row onto the canonical vocabulary; this pins
    /// that the dispatcher itself does not quietly accept both.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_DoesNotMatchAWebhookStillHoldingTheLegacySpelling()
    {
        var requestedUrls = new List<string>();
        var service = BuildService(
            requestedUrls,
            legacySingleWebhookUrl: string.Empty,
            legacyEnabledTriggers: [],
            webhooks:
            [
                new WebhookConfiguration
                {
                    Name = "Never migrated",
                    Url = ConfiguredWebhookUrl,
                    Triggers = ["book-added"],
                    IsEnabled = true
                }
            ]);

        await service.DispatchAsync(NotificationTriggers.Added, new { title = "Unmigrated Row" });

        Assert.Empty(requestedUrls);
    }

    /// <summary>
    /// The destinations that received at least one request, in the order they were first reached.
    /// Deliberately not a call count: a single delivery to a Discord-style URL already posts twice
    /// on canary, because the Discord branch of SendNotificationAsync falls through into the
    /// generic fallback instead of returning. That behaviour predates this change and is not what
    /// these tests are about.
    /// </summary>
    private static List<string> DestinationsInOrder(IEnumerable<string> requestedUrls) =>
        requestedUrls.Distinct(StringComparer.Ordinal).ToList();

    private static NotificationService BuildService(
        List<string> requestedUrls,
        string legacySingleWebhookUrl,
        List<string> legacyEnabledTriggers,
        List<WebhookConfiguration> webhooks)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    requestedUrls.Add(request.RequestUri!.ToString());
                }
            })
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK));

        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.GetStartupConfigAsync())
            .ReturnsAsync(new StartupConfig { UrlBase = "https://listenarr.example.com" });
        configurationService
            .Setup(service => service.GetApplicationSettingsAsync())
            .ReturnsAsync(new ApplicationSettings
            {
                WebhookUrl = legacySingleWebhookUrl,
                EnabledNotificationTriggers = legacyEnabledTriggers,
                Webhooks = webhooks
            });
        configurationService
            .Setup(service => service.GetWebhookConfigurationsAsync())
            .ReturnsAsync(webhooks);

        var services = new ServiceCollection();
        services.AddSingleton<INotificationPayloadBuilder, NotificationPayloadBuilderAdapter>();
        using var provider = services.BuildServiceProvider();

        return new NotificationService(
            new HttpClient(handler.Object),
            Mock.Of<ILogger<NotificationService>>(),
            configurationService.Object,
            provider.GetRequiredService<INotificationPayloadBuilder>(),
            Mock.Of<IRequestContextAccessor>());
    }
}
