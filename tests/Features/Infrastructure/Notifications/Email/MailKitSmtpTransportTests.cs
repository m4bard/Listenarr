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
    }
}
