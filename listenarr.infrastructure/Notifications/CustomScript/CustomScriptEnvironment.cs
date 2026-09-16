/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Globalization;
using Listenarr.Domain.Notifications;

namespace Listenarr.Infrastructure.Notifications.CustomScript
{
    /// <summary>
    /// Turns a <see cref="NotificationEvent"/> into the environment variables a custom script
    /// receives.
    /// </summary>
    /// <remarks>
    /// The naming follows the *arr family so that a script carried over from Sonarr, Radarr or
    /// Readarr recognises what it is given. The conventions, taken from those implementations:
    /// <list type="bullet">
    /// <item><c>Listenarr_EventType</c> is set on every event, including the test, and is the value
    /// a script is expected to switch on.</item>
    /// <item><c>Listenarr_InstanceName</c> and <c>Listenarr_ApplicationUrl</c> are also set on every
    /// event, as Sonarr, Radarr and Prowlarr do.</item>
    /// <item>Multi-valued text and paths are joined with a pipe; numbers and dates with a comma.</item>
    /// <item>A variable whose value is unknown is set to the empty string rather than omitted, so a
    /// script can read it unconditionally.</item>
    /// </list>
    /// </remarks>
    public static class CustomScriptEnvironment
    {
        public const string Prefix = "Listenarr_";

        /// <summary>Separator for lists of text and paths.</summary>
        public const string TextSeparator = "|";

        /// <summary>Separator for lists of numbers and dates.</summary>
        public const string NumericSeparator = ",";

        /// <summary>
        /// Builds the variables for one event.
        /// </summary>
        /// <param name="notification">The event being delivered.</param>
        /// <param name="instanceName">This Listenarr instance's name, for scripts serving several.</param>
        /// <param name="applicationUrl">This instance's absolute base URL, or null when not configured.</param>
        public static IReadOnlyDictionary<string, string> Build(
            NotificationEvent notification,
            string? instanceName,
            string? applicationUrl)
        {
            ArgumentNullException.ThrowIfNull(notification);

            var variables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Prefix + "EventType"] = notification.Channel.ToString(),
                [Prefix + "InstanceName"] = instanceName ?? string.Empty,
                [Prefix + "ApplicationUrl"] = AbsoluteOrEmpty(applicationUrl),
            };

            AddBook(variables, notification.Book);
            AddRelease(variables, notification.Release);
            AddDownload(variables, notification.Download);
            AddPaths(variables, notification);

            variables[Prefix + "Message"] = notification.Message ?? string.Empty;
            variables[Prefix + "Timestamp"] =
                notification.OccurredAtUtc.ToString("o", CultureInfo.InvariantCulture);

            return variables;
        }

        /// <summary>
        /// Builds the variables for the connectivity check. Sonarr sets exactly these three, and a
        /// script is expected to handle <c>EventType=Test</c> without side effects.
        /// </summary>
        public static IReadOnlyDictionary<string, string> BuildTest(
            string? instanceName,
            string? applicationUrl) =>
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Prefix + "EventType"] = NotificationChannel.Test.ToString(),
                [Prefix + "InstanceName"] = instanceName ?? string.Empty,
                [Prefix + "ApplicationUrl"] = AbsoluteOrEmpty(applicationUrl),
            };

        private static void AddBook(IDictionary<string, string> variables, NotificationEventBook? book)
        {
            variables[Prefix + "Book_Id"] = book?.Id.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            variables[Prefix + "Book_Title"] = book?.Title ?? string.Empty;
            variables[Prefix + "Book_Asin"] = book?.Asin ?? string.Empty;
            variables[Prefix + "Book_Authors"] = JoinText(book?.Authors);
            variables[Prefix + "Book_Narrators"] = JoinText(book?.Narrators);
            variables[Prefix + "Book_Publisher"] = book?.Publisher ?? string.Empty;
            variables[Prefix + "Book_Year"] = book?.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static void AddRelease(IDictionary<string, string> variables, NotificationEventRelease? release)
        {
            variables[Prefix + "Release_Title"] = release?.Title ?? string.Empty;
            variables[Prefix + "Release_Indexer"] = release?.Indexer ?? string.Empty;
            variables[Prefix + "Release_Size"] = release?.Size?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            variables[Prefix + "Release_Quality"] = release?.Quality ?? string.Empty;
            variables[Prefix + "Release_Protocol"] = release?.Protocol ?? string.Empty;
        }

        private static void AddDownload(IDictionary<string, string> variables, NotificationEventDownload? download)
        {
            variables[Prefix + "Download_Id"] = download?.Id ?? string.Empty;
            variables[Prefix + "Download_Client"] = download?.Client ?? string.Empty;
            variables[Prefix + "Download_Client_Type"] = download?.ClientType ?? string.Empty;
            variables[Prefix + "Download_ErrorMessage"] = download?.ErrorMessage ?? string.Empty;
        }

        private static void AddPaths(IDictionary<string, string> variables, NotificationEvent notification)
        {
            variables[Prefix + "AddedBookPaths"] = JoinText(notification.AddedPaths);
            variables[Prefix + "SourcePath"] = notification.SourcePath ?? string.Empty;
            variables[Prefix + "DestinationPath"] = notification.DestinationPath ?? string.Empty;
        }

        private static string JoinText(IReadOnlyList<string>? values) =>
            values == null || values.Count == 0
                ? string.Empty
                : string.Join(TextSeparator, values.Where(value => !string.IsNullOrWhiteSpace(value)));

        // UrlBase defaults to "/" on a fresh install, and a relative value is no use to a script
        // that wants to call back into the API. Give it nothing rather than something unusable.
        private static string AbsoluteOrEmpty(string? applicationUrl) =>
            !string.IsNullOrWhiteSpace(applicationUrl)
            && Uri.TryCreate(applicationUrl, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
                ? applicationUrl
                : string.Empty;
    }
}
