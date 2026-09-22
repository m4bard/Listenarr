/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Globalization;
using System.Text;
using Listenarr.Domain.Notifications;

namespace Listenarr.Infrastructure.Notifications.Email
{
    /// <summary>
    /// Turns a published <see cref="NotificationEvent"/> into a subject and a plain-text body.
    /// </summary>
    /// <remarks>
    /// This is the email counterpart of <c>CustomScriptEnvironment</c>: the one place that decides
    /// what a subscriber makes of an event, kept out of the provider so that the wording can be
    /// asserted without a transport.
    /// <para>
    /// Subjects follow the branded pattern Readarr uses in NotificationBase
    /// (src/NzbDrone.Core/Notifications/NotificationBase.cs, the *_TITLE_BRANDED constants):
    /// the application name, a separator, and a short event name. Two of Listenarr's channels have
    /// no Readarr counterpart, BookAvailable and Rename, and are named from
    /// <see cref="NotificationChannel"/>'s own documentation rather than invented against Readarr's
    /// list.
    /// </para>
    /// </remarks>
    internal static class EmailMessageBuilder
    {
        internal const string SubjectPrefix = "Listenarr - ";

        internal static string Subject(NotificationChannel channel) => SubjectPrefix + channel switch
        {
            NotificationChannel.Grab => "Book Grabbed",
            NotificationChannel.Download => "Book Downloaded",
            NotificationChannel.DownloadFailed => "Download Failed",
            NotificationChannel.BookAdded => "Book Added",
            NotificationChannel.BookAvailable => "Book Available",
            NotificationChannel.Rename => "Book Renamed",
            NotificationChannel.Test => "Test Notification",
            _ => channel.ToString(),
        };

        internal static string Body(NotificationEvent notification)
        {
            ArgumentNullException.ThrowIfNull(notification);

            var body = new StringBuilder();
            body.AppendLine(Summary(notification));

            var book = notification.Book;
            if (book != null)
            {
                body.AppendLine();
                AppendField(body, "Title", book.Title);
                AppendField(body, "Author", string.Join(", ", book.Authors));
                AppendField(body, "Narrator", string.Join(", ", book.Narrators));
                AppendField(body, "ASIN", book.Asin);
                AppendField(body, "Publisher", book.Publisher);
                AppendField(body, "Year", book.Year?.ToString(CultureInfo.InvariantCulture));
            }

            var release = notification.Release;
            if (release != null)
            {
                body.AppendLine();
                AppendField(body, "Release", release.Title);
                AppendField(body, "Indexer", release.Indexer);
                AppendField(body, "Quality", release.Quality);
                AppendField(body, "Protocol", release.Protocol);
            }

            AppendField(body, "Error", notification.Download?.ErrorMessage);
            AppendField(body, "Moved from", notification.SourcePath);
            AppendField(body, "Moved to", notification.DestinationPath);

            if (notification.AddedPaths.Count > 0)
            {
                body.AppendLine();
                body.AppendLine("Files added:");
                foreach (var path in notification.AddedPaths)
                {
                    body.AppendLine("  " + path);
                }
            }

            if (!string.IsNullOrWhiteSpace(notification.Message))
            {
                body.AppendLine();
                body.AppendLine(notification.Message);
            }

            return body.ToString().TrimEnd();
        }

        /// <summary>
        /// The body of the message the Test button sends. It says plainly that a real event will
        /// look different, because a test that reads like a delivery is the one an operator later
        /// mistakes for one.
        /// </summary>
        internal static string TestBody() =>
            "Listenarr sent this to check the settings for this email notification." + Environment.NewLine
            + "Nothing was downloaded, imported or renamed. A real notification carries the "
            + "audiobook it is about.";

        private static string Summary(NotificationEvent notification)
        {
            var title = notification.Book?.Title;
            var subject = string.IsNullOrWhiteSpace(title) ? "An audiobook" : title;

            return notification.Channel switch
            {
                NotificationChannel.Grab => $"{subject} was sent to a download client.",
                NotificationChannel.Download => $"{subject} finished downloading and was imported.",
                NotificationChannel.DownloadFailed => $"{subject} failed to download and was not imported.",
                NotificationChannel.BookAdded => $"{subject} was added to the library.",
                NotificationChannel.BookAvailable => $"Files for {subject} were found but not imported.",
                NotificationChannel.Rename => $"Files for {subject} were moved on disk.",
                _ => $"{subject}: {notification.Channel}.",
            };
        }

        private static void AppendField(StringBuilder body, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                body.AppendLine($"{label}: {value}");
            }
        }
    }
}
