/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */

namespace Listenarr.Domain.Notifications
{
    /// <summary>
    /// The vocabulary of notification topics. Every publisher and every subscriber names an event
    /// with a value from this enum, so a topic cannot be spelled two ways.
    /// </summary>
    /// <remarks>
    /// Names follow the *arr family so that a script or integration carried over from Sonarr,
    /// Radarr or Readarr recognises them. <see cref="Download"/> rather than "Imported" is the
    /// family name for a completed import, and <see cref="Grab"/> rather than "Downloading" is the
    /// family name for a release handed to a download client.
    /// </remarks>
    public enum NotificationChannel
    {
        /// <summary>Operator-initiated connectivity check. Not fired by any real event.</summary>
        Test = 0,

        /// <summary>A release was accepted and sent to a download client.</summary>
        Grab = 1,

        /// <summary>A download completed and its files were imported into the library.</summary>
        Download = 2,

        /// <summary>A download failed and will not be imported.</summary>
        DownloadFailed = 3,

        /// <summary>An audiobook was added to the library.</summary>
        BookAdded = 4,

        /// <summary>A library scan found files for a monitored audiobook that Listenarr did not import.</summary>
        BookAvailable = 5,

        /// <summary>Files belonging to an audiobook were relocated on disk.</summary>
        Rename = 6,
    }
}
