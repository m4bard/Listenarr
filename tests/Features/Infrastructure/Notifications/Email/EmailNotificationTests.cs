/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Security.Authentication;
using Listenarr.Domain.Notifications;
using Listenarr.Infrastructure.Notifications.Email;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Notifications.Email
{
    [Trait("Name", "EmailNotificationTests")]
    [Trait("Category", "Notifications")]
    public class EmailNotificationTests : BaseTests
    {
        private const string Password = "hunter2-smtp-password";

        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<ISmtpTransport> _transport = new();
        private readonly List<(SmtpServer Server, SmtpMessage Message)> _sent = new();
        private readonly List<string> _logged = new();

        private EmailNotification BuildSubject(params EmailConfiguration[] emails)
        {
            _configuration.Setup(service => service.GetEmailConfigurationsAsync())
                .ReturnsAsync(emails.ToList());
            _transport
                .Setup(transport => transport.SendAsync(It.IsAny<SmtpServer>(), It.IsAny<SmtpMessage>(), It.IsAny<CancellationToken>()))
                .Callback<SmtpServer, SmtpMessage, CancellationToken>((server, message, _) => _sent.Add((server, message)))
                .Returns(Task.CompletedTask);

            return new EmailNotification(_configuration.Object, _transport.Object, CapturingLogger());
        }

        /// <summary>
        /// Records every line the provider logs, formatted message and exception alike, so a test
        /// can assert over everything that would reach a sink rather than over one chosen argument.
        /// </summary>
        private ILogger<EmailNotification> CapturingLogger()
        {
            var logger = new Mock<ILogger<EmailNotification>>();
            logger
                .Setup(candidate => candidate.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()))
                .Callback(new InvocationAction(invocation =>
                {
                    var state = invocation.Arguments[2];
                    var exception = invocation.Arguments[3] as Exception;
                    var formatter = invocation.Arguments[4] as Func<object, Exception?, string>;

                    _logged.Add(formatter != null ? formatter.Invoke(state!, exception) : state?.ToString() ?? string.Empty);
                    if (exception != null)
                    {
                        _logged.Add(exception.ToString());
                    }
                }));

            return logger.Object;
        }

        private static EmailConfiguration AnEmail(
            string name = "Household",
            bool enabled = true,
            params NotificationChannel[] channels) =>
            new()
            {
                Id = name,
                Name = name,
                Server = "smtp.example.invalid",
                Port = 587,
                RequireEncryption = true,
                Username = "listenarr@example.invalid",
                Password = Password,
                From = "listenarr@example.invalid",
                To = ["household@example.invalid"],
                IsEnabled = enabled,
                Channels = channels.ToList(),
            };

        private static NotificationEvent AnEvent(NotificationChannel channel) =>
            new()
            {
                Channel = channel,
                Book = new NotificationEventBook { Id = 1, Title = "Frankenstein", Authors = ["Mary Shelley"] },
            };

        [Fact]
        public void Name_MatchesTheNameOperatorsKnowFromTheOtherArrApplications()
        {
            Assert.Equal("Email", BuildSubject().Name);
        }

        [Fact]
        public void Supports_CoversEveryDeliverableChannel()
        {
            var subject = BuildSubject();

            Assert.All(
                Enum.GetValues<NotificationChannel>().Where(channel => channel != NotificationChannel.Test),
                channel => Assert.True(subject.Supports(channel)));
            Assert.False(subject.Supports(NotificationChannel.Test));
        }

        [Fact]
        public async Task NotifyAsync_HandsASuccessfulSendToTheTransport()
        {
            var subject = BuildSubject(AnEmail(channels: NotificationChannel.Download));

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            var (server, message) = Assert.Single(_sent);
            Assert.Equal("smtp.example.invalid", server.Host);
            Assert.Equal(587, server.Port);
            Assert.Equal(Password, server.Password);
            Assert.Equal("listenarr@example.invalid", message.From);
            Assert.Equal(["household@example.invalid"], message.To);
            Assert.Equal("Listenarr - Book Downloaded", message.Subject);
            Assert.Contains("Frankenstein", message.Body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NotifyAsync_SendsNothingForADisabledSubscriber()
        {
            // The control is the test above: the same target, the same event, differing only in
            // IsEnabled, does reach the transport.
            var subject = BuildSubject(AnEmail(enabled: false, channels: NotificationChannel.Download));

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            Assert.Empty(_sent);
        }

        [Fact]
        public async Task NotifyAsync_SendsNothingForAChannelTheTargetIsNotEnabledFor()
        {
            var subject = BuildSubject(AnEmail(channels: NotificationChannel.Download));

            await subject.NotifyAsync(AnEvent(NotificationChannel.Grab));

            Assert.Empty(_sent);
        }

        [Fact]
        public async Task NotifyAsync_SendsOnceThroughEveryTargetEnabledForTheChannel()
        {
            var subject = BuildSubject(
                AnEmail("First", channels: NotificationChannel.Download),
                AnEmail("Second", channels: NotificationChannel.Download),
                AnEmail("Third", channels: NotificationChannel.Grab));

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            Assert.Equal(2, _sent.Count);
        }

        [Fact]
        public async Task NotifyAsync_DoesNotSendThroughAnInvalidTarget()
        {
            var invalid = AnEmail(channels: NotificationChannel.Download);
            invalid.To = [];
            invalid.Cc = [];
            invalid.Bcc = [];
            var subject = BuildSubject(invalid);

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            Assert.Empty(_sent);
        }

        [Fact]
        public async Task NotifyAsync_DoesNotThrowWhenTheSendFails()
        {
            // A notification is a side effect of an operation and may not break it.
            var subject = BuildSubject(AnEmail(channels: NotificationChannel.Download));
            _transport
                .Setup(transport => transport.SendAsync(It.IsAny<SmtpServer>(), It.IsAny<SmtpMessage>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("mail server refused the connection"));

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            Assert.Contains(_logged, line => line.Contains("mail server refused the connection", StringComparison.Ordinal));
        }

        [Fact]
        public async Task NotifyAsync_DoesNotThrowWhenConfigurationCannotBeRead()
        {
            _configuration.Setup(service => service.GetEmailConfigurationsAsync())
                .ThrowsAsync(new IOException("settings unreadable"));
            var subject = new EmailNotification(_configuration.Object, _transport.Object, CapturingLogger());

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            Assert.Empty(_sent);
        }

        [Fact]
        public async Task TestAsync_ActuallySendsRatherThanOnlyValidating()
        {
            // A Test button that reported success without sending would report success on a
            // configuration that fails at the first real event, which is the whole reason to press
            // it.
            var subject = BuildSubject(AnEmail(channels: NotificationChannel.Download));

            var result = await subject.TestAsync("Household");

            Assert.True(result.IsValid);
            var (_, message) = Assert.Single(_sent);
            Assert.Equal("Listenarr - Test Notification", message.Subject);
        }

        [Fact]
        public async Task TestAsync_ReportsFailureWhenTheServerRejectsAuthentication()
        {
            // The control for the test above: same configuration, same call, and the only
            // difference is that the transport throws. If this returned IsValid the button would be
            // reporting success on a login the server refuses.
            var subject = BuildSubject(AnEmail(channels: NotificationChannel.Download));
            _transport
                .Setup(transport => transport.SendAsync(It.IsAny<SmtpServer>(), It.IsAny<SmtpMessage>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AuthenticationException("535: 5.7.8 Username and Password not accepted"));

            var result = await subject.TestAsync("Household");

            Assert.False(result.IsValid);
            Assert.Contains(result.Failures, failure => failure.Contains("535", StringComparison.Ordinal));
        }

        [Fact]
        public async Task TestAsync_ReportsTheValidationFailuresWithoutSending()
        {
            var invalid = AnEmail(channels: NotificationChannel.Download);
            invalid.Server = string.Empty;
            var subject = BuildSubject(invalid);

            var result = await subject.TestAsync("Household");

            Assert.False(result.IsValid);
            Assert.Contains("Server is required", result.Failures);
            Assert.Empty(_sent);
        }

        [Fact]
        public async Task TestAsync_ReportsFailureWhenNoSuchTargetIsConfigured()
        {
            var subject = BuildSubject(AnEmail(channels: NotificationChannel.Download));

            var result = await subject.TestAsync("does-not-exist");

            Assert.False(result.IsValid);
            Assert.Empty(_sent);
        }

        [Fact]
        public async Task SendFailure_KeepsThePasswordOutOfEveryLineItLogs()
        {
            // The control is the assertion that the rest of the server's reply survives: if the
            // provider had simply logged nothing, or logged an empty string, the "password absent"
            // half of this would pass while proving nothing.
            var subject = BuildSubject(AnEmail(channels: NotificationChannel.Download));
            _transport
                .Setup(transport => transport.SendAsync(It.IsAny<SmtpServer>(), It.IsAny<SmtpMessage>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AuthenticationException($"535 rejected login for listenarr with password {Password}"));

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            Assert.NotEmpty(_logged);
            Assert.Contains(_logged, line => line.Contains("535 rejected login", StringComparison.Ordinal));
            Assert.DoesNotContain(_logged, line => line.Contains(Password, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task TestFailure_KeepsThePasswordOutOfTheReasonShownToTheOperator()
        {
            var subject = BuildSubject(AnEmail(channels: NotificationChannel.Download));
            _transport
                .Setup(transport => transport.SendAsync(It.IsAny<SmtpServer>(), It.IsAny<SmtpMessage>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AuthenticationException($"535 rejected login for listenarr with password {Password}"));

            var result = await subject.TestAsync("Household");

            Assert.False(result.IsValid);
            Assert.Contains(result.Failures, failure => failure.Contains("535 rejected login", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Failures, failure => failure.Contains(Password, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task SuccessfulSend_DoesNotLogThePasswordEither()
        {
            var subject = BuildSubject(AnEmail(channels: NotificationChannel.Download));

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            Assert.NotEmpty(_logged);
            Assert.DoesNotContain(_logged, line => line.Contains(Password, StringComparison.OrdinalIgnoreCase));
        }
    }
}
