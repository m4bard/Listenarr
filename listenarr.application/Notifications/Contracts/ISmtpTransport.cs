/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Text;

namespace Listenarr.Application.Notifications.Contracts
{
    /// <summary>
    /// The mail server to hand a message to, without any notion of which events it is configured
    /// for. Kept separate from <see cref="Listenarr.Domain.Configuration.EmailConfiguration"/> so
    /// that the transport cannot see, and therefore cannot log, anything beyond what it needs to
    /// open the connection.
    /// </summary>
    public sealed record SmtpServer
    {
        public required string Host { get; init; }

        public required int Port { get; init; }

        /// <summary>
        /// Require SSL on connect (port 465) or StartTLS (any other port). False leaves the choice
        /// to negotiation.
        /// </summary>
        public bool RequireEncryption { get; init; }

        public string? Username { get; init; }

        public string? Password { get; init; }

        /// <summary>
        /// Keeps the credential out of the compiler-generated ToString.
        /// </summary>
        /// <remarks>
        /// A record prints every property by default, so <c>$"{server}"</c>, a structured log
        /// argument or a failing assertion's message would all render the password verbatim.
        /// Nothing does that today. The point of this type is that it cannot, and that has to be
        /// structural rather than a convention nobody can see.
        /// </remarks>
        private bool PrintMembers(StringBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"Host = {Host}, Port = {Port}, RequireEncryption = {RequireEncryption}");
            return true;
        }
    }

    /// <summary>One message, already addressed and rendered.</summary>
    public sealed record SmtpMessage
    {
        public required string From { get; init; }

        public IReadOnlyList<string> To { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> Cc { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> Bcc { get; init; } = Array.Empty<string>();

        public required string Subject { get; init; }

        public required string Body { get; init; }
    }

    /// <summary>
    /// Hands one message to an SMTP server. This exists to keep the network out of the provider's
    /// tests, in the same way <c>IProcessRunner</c> keeps process creation out of the Custom Script
    /// provider's tests.
    /// </summary>
    /// <remarks>
    /// A failure throws. That is load-bearing rather than stylistic: the Test button reports
    /// success only when nothing was thrown, so a transport that swallowed a rejected login would
    /// make the button report success on a configuration that cannot deliver.
    /// </remarks>
    public interface ISmtpTransport
    {
        Task SendAsync(SmtpServer server, SmtpMessage message, CancellationToken cancellationToken = default);
    }
}
