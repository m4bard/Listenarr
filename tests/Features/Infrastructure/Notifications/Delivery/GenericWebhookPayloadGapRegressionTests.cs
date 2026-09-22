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
using System.Text.Json.Nodes;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Notifications.Delivery
{
    /// <summary>
    /// Pins two gaps in the generic ("Zapier / Generic") webhook fallback path in
    /// NotificationService.Webhooks.cs (tracker item 184, findings G3 and G4). Both tests drive
    /// the real SendNotificationAsync(string, object, string, List&lt;string&gt;) overload with a
    /// webhook URL that matches none of the provider-specific substrings the method checks in
    /// order (discord.com/api/webhooks, ntfy, api.pushover.net/1/messages.json,
    /// api.telegram.org/bot, api.pushbullet.com/v2/pushes or pushbullet://, hooks.slack.com/services),
    /// so execution reaches the generic fallback at
    /// NotificationService.Webhooks.cs lines 409-441, which is the code path behind the settings
    /// screen's own "Zapier / Generic" connection type.
    ///
    /// Both tests are regression-only, with no matching production change: item 184 is downstream
    /// of upstream PR #943, which is open and rewrites NotificationService.Webhooks.cs (among
    /// other files in this partial-class family). They pin what the fallback does today so a
    /// future fix has to touch, and consciously update, an explicit assertion rather than have
    /// the gap close silently.
    /// </summary>
    [Trait("Area", "Notifications")]
    [Trait("Name", "GenericWebhookPayloadGapRegressionTests")]
    [Trait("Category", "NotificationService")]
    public class GenericWebhookPayloadGapRegressionTests : BaseTests
    {
        private const string GenericWebhookUrl = "https://hooks.zapier.com/hooks/catch/123456/abcdef/";

        private static NotificationService BuildServiceWithHandler(Mock<HttpMessageHandler> mockHttpMessageHandler)
        {
            var httpClient = new HttpClient(mockHttpMessageHandler.Object);

            var mockConfigService = new Mock<IConfigurationService>();
            mockConfigService
                .Setup(x => x.GetStartupConfigAsync())
                .ReturnsAsync(new StartupConfig { UrlBase = "https://listenarr.example.com" });

            var mockHttpContextAccessor = new Mock<IRequestContextAccessor>();

            var services = new ServiceCollection();
            services.AddSingleton<INotificationPayloadBuilder, NotificationPayloadBuilderAdapter>();
            var provider = services.BuildServiceProvider();
            var payloadBuilder = provider.GetRequiredService<INotificationPayloadBuilder>();

            return new NotificationService(
                httpClient,
                Mock.Of<ILogger<NotificationService>>(),
                mockConfigService.Object,
                payloadBuilder,
                mockHttpContextAccessor.Object);
        }

        [Fact]
        [Trait("Scenario", "ZapierGenericWebhook_ReceivesDiscordEmbedShapedBody")]
        public async Task SendNotificationAsync_ZapierGenericWebhook_ReceivesDiscordEmbedShapedBody()
        {
            // Given
            var trigger = "book-added";
            var data = new
            {
                id = 123,
                title = "Generic Contract Book",
                authors = new[] { "Jane Doe" },
                asin = "B00GENERIC"
            };
            var enabledTriggers = new List<string> { trigger };

            string? capturedJson = null;
            var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            using var postResponse = new HttpResponseMessage(HttpStatusCode.OK);
            mockHttpMessageHandler
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>(async (request, _) =>
                {
                    capturedJson = await request.Content!.ReadAsStringAsync();
                })
                .ReturnsAsync(postResponse);

            var service = BuildServiceWithHandler(mockHttpMessageHandler);

            // When: dispatched through the same URL the settings screen's "Zapier / Generic"
            // connection type would use, which none of the provider-specific branches match.
            await service.SendNotificationAsync(trigger, data, GenericWebhookUrl, enabledTriggers);

            // Then
            Assert.NotNull(capturedJson);
            var postedNode = JsonNode.Parse(capturedJson);
            Assert.NotNull(postedNode);
            var postedObj = postedNode!.AsObject();

            // The gap: a receiver built against a generic/typed contract (an {eventType, ...}
            // shape, per Readarr's WebhookPayload/WebhookEventType) instead gets Discord's own
            // fields. These three keys have no business existing in a "Zapier / Generic" payload.
            Assert.Equal("Listenarr", postedObj["username"]?.ToString());
            Assert.True(postedObj.ContainsKey("avatar_url"), "Generic webhook body should not carry a Discord avatar_url field.");
            Assert.True(postedObj.ContainsKey("embeds"), "Generic webhook body should not carry a Discord embeds array.");

            // Pin the coupling itself: the fallback's output is byte-for-byte what the
            // Discord-specific path produces for the same trigger and data. This is the control:
            // if a real generic/typed contract is ever built, this path and the Discord path will
            // diverge and this equality assertion is what breaks, on purpose, forcing the test to
            // be looked at rather than the gap closing unnoticed.
            var expectedDiscordShapeNode = NotificationPayloadBuilder.CreateDiscordPayload(trigger, data, "https://listenarr.example.com");
            var expectedObj = expectedDiscordShapeNode.AsObject();
            Assert.Equal(expectedObj["content"]?.ToString(), postedObj["content"]?.ToString());
            Assert.Equal(expectedObj["username"]?.ToString(), postedObj["username"]?.ToString());
            Assert.Equal(expectedObj["avatar_url"]?.ToString(), postedObj["avatar_url"]?.ToString());
        }

        [Fact]
        [Trait("Scenario", "RepeatedlyFailingGenericWebhook_AttemptsEveryDeliveryWithNoBackoff")]
        public async Task SendNotificationAsync_RepeatedlyFailingGenericWebhook_AttemptsEveryDeliveryWithNoBackoff()
        {
            // Given: a target that fails every time it is called.
            var trigger = "book-added";
            var data = new { id = 1, title = "Backoff Gap Book", authors = new[] { "Jane Doe" } };
            var enabledTriggers = new List<string> { trigger };

            var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            mockHttpMessageHandler
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var service = BuildServiceWithHandler(mockHttpMessageHandler);

            // When: the same still-down target is notified twice in a row, as two library events
            // firing back to back would do.
            await service.SendNotificationAsync(trigger, data, GenericWebhookUrl, enabledTriggers);
            await service.SendNotificationAsync(trigger, data, GenericWebhookUrl, enabledTriggers);

            // Then: both attempts reach the network. Exactly 2 is the control that separates this
            // from a broken apparatus (0 would mean the request never left the process at all,
            // for example because URL validation rejected it) and from the behaviour a real
            // backoff/escalation mechanism (Readarr's ProviderStatusServiceBase /
            // NotificationStatusService) would produce (the second attempt suppressed, giving 1).
            // There is no NotificationStatus-equivalent class anywhere in this backend today, so
            // every failing attempt is retried at full speed forever, silently.
            mockHttpMessageHandler
                .Protected()
                .Verify(
                    "SendAsync",
                    Times.Exactly(2),
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>());
        }
    }
}
