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
        /// How long the whole exchange may take before it is abandoned, in the same sense as the
        /// Custom Script provider's script timeout: a bound on the operation, not on one step of
        /// it. A notification target may not hold up the operation that produced the event
        /// indefinitely.
        /// </summary>
        /// <remarks>
        /// MailKit's own <c>Timeout</c> property is per network operation, being the socket
        /// stream's read and write timeouts, so a server that answers every command just inside it
        /// can hold a ten round trip conversation open for many multiples of this. Both are set:
        /// the property bounds a single stalled read, and the linked token below bounds the
        /// exchange.
        /// </remarks>
        public const int SendTimeoutMilliseconds = 30_000;

        /// <summary>
        /// How long the closing QUIT may take. Its own budget rather than the send deadline, so
        /// that the worst case for a whole call stays near
        /// <see cref="SendTimeoutMilliseconds"/> rather than doubling it, which is what a second
        /// full-length budget after an already-exhausted one would do.
        /// </summary>
        public const int QuitTimeoutMilliseconds = 5_000;

        public async Task SendAsync(SmtpServer server, SmtpMessage message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(message);

            using var email = BuildMessage(message);
            using var client = new SmtpClient { Timeout = SendTimeoutMilliseconds };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(SendTimeoutMilliseconds);

            try
            {
                await client.ConnectAsync(server.Host, server.Port, ResolveSocketOptions(server), deadline.Token);

                if (!string.IsNullOrWhiteSpace(server.Username))
                {
                    await client.AuthenticateAsync(server.Username, server.Password ?? string.Empty, deadline.Token);
                }

                await client.SendAsync(email, deadline.Token);
            }
            catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Our own deadline, not the caller's cancel. Reported as what it is, so the Test
                // button says the server was too slow rather than that something was cancelled.
                throw new TimeoutException(
                    $"The mail server did not complete the exchange within {SendTimeoutMilliseconds / 1000} seconds.",
                    ex);
            }

            // The QUIT is outside the deadline, on its own short budget, and anything it throws
            // is dropped. The server accepted the message before this line, so nothing that
            // happens here can change whether the mail was delivered. Servers that close the
            // connection on "250 queued" rather than waiting for QUIT are common, and without
            // this a delivered mail is logged and reported to the operator as a failed send. A
            // cancellation escaping here is worse still: the provider rethrows it and abandons
            // delivery to every remaining target.
            try
            {
                using var quit = new CancellationTokenSource(QuitTimeoutMilliseconds);
                await client.DisconnectAsync(true, quit.Token);
            }
#pragma warning disable CA1031
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                // Deliberately swallowed. See above: the message is already accepted.
            }
#pragma warning restore CA1031
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
