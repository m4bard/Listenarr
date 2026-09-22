/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Infrastructure.DependencyInjection.Notifications;
using Listenarr.Infrastructure.Notifications.CustomScript;
using Listenarr.Infrastructure.Notifications.Email;
using Listenarr.Tests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Listenarr.Tests.Features.Infrastructure.DependencyInjection
{
    /// <summary>
    /// Asserts that every notification provider is actually wired into the container.
    /// </summary>
    /// <remarks>
    /// A provider nothing resolves is a provider that passes its own unit tests and does nothing in
    /// production, and a deleted registration line is invisible to every other test in this suite.
    /// These assertions read the descriptors rather than resolving, so the check does not drag in
    /// the whole dependency graph to answer a question about registration.
    /// </remarks>
    [Trait("Name", "NotificationSubscriberRegistrationTests")]
    [Trait("Category", "Notifications")]
    public class NotificationSubscriberRegistrationTests : BaseTests
    {
        private static IReadOnlyList<Type> RegisteredSubscriberTypes()
        {
            var services = new ServiceCollection();
            services.AddNotificationAndRealtimeServices();

            return services
                .Where(descriptor => descriptor.ServiceType == typeof(INotificationSubscriber))
                .Select(descriptor => descriptor.ImplementationType!)
                .ToList();
        }

        [Fact]
        public void EmailNotification_IsRegisteredAsANotificationSubscriber()
        {
            Assert.Contains(typeof(EmailNotification), RegisteredSubscriberTypes());
        }

        [Fact]
        public void CustomScriptNotification_IsStillRegisteredAsANotificationSubscriber()
        {
            // The control. If the query above found nothing at all, or read the wrong service type,
            // the Email assertion would fail for a reason that has nothing to do with Email.
            Assert.Contains(typeof(CustomScriptNotification), RegisteredSubscriberTypes());
        }

        [Fact]
        public void EveryRegisteredSubscriberIsRegisteredExactlyOnce()
        {
            // A duplicated registration is not a harmless copy-paste: PublishAsync hands the event
            // to every registered subscriber, so the operator gets two of each notification.
            var registered = RegisteredSubscriberTypes();

            Assert.Equal(registered.Distinct().Count(), registered.Count);
        }

        [Fact]
        public void TheSmtpTransportIsRegistered()
        {
            var services = new ServiceCollection();
            services.AddNotificationAndRealtimeServices();

            var transport = Assert.Single(
                services.Where(descriptor => descriptor.ServiceType == typeof(ISmtpTransport)));
            Assert.Equal(typeof(MailKitSmtpTransport), transport.ImplementationType);
        }
    }
}
