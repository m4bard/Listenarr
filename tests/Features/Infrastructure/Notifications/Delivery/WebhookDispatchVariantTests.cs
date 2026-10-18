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

namespace Listenarr.Tests.Features.Infrastructure.Notifications.Delivery
{
    /// <summary>Variant of the webhook dispatch tests that uses only API present on every revision.</summary>
    [Trait("Name", "WebhookDispatchVariantTests")]
    [Trait("Category", "Notifications")]
    public sealed class WebhookDispatchVariantTests : BaseTests
    {
        private const string WebhookUrl = "https://hooks.notification-target.example/inbound";

        [Theory]
        [InlineData("book-completed", 1)]   // UI name: regression
        [InlineData("Imported", 1)]         // older internal spelling: compat control
        [InlineData("book-added", 0)]       // different event: must not fire
        public async Task ImportedDownload_ReachesWebhooksBySubscription(string stored, int expectedPosts)
        {
            var posts = await Run(stored, true, svc => svc.OnDownloadImportedAsync(new Download { Id = "d1", Title = "A Synthetic Audiobook" }));
            Assert.Equal(expectedPosts, posts);
        }

        [Fact]
        public async Task FailedDownload_ReachesWebhookSubscribedToFailed_Control()
        {
            var posts = await Run("Failed", true, svc => svc.OnDownloadFailedAsync(new Download { Id = "d2", Title = "A Synthetic Audiobook", ErrorMessage = "refused" }));
            Assert.Equal(1, posts);
        }

        [Fact]
        public async Task DisabledWebhook_HearsNothing_ApparatusControl()
        {
            var posts = await Run("Imported", false, svc => svc.OnDownloadImportedAsync(new Download { Id = "d3", Title = "A Synthetic Audiobook" }));
            Assert.Equal(0, posts);
        }

        private static async Task<int> Run(string storedTrigger, bool enabled, Func<NotificationService, Task> act)
        {
            var count = 0;
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((r, _) => { if (r.Method == HttpMethod.Post) Interlocked.Increment(ref count); })
                .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK));
            var settings = new ApplicationSettings
            {
                WebhookUrl = string.Empty,
                EnabledNotificationTriggers = [],
                Webhooks = [new WebhookConfiguration { Name = "Test target", Url = WebhookUrl, Type = "Zapier", Triggers = [storedTrigger], IsEnabled = enabled }]
            };
            var config = new Mock<IConfigurationService>();
            config.Setup(s => s.GetApplicationSettingsAsync()).ReturnsAsync(settings);
            config.Setup(s => s.GetWebhookConfigurationsAsync()).ReturnsAsync(settings.Webhooks);
            config.Setup(s => s.GetStartupConfigAsync()).ReturnsAsync(new StartupConfig());
            using var http = new HttpClient(handler.Object);
            var svc = new NotificationService(http, Mock.Of<ILogger<NotificationService>>(), config.Object,
                new NotificationPayloadBuilderAdapter(), Mock.Of<IRequestContextAccessor>());
            await act(svc);
            return count;
        }
    }
}
