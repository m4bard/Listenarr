/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Domain.Notifications;

namespace Listenarr.Domain.Configuration
{
    /// <summary>
    /// One configured email target: an SMTP server Listenarr sends a message through when an event
    /// on one of its enabled channels is published.
    /// </summary>
    /// <remarks>
    /// The field set follows Readarr's EmailSettings
    /// (src/NzbDrone.Core/Notifications/Email/EmailSettings.cs), so that an operator arriving from
    /// another *arr finds the form they already know: server, port, require encryption, username,
    /// password, from address, and the three recipient lists.
    /// <para>
    /// Two differences, both deliberate. Readarr's "Attach Books" is not carried over: its own
    /// implementation attaches text extensions only and logs "Skipping audiobook file" for
    /// everything else (Email.cs, the attachment loop), so in Listenarr, where every file is an
    /// audiobook, the checkbox could never attach anything. And Id, Name, Channels and IsEnabled
    /// are Listenarr's, not Readarr's: Readarr carries those on the surrounding
    /// NotificationDefinition row, while Listenarr keeps configured instances as a list on
    /// ApplicationSettings, the same way <see cref="CustomScriptConfiguration"/> does.
    /// </para>
    /// </remarks>
    public class EmailConfiguration
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>Operator-chosen label, shown in the settings list.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Hostname or IP of the mail server. Readarr defaults this to Gmail's; so do we.</summary>
        public string Server { get; set; } = "smtp.gmail.com";

        public int Port { get; set; } = 587;

        /// <summary>
        /// Require SSL on connect (port 465 only) or StartTLS (any other port). The wording and the
        /// port-465 special case are Readarr's, from EmailSettings.RequireEncryption and the
        /// SecureSocketOptions branch in Email.Send.
        /// </summary>
        public bool RequireEncryption { get; set; }

        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// The SMTP password. Never returned to an API caller in the clear: a settings read replaces
        /// it with <c>ApiResponseRedactor.RedactedValue</c>, and a save that carries that sentinel
        /// back keeps the stored value rather than writing the sentinel over it.
        /// </summary>
        public string Password { get; set; } = string.Empty;

        public string From { get; set; } = string.Empty;

        public List<string> To { get; set; } = new();

        public List<string> Cc { get; set; } = new();

        public List<string> Bcc { get; set; } = new();

        /// <summary>
        /// The channels this target is enabled for. A channel absent from this list is not
        /// delivered; whether the provider could ever deliver it at all is the provider's own
        /// capability declaration.
        /// </summary>
        public List<NotificationChannel> Channels { get; set; } = new();

        public bool IsEnabled { get; set; } = true;
    }

    /// <summary>
    /// The save-time rules for an <see cref="EmailConfiguration"/>, kept beside the type the way
    /// Readarr keeps EmailSettingsValidator beside EmailSettings.
    /// </summary>
    /// <remarks>
    /// Rule for rule this is Readarr's EmailSettingsValidator
    /// (src/NzbDrone.Core/Notifications/Email/EmailSettings.cs): server not empty, port in range,
    /// from address not empty, every recipient a well-formed address, and at least one of To, Cc or
    /// Bcc populated.
    /// <para>
    /// It is a plain method rather than a FluentValidation validator because Listenarr does not
    /// take that dependency, and it returns reasons rather than throwing because the Test button is
    /// where they are shown, the same place
    /// <see cref="Listenarr.Domain.Notifications.NotificationChannel"/> targets report every other
    /// misconfiguration.
    /// </para>
    /// </remarks>
    public static class EmailConfigurationValidator
    {
        public static IReadOnlyList<string> Validate(EmailConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var failures = new List<string>();

            if (string.IsNullOrWhiteSpace(configuration.Server))
            {
                failures.Add("Server is required");
            }

            if (configuration.Port < 1 || configuration.Port > 65535)
            {
                failures.Add("Port must be between 1 and 65535");
            }

            if (string.IsNullOrWhiteSpace(configuration.From))
            {
                failures.Add("From address is required");
            }

            AddAddressFailures(failures, "Recipient", configuration.To);
            AddAddressFailures(failures, "CC", configuration.Cc);
            AddAddressFailures(failures, "BCC", configuration.Bcc);

            if (Recipients(configuration).Count == 0)
            {
                failures.Add("At least one recipient, CC or BCC address is required");
            }

            return failures;
        }

        /// <summary>Every address a message will actually be delivered to.</summary>
        public static IReadOnlyList<string> Recipients(EmailConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            return configuration.To
                .Concat(configuration.Cc)
                .Concat(configuration.Bcc)
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .ToList();
        }

        /// <summary>
        /// Matches FluentValidation's AspNetCoreCompatible email rule, which is what Readarr's
        /// RuleForEach(...).EmailAddress() resolves to: exactly one at-sign, with something on
        /// either side of it. Readarr's own fixture asserts this shape, rejecting "readarr" and
        /// "readarr.com" and nothing narrower.
        /// </summary>
        public static bool IsWellFormedAddress(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            var at = address.IndexOf('@', StringComparison.Ordinal);
            return at > 0
                   && at == address.LastIndexOf('@')
                   && at < address.Length - 1
                   && !address.Any(char.IsWhiteSpace);
        }

        private static void AddAddressFailures(List<string> failures, string label, IEnumerable<string> addresses)
        {
            foreach (var address in addresses.Where(candidate => !IsWellFormedAddress(candidate)))
            {
                failures.Add($"{label} address '{address}' is not a valid email address");
            }
        }
    }
}
