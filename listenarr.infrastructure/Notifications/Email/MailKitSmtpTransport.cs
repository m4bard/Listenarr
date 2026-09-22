/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Listenarr.Infrastructure.Notifications.Email
{
    /// <summary>
    /// The production <see cref="ISmtpTransport"/>, built on MailKit.
    /// </summary>
    /// <remarks>
    /// MailKit is what the family uses: it is a PackageReference in Readarr's Readarr.Core.csproj,
    /// Sonarr's Sonarr.Core.csproj and Prowlarr's Prowlarr.Core.csproj, and Readarr's Email
    /// provider drives it the same way this does (Email.cs, the Send method: SecureSocketOptions,
    /// Connect, Authenticate, Send, Disconnect).
    /// <para>
    /// It is also the only way to honour the RequireEncryption setting as Readarr defines it.
    /// System.Net.Mail.SmtpClient can negotiate StartTLS but cannot do implicit TLS on connect, so
    /// port 465 would silently behave as something other than what the checkbox promises.
    /// </para>
    /// <para>
    /// Nothing here logs. The transport holds the credential, so the one way it cannot leak it is
    /// to have no logger at all; the provider logs around the call instead, with the password
    /// passed to LogRedaction as an explicit secret.
    /// </para>
    /// </remarks>
    public sealed class MailKitSmtpTransport : ISmtpTransport
    {
        /// <summary>
        /// How long a send may take before it is abandoned. A notification target may not hold up
        /// the operation that produced the event indefinitely; the Custom Script provider bounds
        /// itself the same way.
        /// </summary>
        public const int SendTimeoutMilliseconds = 30_000;

        public async Task SendAsync(SmtpServer server, SmtpMessage message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(message);

            using var email = BuildMessage(message);
            using var client = new SmtpClient { Timeout = SendTimeoutMilliseconds };

            await client.ConnectAsync(server.Host, server.Port, ResolveSocketOptions(server), cancellationToken);

            if (!string.IsNullOrWhiteSpace(server.Username))
            {
                await client.AuthenticateAsync(server.Username, server.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(email, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }

        /// <summary>
        /// Readarr's rule, from Email.Send: encryption off leaves the choice to negotiation;
        /// encryption on means implicit TLS on port 465 and StartTLS everywhere else.
        /// </summary>
        internal static SecureSocketOptions ResolveSocketOptions(SmtpServer server)
        {
            ArgumentNullException.ThrowIfNull(server);

            if (!server.RequireEncryption)
            {
                return SecureSocketOptions.Auto;
            }

            return server.Port == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;
        }

        private static MimeMessage BuildMessage(SmtpMessage message)
        {
            var email = new MimeMessage();

            email.From.Add(MailboxAddress.Parse(message.From));
            email.To.AddRange(message.To.Select(MailboxAddress.Parse));
            email.Cc.AddRange(message.Cc.Select(MailboxAddress.Parse));
            email.Bcc.AddRange(message.Bcc.Select(MailboxAddress.Parse));
            email.Subject = message.Subject;
            email.Body = new TextPart("plain") { Text = message.Body };

            return email;
        }
    }
}
