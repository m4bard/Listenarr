/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Domain.Notifications;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Notifications.Delivery
{
    /// <summary>
    /// Covers the single entry point: one publish, fanned out to every subscriber that supports the
    /// channel, exactly once each, with no subscriber able to affect another or the caller.
    /// </summary>
    [Trait("Name", "NotificationServicePublishTests")]
    [Trait("Category", "Notifications")]
    public class NotificationServicePublishTests : BaseTests
    {
        private sealed class RecordingSubscriber : INotificationSubscriber
        {
            private readonly Func<NotificationChannel, bool> _supports;
            private readonly Exception? _throws;

            public RecordingSubscriber(string name, Func<NotificationChannel, bool>? supports = null, Exception? throws = null)
            {
                Name = name;
                _supports = supports ?? (_ => true);
                _throws = throws;
            }

            public string Name { get; }

            public List<NotificationEvent> Received { get; } = new();

            public bool Supports(NotificationChannel channel) => _supports(channel);

            public Task NotifyAsync(NotificationEvent notification, CancellationToken cancellationToken = default)
            {
                Received.Add(notification);
                return _throws == null ? Task.CompletedTask : Task.FromException(_throws);
            }

            public Task<NotificationSubscriberTestResult> TestAsync(string configurationId, CancellationToken cancellationToken = default) =>
                Task.FromResult(NotificationSubscriberTestResult.Success());
        }

        /// <summary>
        /// A configuration service with the EnableNotifications master switch on, which is what the
        /// rest of these tests are about. The switch defaults to off on a real instance.
        /// </summary>
        private static Mock<IConfigurationService> AConfiguration(
            bool enableNotifications = true,
            List<WebhookConfiguration>? webhooks = null)
        {
            var configuration = new Mock<IConfigurationService>();
            configuration.Setup(service => service.GetWebhookConfigurationsAsync())
                .ReturnsAsync(webhooks ?? new List<WebhookConfiguration>());
            configuration.Setup(service => service.GetApplicationSettingsAsync())
                .ReturnsAsync(new ApplicationSettings { EnableNotifications = enableNotifications });

            return configuration;
        }

        private static NotificationService BuildSubject(params INotificationSubscriber[] subscribers) =>
            BuildSubject(AConfiguration().Object, subscribers);

        private static NotificationService BuildSubject(
            IConfigurationService configuration,
            params INotificationSubscriber[] subscribers)
        {
            return new NotificationService(
                new HttpClient(),
                Mock.Of<ILogger<NotificationService>>(),
                configuration,
                Mock.Of<INotificationPayloadBuilder>(),
                requestContextAccessor: null,
                subscribers: subscribers);
        }

        private static NotificationEvent AnEvent(NotificationChannel channel) => new() { Channel = channel };

        [Fact]
        public async Task PublishAsync_DeliversToEverySupportingSubscriberExactlyOnce()
        {
            var first = new RecordingSubscriber("First");
            var second = new RecordingSubscriber("Second");
            var subject = BuildSubject(first, second);

            await subject.PublishAsync(AnEvent(NotificationChannel.Download));

            Assert.Equal(NotificationChannel.Download, Assert.Single(first.Received).Channel);
            Assert.Equal(NotificationChannel.Download, Assert.Single(second.Received).Channel);
        }

        [Fact]
        public async Task PublishAsync_SkipsSubscribersThatDoNotSupportTheChannel()
        {
            var grabOnly = new RecordingSubscriber("GrabOnly", channel => channel == NotificationChannel.Grab);
            var subject = BuildSubject(grabOnly);

            await subject.PublishAsync(AnEvent(NotificationChannel.Download));

            Assert.Empty(grabOnly.Received);
        }

        [Fact]
        public async Task PublishAsync_ContinuesAfterASubscriberThrows()
        {
            // One broken target must not silence the rest, and must not reach the caller. This is the
            // property the URL-matching webhook chain cannot offer, because its branches share a
            // single method body.
            var failing = new RecordingSubscriber("Failing", throws: new InvalidOperationException("boom"));
            var healthy = new RecordingSubscriber("Healthy");
            var subject = BuildSubject(failing, healthy);

            await subject.PublishAsync(AnEvent(NotificationChannel.Download));

            Assert.Single(healthy.Received);
        }

        [Fact]
        public async Task PublishAsync_WithNoSubscribersIsHarmless()
        {
            await BuildSubject().PublishAsync(AnEvent(NotificationChannel.Download));
        }

        [Fact]
        public async Task PublishAsync_WithNotificationsEnabled_CallsSubscribers()
        {
            // The on half of the pair below. Stated on its own so that the off case is a comparison
            // against something, rather than an assertion that nothing happened.
            var subscriber = new RecordingSubscriber("Recorder");
            var subject = BuildSubject(AConfiguration(enableNotifications: true).Object, subscriber);

            await subject.PublishAsync(AnEvent(NotificationChannel.Download));

            Assert.Single(subscriber.Received);
        }

        [Fact]
        public async Task PublishAsync_WithNotificationsDisabled_CallsNoSubscriber()
        {
            var subscriber = new RecordingSubscriber("Recorder");
            var subject = BuildSubject(AConfiguration(enableNotifications: false).Object, subscriber);

            await subject.PublishAsync(AnEvent(NotificationChannel.Download));

            Assert.Empty(subscriber.Received);
        }

        [Fact]
        public async Task PublishAsync_WhenTheSettingCannotBeRead_SuppressesDeliveryWithoutThrowing()
        {
            // Chosen behaviour on an unreadable setting: suppress. A subscriber runs an
            // operator-authored executable, and the caller still must not see the failure.
            var subscriber = new RecordingSubscriber("Recorder");
            var configuration = new Mock<IConfigurationService>();
            configuration.Setup(service => service.GetApplicationSettingsAsync())
                .ThrowsAsync(new IOException("simulated settings read failure"));
            var subject = BuildSubject(configuration.Object, subscriber);

            await subject.PublishAsync(AnEvent(NotificationChannel.Download));

            Assert.Empty(subscriber.Received);
        }

        [Fact]
        public async Task OnDownloadImportedAsync_PublishesTheDownloadChannel()
        {
            var subscriber = new RecordingSubscriber("Recorder");
            var subject = BuildSubject(subscriber);

            await subject.OnDownloadImportedAsync(new Download { Id = "d1", Title = "Frankenstein" });

            var published = Assert.Single(subscriber.Received);
            Assert.Equal(NotificationChannel.Download, published.Channel);
            Assert.Equal("Frankenstein", published.Book?.Title);
        }

        [Fact]
        public async Task OnDownloadFailedAsync_PublishesTheDownloadFailedChannel()
        {
            var subscriber = new RecordingSubscriber("Recorder");
            var subject = BuildSubject(subscriber);

            await subject.OnDownloadFailedAsync(new Download
            {
                Id = "d2",
                Title = "Dracula",
                ErrorMessage = "client reported a failure",
            });

            var published = Assert.Single(subscriber.Received);
            Assert.Equal(NotificationChannel.DownloadFailed, published.Channel);
            Assert.Equal("client reported a failure", published.Download?.ErrorMessage);
        }

        [Fact]
        public async Task OnDownloadImportedAsync_ReachesSubscribersEvenThoughNoWebhookMatches()
        {
            // A configured webhook that subscribes to a different trigger gets nothing for this
            // event. A subscriber must not inherit that: it subscribes by channel, not by trigger
            // name, and the two dispatches are independent.
            var subscriber = new RecordingSubscriber("Recorder");
            var configuration = AConfiguration(webhooks: new List<WebhookConfiguration>
            {
                new()
                {
                    Name = "A Webhook",
                    Url = "https://example.invalid/hook",
                    IsEnabled = true,
                    Triggers = new List<string> { "book-completed" },
                },
            });

            var subject = BuildSubject(configuration.Object, subscriber);

            await subject.OnDownloadImportedAsync(new Download { Id = "d3", Title = "Frankenstein" });

            Assert.Single(subscriber.Received);
        }
    }
}
