/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Globalization;
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
        public async Task NotifyAsync_PutsEachRecipientListInItsOwnField()
        {
            // Not cosmetic. A BCC recipient who arrives in the CC header has been disclosed to
            // every other recipient, and nothing about the delivery looks wrong.
            var email = AnEmail(channels: NotificationChannel.Download);
            email.To = ["to@example.invalid"];
            email.Cc = ["cc@example.invalid"];
            email.Bcc = ["bcc-one@example.invalid", "bcc-two@example.invalid"];
            var subject = BuildSubject(email);

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            var (_, message) = Assert.Single(_sent);
            Assert.Equal(["to@example.invalid"], message.To);
            Assert.Equal(["cc@example.invalid"], message.Cc);
            Assert.Equal(["bcc-one@example.invalid", "bcc-two@example.invalid"], message.Bcc);
        }

        [Fact]
        public async Task TestAsync_RefusesABlankAddressRatherThanQuietlyDroppingIt()
        {
            // Deliberately stricter than Readarr here, and worth saying why. FluentValidation's
            // EmailAddress() passes an empty string, so Readarr accepts a blank entry and then
            // MimeKit throws on it at send time, which an operator reads as an unexplained
            // failure. Refusing it names the field instead.
            var email = AnEmail(channels: NotificationChannel.Download);
            email.To = ["to@example.invalid", "   "];
            var subject = BuildSubject(email);

            var result = await subject.TestAsync("Household");

            Assert.False(result.IsValid);
            Assert.Contains(result.Failures, failure => failure.Contains("not a valid email address", StringComparison.Ordinal));
            Assert.Empty(_sent);
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
            // Asserting on _sent here would prove nothing: it is only ever filled by the callback
            // BuildSubject registers, and this test does not call BuildSubject, so it would be
            // empty against an implementation that sent a thousand messages. The transport is
            // asked directly instead.
            _configuration.Setup(service => service.GetEmailConfigurationsAsync())
                .ThrowsAsync(new IOException("settings unreadable"));
            var subject = new EmailNotification(_configuration.Object, _transport.Object, CapturingLogger());

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            _transport.Verify(
                transport => transport.SendAsync(It.IsAny<SmtpServer>(), It.IsAny<SmtpMessage>(), It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.Contains(_logged, line => line.Contains("Could not read email configuration", StringComparison.Ordinal));
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
        public async Task FailureText_IsLeftAloneWhenItNeverContainedThePassword()
        {
            // Redaction must not announce itself when it did nothing. The operator reads this
            // string, and a trailing marker on "the remote certificate is invalid" tells them
            // something was withheld when nothing was.
            var subject = BuildSubject(AnEmail(channels: NotificationChannel.Download));
            const string reason = "The remote certificate is invalid according to the validation procedure.";
            _transport
                .Setup(transport => transport.SendAsync(It.IsAny<SmtpServer>(), It.IsAny<SmtpMessage>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException(reason));

            var result = await subject.TestAsync("Household");

            Assert.False(result.IsValid);
            Assert.Equal(reason, Assert.Single(result.Failures));
        }

        [Theory]
        [InlineData("en-US", "istanbul", "535 auth failed for ISTANBUL")]
        [InlineData("tr-TR", "istanbul", "535 auth failed for ISTANBUL")]
        [InlineData("tr-TR", "ISTANBUL", "535 auth failed for istanbul")]
        [InlineData("tr-TR", "hunter2", "535 auth failed for HUNTER2")]
        public async Task PasswordRedaction_HoldsUnderALocaleThatCaseFoldsTheLetterIDifferently(
            string culture,
            string password,
            string serverReply)
        {
            // Turkish and Azeri fold I to a dotless i and i to a dotted I, so a case-insensitive
            // match that follows the current culture misses a password containing either, which
            // is most passwords. The last row is the control: plain ASCII still redacts under
            // tr-TR, so a failure in the other rows is the letter and not the apparatus.
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);

                var email = AnEmail(channels: NotificationChannel.Download);
                email.Password = password;
                var subject = BuildSubject(email);
                _transport
                    .Setup(transport => transport.SendAsync(It.IsAny<SmtpServer>(), It.IsAny<SmtpMessage>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new AuthenticationException(serverReply));

                var result = await subject.TestAsync("Household");

                Assert.DoesNotContain(
                    result.Failures,
                    failure => failure.Contains(password, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public async Task PasswordRedaction_DoesNotLeaveTheTailOfALongerPasswordBehind()
        {
            // The environment secret is a prefix of the password. Replacing it first would take
            // out the prefix and leave the rest of the password sitting in the message.
            var previous = Environment.GetEnvironmentVariable("PASSWORD");
            try
            {
                Environment.SetEnvironmentVariable("PASSWORD", "hunter");
                var email = AnEmail(channels: NotificationChannel.Download);
                email.Password = "hunter2swordfish";
                var subject = BuildSubject(email);
                _transport
                    .Setup(transport => transport.SendAsync(It.IsAny<SmtpServer>(), It.IsAny<SmtpMessage>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new AuthenticationException("535 rejected hunter2swordfish"));

                var result = await subject.TestAsync("Household");

                Assert.DoesNotContain(result.Failures, failure => failure.Contains("swordfish", StringComparison.Ordinal));
            }
            finally
            {
                Environment.SetEnvironmentVariable("PASSWORD", previous);
            }
        }

        [Fact]
        public async Task PasswordRedaction_LeavesTheMessageAloneWhenThePasswordIsOnlyWhitespace()
        {
            // A whitespace password cannot authenticate anything, and using it as a pattern would
            // replace every run of spaces in the text the operator reads.
            var email = AnEmail(channels: NotificationChannel.Download);
            email.Password = "   ";
            var subject = BuildSubject(email);
            // The run of spaces is the point: it is what a three-space password would match.
            const string reason = "The remote   certificate is invalid.";
            _transport
                .Setup(transport => transport.SendAsync(It.IsAny<SmtpServer>(), It.IsAny<SmtpMessage>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException(reason));

            var result = await subject.TestAsync("Household");

            Assert.Equal(reason, Assert.Single(result.Failures));
        }

        [Fact]
        public void TheMessageRecordDoesNotPrintTheBodyOrTheRecipientsWhenItIsFormatted()
        {
            var message = new SmtpMessage
            {
                From = "listenarr@example.invalid",
                To = ["household@example.invalid"],
                Bcc = ["secret@example.invalid"],
                Subject = "Listenarr - Book Downloaded",
                Body = "Frankenstein finished downloading.",
            };

            var formatted = message.ToString();

            Assert.DoesNotContain("household@example.invalid", formatted, StringComparison.Ordinal);
            Assert.DoesNotContain("secret@example.invalid", formatted, StringComparison.Ordinal);
            Assert.DoesNotContain("Frankenstein", formatted, StringComparison.Ordinal);
            // The control: it still formats as something useful rather than as nothing.
            Assert.Contains("Listenarr - Book Downloaded", formatted, StringComparison.Ordinal);
        }

        [Fact]
        public void TheServerRecordDoesNotPrintThePasswordWhenItIsFormatted()
        {
            // A record prints every property by default, so one structured log argument or one
            // string interpolation would be enough to leak it. The control is that the rest of
            // the record still formats, so a type that printed nothing could not pass this.
            var server = new SmtpServer
            {
                Host = "smtp.example.invalid",
                Port = 587,
                Username = "listenarr@example.invalid",
                Password = Password,
            };

            var formatted = server.ToString();

            Assert.DoesNotContain(Password, formatted, StringComparison.OrdinalIgnoreCase);
            // The control: host and port still format, so a type that printed nothing at all
            // could not pass. Username is omitted too, deliberately, so it is not asserted here.
            Assert.Contains("smtp.example.invalid", formatted, StringComparison.Ordinal);
            Assert.Contains("587", formatted, StringComparison.Ordinal);
            Assert.DoesNotContain("listenarr@example.invalid", formatted, StringComparison.Ordinal);
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
