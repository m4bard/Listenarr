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

        private static NotificationService BuildSubject(params INotificationSubscriber[] subscribers)
        {
            var configuration = new Mock<IConfigurationService>();
            configuration.Setup(service => service.GetWebhookConfigurationsAsync())
                .ReturnsAsync(new List<WebhookConfiguration>());

            return new NotificationService(
                new HttpClient(),
                Mock.Of<ILogger<NotificationService>>(),
                configuration.Object,
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
            // The per-webhook dispatch filters on a trigger name the settings UI cannot produce, so
            // that path delivers nothing for a real import. A subscriber must not inherit that.
            var subscriber = new RecordingSubscriber("Recorder");
            var configuration = new Mock<IConfigurationService>();
            configuration.Setup(service => service.GetWebhookConfigurationsAsync())
                .ReturnsAsync(new List<WebhookConfiguration>
                {
                    new()
                    {
                        Name = "A Webhook",
                        Url = "https://example.invalid/hook",
                        IsEnabled = true,
                        Triggers = new List<string> { "book-completed" },
                    },
                });

            var subject = new NotificationService(
                new HttpClient(),
                Mock.Of<ILogger<NotificationService>>(),
                configuration.Object,
                Mock.Of<INotificationPayloadBuilder>(),
                requestContextAccessor: null,
                subscribers: new[] { subscriber });

            await subject.OnDownloadImportedAsync(new Download { Id = "d3", Title = "Frankenstein" });

            Assert.Single(subscriber.Received);
        }
    }
}
