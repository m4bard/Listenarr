/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Text.RegularExpressions;
using Listenarr.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Notifications.Email
{
    /// <summary>
    /// Sends an email through an operator-supplied SMTP server on notification events.
    /// </summary>
    /// <remarks>
    /// This is the provider that does not fit the webhook model: SMTP is not HTTP, so there is no
    /// payload shape that turns it into another webhook type. It is registered as an
    /// <see cref="INotificationSubscriber"/> alongside
    /// <c>CustomScriptNotification</c> and reaches the network only through
    /// <see cref="ISmtpTransport"/>.
    /// <para>
    /// The operator-visible name is "Email", matching Sonarr, Prowlarr and Readarr, because that is
    /// what someone will look for in the settings list.
    /// </para>
    /// </remarks>
    public sealed class EmailNotification : INotificationSubscriber
    {
        private readonly IConfigurationService _configurationService;
        private readonly ISmtpTransport _transport;
        private readonly ILogger<EmailNotification> _logger;

        public EmailNotification(
            IConfigurationService configurationService,
            ISmtpTransport transport,
            ILogger<EmailNotification> logger)
        {
            _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => "Email";

        // An email can carry any event, so there is no deliverable channel this cannot handle. Test
        // is excluded because it is not a delivered event; it is reached through TestAsync.
        public bool Supports(NotificationChannel channel) => channel != NotificationChannel.Test;

        public async Task NotifyAsync(NotificationEvent notification, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(notification);

            if (!Supports(notification.Channel))
            {
                return;
            }

            List<EmailConfiguration> configurations;
            try
            {
                configurations = await _configurationService.GetEmailConfigurationsAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Could not read email configuration; no email will be sent for {Channel}", notification.Channel);
                return;
            }

            var subject = EmailMessageBuilder.Subject(notification.Channel);
            var body = EmailMessageBuilder.Body(notification);

            foreach (var configuration in configurations.Where(candidate =>
                candidate.IsEnabled && candidate.Channels.Contains(notification.Channel)))
            {
                await SendQuietlyAsync(configuration, subject, body, cancellationToken);
            }
        }

        public async Task<NotificationSubscriberTestResult> TestAsync(string configurationId, CancellationToken cancellationToken = default)
        {
            var configurations = await _configurationService.GetEmailConfigurationsAsync();
            var configuration = configurations.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, configurationId, StringComparison.Ordinal));

            if (configuration == null)
            {
                return NotificationSubscriberTestResult.Failure($"No email notification is configured with id '{configurationId}'");
            }

            var failures = EmailConfigurationValidator.Validate(configuration);
            if (failures.Count > 0)
            {
                return NotificationSubscriberTestResult.Failure(failures.ToArray());
            }

            try
            {
                // A real send, not a dry run. A Test button that only validated the form would
                // report success on a server that rejects the login, which is the one thing an
                // operator presses it to rule out.
                await SendAsync(
                    configuration,
                    EmailMessageBuilder.Subject(NotificationChannel.Test),
                    EmailMessageBuilder.TestBody(),
                    cancellationToken);

                return NotificationSubscriberTestResult.Success();
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                LogSendFailure(configuration, ex);
                return NotificationSubscriberTestResult.Failure(Scrub(ex.Message, configuration.Password));
            }
        }

        private async Task SendQuietlyAsync(
            EmailConfiguration configuration,
            string subject,
            string body,
            CancellationToken cancellationToken)
        {
            var failures = EmailConfigurationValidator.Validate(configuration);
            if (failures.Count > 0)
            {
                _logger.LogWarning(
                    "Skipping email notification {EmailName}: {Reason}",
                    configuration.Name,
                    string.Join("; ", failures));
                return;
            }

            try
            {
                await SendAsync(configuration, subject, body, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            // A failing notification target must never break the operation that produced the event.
#pragma warning disable CA1031
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                LogSendFailure(configuration, ex);
            }
#pragma warning restore CA1031
        }

        private async Task SendAsync(
            EmailConfiguration configuration,
            string subject,
            string body,
            CancellationToken cancellationToken)
        {
            var server = new SmtpServer
            {
                Host = configuration.Server,
                Port = configuration.Port,
                RequireEncryption = configuration.RequireEncryption,
                Username = configuration.Username,
                Password = configuration.Password,
            };

            var message = new SmtpMessage
            {
                From = configuration.From,
                To = configuration.To.Where(address => !string.IsNullOrWhiteSpace(address)).ToList(),
                Cc = configuration.Cc.Where(address => !string.IsNullOrWhiteSpace(address)).ToList(),
                Bcc = configuration.Bcc.Where(address => !string.IsNullOrWhiteSpace(address)).ToList(),
                Subject = subject,
                Body = body,
            };

            // Server and port are logged; username, password and recipients are not. An operator
            // debugging delivery needs to know which server was tried, and nothing here needs the
            // credential to be legible.
            _logger.LogDebug(
                "Sending email notification {EmailName} to {Server}:{Port}, subject {Subject}",
                configuration.Name,
                configuration.Server,
                configuration.Port,
                subject);

            await _transport.SendAsync(server, message, cancellationToken);

            _logger.LogDebug("Sent email notification {EmailName}", configuration.Name);
        }

        /// <summary>
        /// Logs a send failure with the password taken back out of it.
        /// </summary>
        /// <remarks>
        /// The exception is not passed to the logger as an exception object, because a rejected
        /// login can carry the attempted credential in the server's reply, and an exception's
        /// message and stack are rendered verbatim by every sink. The text goes through
        /// <c>LogRedaction.RedactText</c> with the configured password as an explicit secret, the
        /// same way DiscordBotService passes its bot token.
        /// </remarks>
        private void LogSendFailure(EmailConfiguration configuration, Exception ex)
        {
            _logger.LogError(
                "Email notification {EmailName} could not be sent to {Server}:{Port}: {Reason}",
                configuration.Name,
                configuration.Server,
                configuration.Port,
                Scrub(ex.Message, configuration.Password));
        }

        /// <summary>
        /// Removes the configured password, and anything sensitive in the environment, from text
        /// that is about to be logged or shown to the operator.
        /// </summary>
        /// <remarks>
        /// The matching rule is the one LogRedaction.RedactText applies, an escaped
        /// case-insensitive replacement, but the replacement is done here rather than by calling
        /// it. RedactText appends a trailing marker whenever it was given a secret and did not
        /// find it in the text. That is defensible for a log line and wrong for a string an
        /// operator reads: a bad certificate would come back as "The remote certificate is
        /// invalid. &lt;redacted&gt;", which says something was withheld when nothing was.
        /// <para>
        /// A short password over-redacts, so a password of "smtp" would take the word out of a
        /// hostname in the message. That is the safe direction to be wrong in and it is left
        /// alone; a length threshold would be a guess at where a real password starts.
        /// </para>
        /// </remarks>
        private static string Scrub(string? text, string? password)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var secrets = LogRedaction.GetSensitiveValuesFromEnvironment()
                .Concat(new[] { password })
                .Where(secret => !string.IsNullOrEmpty(secret))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var scrubbed = text;
            foreach (var secret in secrets)
            {
                scrubbed = Regex.Replace(scrubbed, Regex.Escape(secret!), "<redacted>", RegexOptions.IgnoreCase);
            }

            return scrubbed;
        }
    }
}
