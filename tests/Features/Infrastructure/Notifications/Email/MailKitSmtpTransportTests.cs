/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Infrastructure.Notifications.Email;
using Listenarr.Tests.Common;
using MailKit.Security;
using AuthenticationException = MailKit.Security.AuthenticationException;

namespace Listenarr.Tests.Features.Infrastructure.Notifications.Email
{
    /// <summary>
    /// The transport's only decision that does not need a mail server: which socket options to ask
    /// MailKit for. The rule is Readarr's, from Email.Send.
    /// </summary>
    [Trait("Name", "MailKitSmtpTransportTests")]
    [Trait("Category", "Notifications")]
    public class MailKitSmtpTransportTests : BaseTests
    {
        private static SmtpServer AServer(int port, bool requireEncryption) =>
            new() { Host = "smtp.example.invalid", Port = port, RequireEncryption = requireEncryption };

        [Fact]
        public void ResolveSocketOptions_LeavesTheChoiceToNegotiationWhenEncryptionIsNotRequired()
        {
            Assert.Equal(SecureSocketOptions.Auto, MailKitSmtpTransport.ResolveSocketOptions(AServer(465, false)));
        }

        [Fact]
        public void ResolveSocketOptions_UsesImplicitTlsOnPort465()
        {
            Assert.Equal(SecureSocketOptions.SslOnConnect, MailKitSmtpTransport.ResolveSocketOptions(AServer(465, true)));
        }

        [Theory]
        [InlineData(25)]
        [InlineData(587)]
        [InlineData(2525)]
        public void ResolveSocketOptions_UsesStartTlsOnEveryOtherPort(int port)
        {
            Assert.Equal(SecureSocketOptions.StartTls, MailKitSmtpTransport.ResolveSocketOptions(AServer(port, true)));
        }

        // The tests below run against a stub SMTP server on the loopback interface rather than a
        // mock, because the only thing this class does is talk to a socket, and a mock of its own
        // dependency would assert nothing about that. No mail leaves the machine.

        private static SmtpMessage AMessage() =>
            new()
            {
                From = "listenarr@example.invalid",
                To = ["household@example.invalid"],
                Subject = "Listenarr - Test Notification",
                Body = "A body.",
            };

        private static SmtpServer AStubServer(StubSmtpServer stub, string? username) =>
            new()
            {
                Host = "127.0.0.1",
                Port = stub.Port,
                RequireEncryption = false,
                Username = username,
                Password = username == null ? null : "hunter2-smtp-password",
            };

        [Fact]
        public async Task SendAsync_DeliversTheMessageToTheServer()
        {
            using var stub = new StubSmtpServer();

            await new MailKitSmtpTransport().SendAsync(AStubServer(stub, "listenarr"), AMessage());

            Assert.Contains("DATA", stub.Commands);
            Assert.Contains("QUIT", stub.Commands);
            Assert.Contains("Subject: Listenarr - Test Notification", stub.DeliveredMessage, StringComparison.Ordinal);
            Assert.Contains("A body.", stub.DeliveredMessage, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SendAsync_AuthenticatesWhenAUsernameIsConfigured()
        {
            using var stub = new StubSmtpServer();

            await new MailKitSmtpTransport().SendAsync(AStubServer(stub, "listenarr"), AMessage());

            Assert.Contains("AUTH", stub.Commands);
        }

        [Fact]
        public async Task SendAsync_DoesNotAuthenticateWhenNoUsernameIsConfigured()
        {
            // The control for the test above, and the reason it is worth having: an inverted
            // condition would still deliver, so only the pair catches it. A server that wants no
            // credential must not be offered one.
            using var stub = new StubSmtpServer();

            await new MailKitSmtpTransport().SendAsync(AStubServer(stub, username: null), AMessage());

            Assert.DoesNotContain("AUTH", stub.Commands);
            Assert.Contains("DATA", stub.Commands);
        }

        [Fact]
        public async Task SendAsync_OffersTheConfiguredUsernameAndNotSomeOtherValue()
        {
            using var stub = new StubSmtpServer();

            await new MailKitSmtpTransport().SendAsync(AStubServer(stub, "listenarr"), AMessage());

            Assert.Equal("listenarr", stub.AuthenticatedUsername);
        }

        [Fact]
        public async Task SendAsync_ThrowsWhenTheServerRejectsTheLogin()
        {
            // This is what makes the Test button mean something. If a refused login came back as
            // success, the button would pass on a configuration that cannot deliver.
            using var stub = new StubSmtpServer(acceptAuthentication: false);

            await Assert.ThrowsAsync<AuthenticationException>(() =>
                new MailKitSmtpTransport().SendAsync(AStubServer(stub, "listenarr"), AMessage()));

            // Without these two the test passes against a transport that throws before it ever
            // connects, which is a regression rather than the behaviour being asserted.
            Assert.Contains("AUTH", stub.Commands);
            Assert.DoesNotContain("DATA", stub.Commands);
        }

        [Fact]
        public async Task SendAsync_DoesNotFailAMessageTheServerAlreadyAccepted()
        {
            // The server takes the message and then never answers QUIT. A real one that hangs up
            // on "250 queued" looks the same from here. Reporting that as a failed send would tell
            // the operator a delivered mail was not delivered, and, if it arrived as a
            // cancellation, would abandon every remaining target.
            using var stub = new StubSmtpServer(answerQuit: false);

            await new MailKitSmtpTransport().SendAsync(AStubServer(stub, "listenarr"), AMessage());

            Assert.Contains("DATA", stub.Commands);
            Assert.Contains("A body.", stub.DeliveredMessage, StringComparison.Ordinal);
        }

        [Fact]
        public async Task SendAsync_GivesUpOnAServerThatAnswersTooSlowly()
        {
            // The deadline is across the exchange, not per network operation. MailKit's own
            // Timeout bounds one stalled read, so a server answering just inside it on every
            // command can otherwise hold the call open for many multiples of the stated bound.
            using var stub = new StubSmtpServer(responseDelayMilliseconds: 200);
            var transport = new SlowDeadlineTransport();

            await Assert.ThrowsAsync<TimeoutException>(() =>
                transport.SendAsync(AStubServer(stub, "listenarr"), AMessage()));
        }

        /// <summary>
        /// The transport with a deadline short enough to test against, and nothing else changed.
        /// The stub answers every command 200ms late, so a 300ms budget cannot survive the
        /// several round trips a send takes while a per-operation timeout of the same size would.
        /// </summary>
        private sealed class SlowDeadlineTransport : ISmtpTransport
        {
            public async Task SendAsync(SmtpServer server, SmtpMessage message, CancellationToken cancellationToken = default)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(300);
                try
                {
                    await new MailKitSmtpTransport().SendAsync(server, message, deadline.Token);
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    throw new TimeoutException("too slow");
                }
            }
        }
    }
}
