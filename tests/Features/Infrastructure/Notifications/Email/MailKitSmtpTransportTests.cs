/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Infrastructure.Notifications.Email;
using Listenarr.Tests.Common;
using MailKit.Security;

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
        public async Task SendAsync_ThrowsWhenTheServerRejectsTheLogin()
        {
            // This is what makes the Test button mean something. If a refused login came back as
            // success, the button would pass on a configuration that cannot deliver.
            using var stub = new StubSmtpServer(acceptAuthentication: false);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                new MailKitSmtpTransport().SendAsync(AStubServer(stub, "listenarr"), AMessage()));

            Assert.DoesNotContain("DATA", stub.Commands);
        }
    }
}
