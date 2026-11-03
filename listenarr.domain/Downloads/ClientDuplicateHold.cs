/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Domain.Downloads
{
    /// <summary>
    /// A grab the download client accepted and then skipped as a duplicate of something already in
    /// its queue or history, such as NZBGet's duplicate check deleting it as DELETED/COPY.
    ///
    /// This is not a failure: the release is not broken, so it is never blocklisted, never
    /// records DownloadFailed and never sends the failure notification. It is not a completion
    /// either, because nothing new was downloaded and there is nothing at the client's path to
    /// import. The download is held as
    /// <see cref="DownloadStatus.ImportBlocked" /> with this reason, which is terminal, is not
    /// polled, is skipped by orphan cleanup, and stays visible until the user clears it.
    ///
    /// While it is held, searches that grab on their own skip the identical release for that book,
    /// because grabbing it again only gets the same answer from the client. Removing the download
    /// lifts that, so nothing is banned permanently, though the client keeps answering the same
    /// way until its own matching entry is gone. Retry-import refuses a hold, since there is
    /// nothing to import and unblocking would drop the hold.
    /// </summary>
    public static class ClientDuplicateHold
    {
        public const string Reason = "Download client skipped it as a duplicate";

        /// <summary>
        /// The queue item status an adapter reports for such a grab. QueueItemConverter turns it
        /// into the hold.
        /// </summary>
        public const string QueueStatus = "duplicate";

        public static bool Holds(Download download)
        {
            return download.Status == DownloadStatus.ImportBlocked &&
                string.Equals(download.ImportBlockReason, Reason, StringComparison.Ordinal);
        }

        /// <summary>
        /// The release identities of this book's downloads currently held as client duplicates.
        /// </summary>
        public static IReadOnlyCollection<string> HeldReleaseIdentifiers(IEnumerable<Download> downloads)
        {
            return downloads
                .Where(Holds)
                .Select(ReleaseIdentity.ForGrabbed)
                .OfType<string>()
                .ToList();
        }
    }
}
