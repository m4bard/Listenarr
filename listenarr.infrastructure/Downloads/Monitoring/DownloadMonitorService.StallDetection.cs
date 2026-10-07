/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Downloads.Monitoring
{
    /// <summary>
    /// Stalled-download handling: a torrent the client keeps reporting as downloading, with no
    /// change in progress or bytes for StalledDownloadTimeoutHours, is marked Failed. The monitor
    /// then persists it and runs the ordinary failure path (OnDownloadFailed), which blocklists the
    /// release, removes it from the client and leaves the book free to be searched for again.
    /// There is deliberately no second removal path here.
    ///
    /// Kept beside the monitor rather than inside it because the monitor file is already at the
    /// size the architecture tests cap production files at.
    /// </summary>
    public partial class DownloadMonitorProcessor
    {
        // Persisted in Download.Metadata, so the clock survives a restart without a column. Values
        // are invariant strings: metadata read back from the database arrives as JsonElement, and
        // GetMetadataString reads both that and a string the same way.
        internal const string StallLastDownloadedSizeKey = "StallLastDownloadedSize";
        internal const string StallLastProgressKey = "StallLastProgress";
        internal const string StallProgressChangedAtKey = "StallProgressChangedAt";
        internal const string StallLastObservedAtKey = "StallLastObservedAt";

        // The floor's own window, tracked apart from the four keys above. Those four reset on
        // ANY observed byte or progress movement, which is the gap the floor exists to close: a
        // download trickling a few bytes a poll resets them every time and never reaches a
        // standstill long enough to fail by that check alone. The floor's window survives that
        // per-poll reset and only restarts for the same "fresh start" reasons the 8-signal check
        // itself restarts for (no prior observation, a gap in observation, clock skew) -- never
        // for movement on its own.
        internal const string StallWindowStartedAtKey = "StallWindowStartedAt";
        internal const string StallWindowStartedSizeKey = "StallWindowStartedSize";

        private const string ClientStateKey = "ClientState";

        // Only time actually watched counts towards a stall. A longer gap between two observations
        // (Listenarr down, the client unreachable, the poll backing off, which tops out at 900s)
        // restarts the clock rather than counting hours nobody saw.
        private static readonly TimeSpan StallObservationGapCap = TimeSpan.FromMinutes(30);

        // Mirrors DownloadClientSelector's torrent client list. Usenet clients report their own
        // failures, and a usenet item that stops moving is a server or provider outage rather than
        // a dead release, so blocklisting it would be wrong.
        private static readonly string[] StallCheckedClientTypes = ["qbittorrent", "transmission"];

        // Client states that map to Downloading while the client is busy with data it already has
        // rather than fetching any. qBittorrent's checkingDL and moving, and Transmission's
        // verifying, currently reach QueueItemConverter already normalized to "downloading" by
        // their adapters, so this only takes effect for a raw state that gets through. A check or
        // a move is minutes against a timeout measured in hours, which is what bounds the gap.
        private static readonly HashSet<string> StallBusyClientStates = new(StringComparer.OrdinalIgnoreCase)
        {
            "checkingdl",
            "checkingup",
            "checkingresumedata",
            "moving"
        };

        /// <summary>
        /// Poll the client for <paramref name="downloads"/> and then judge each one it actually
        /// reported for a stall.
        /// </summary>
        /// <remarks>
        /// The gateway hands back every download it was given, whether or not the client listed
        /// it, and one it did not list keeps last poll's bytes, which is indistinguishable from a
        /// stall. QueueItemConverter writes ClientState on every item it maps, so the key is taken
        /// off before the poll: present afterwards means reported this poll. Anything not reported
        /// gets its old value back and is left out of the judgement entirely, which leaves a torrent
        /// that has vanished from the client to orphan cleanup.
        /// </remarks>
        private async Task<List<Download>> FetchDownloadsAndJudgeStallsAsync(
            IDownloadClientGateway downloadClientGateway,
            DownloadClientConfiguration client,
            List<Download> downloads,
            ApplicationSettings settings,
            CancellationToken cancellationToken)
        {
            var clientStatesBeforePoll = new Dictionary<Download, object?>(ReferenceEqualityComparer.Instance);
            foreach (var download in downloads)
            {
                download.Metadata.Remove(ClientStateKey, out var clientState);
                clientStatesBeforePoll[download] = clientState;
            }

            var reported = new HashSet<Download>(ReferenceEqualityComparer.Instance);
            List<Download> updatedDownloads;
            try
            {
                updatedDownloads = await downloadClientGateway.FetchDownloadsAsync(client, downloads, cancellationToken);
            }
            finally
            {
                foreach (var (download, clientState) in clientStatesBeforePoll)
                {
                    if (download.Metadata.ContainsKey(ClientStateKey))
                    {
                        reported.Add(download);
                    }
                    else if (clientState is not null)
                    {
                        download.Metadata[ClientStateKey] = clientState;
                    }
                }
            }

            foreach (var download in updatedDownloads.Where(reported.Contains))
            {
                JudgeStall(client, download, settings);
            }

            return updatedDownloads;
        }

        private void JudgeStall(DownloadClientConfiguration client, Download download, ApplicationSettings settings)
        {
            // Everything failure-related hangs off failed-download handling: an operator who has
            // turned it off has said to leave failures alone, and a stall is handled as one.
            var timeoutHours = settings.FailedDownloadHandlingEnabled
                ? Math.Min(settings.StalledDownloadTimeoutHours, ApplicationSettings.MaxStalledDownloadTimeoutHours)
                : 0;

            // Not being watched for a stall clears the clock, so a torrent paused or queued for a
            // week is not failed the moment it resumes.
            if (timeoutHours <= 0 || !IsStallCheckedClient(client) || !IsStallEligible(download))
            {
                ClearStallMarkers(download);
                return;
            }

            var now = timeProvider.GetUtcNow();
            var lastObservedAt = ReadStallTime(download, StallLastObservedAtKey);
            var progressChangedAt = ReadStallTime(download, StallProgressChangedAtKey);

            // Compared as numbers, not text: a decimal read back from the database can carry a
            // different scale ("25.0" against "25") for the same value.
            var hasLastDownloadedSize = long.TryParse(
                download.GetMetadataString(StallLastDownloadedSizeKey),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var lastDownloadedSize);
            var hasLastProgress = decimal.TryParse(
                download.GetMetadataString(StallLastProgressKey),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var lastProgress);

            download.SetMetadata(StallLastObservedAtKey, FormatStallTime(now));

            // The 8-signal check, kept exactly as it was: a first observation starts the clock
            // now rather than at StartedAt (a torrent queued or paused since it was grabbed would
            // otherwise be failed the moment it started), a gap in observation restarts it, and
            // so does any movement in either direction (a recheck can take bytes away).
            //
            // Split into "fresh start" (nothing trustworthy to compare against yet) and
            // "activity" (a real per-poll change) only so the floor below can tell them apart --
            // the combined condition and what it does when true are unchanged.
            var isFreshStart = lastObservedAt is null
                || progressChangedAt is null
                || now < lastObservedAt
                || now - lastObservedAt.Value > StallObservationGapCapFor(client)
                || !hasLastDownloadedSize
                || !hasLastProgress;
            var hasActivity = !isFreshStart
                && (lastDownloadedSize != download.DownloadedSize || lastProgress != download.Progress);

            if (isFreshStart || hasActivity)
            {
                // The floor: a second, independent trigger. The 8-signal check above is about to
                // reset its own clock because it saw per-poll activity -- exactly the case a
                // byte-only trickle hits on every single poll, so the 8-signal check alone would
                // never fail it no matter how long it ran. Only reachable on real activity, never
                // on a fresh start (there is no prior window yet to judge).
                var floorPercent = Math.Min(settings.StalledDownloadFloorPercent, ApplicationSettings.MaxStalledDownloadFloorPercent);
                if (hasActivity && floorPercent > 0 && download.TotalSize > 0
                    && TryFailBelowFloor(download, now, timeoutHours, floorPercent))
                {
                    return;
                }

                if (isFreshStart && floorPercent > 0 && download.TotalSize > 0)
                {
                    StartFloorWindow(download, now);
                }

                download.SetMetadata(StallLastDownloadedSizeKey, download.DownloadedSize.ToString(CultureInfo.InvariantCulture));
                download.SetMetadata(StallLastProgressKey, download.Progress.ToString(CultureInfo.InvariantCulture));
                download.SetMetadata(StallProgressChangedAtKey, FormatStallTime(now));
                return;
            }

            // Neither a fresh start nor any per-poll activity: a genuine standstill, judged by
            // the 8-signal check alone, unaffected by the floor above (which is never reached
            // from here).
            if (now - progressChangedAt.Value < TimeSpan.FromHours(timeoutHours))
            {
                return;
            }

            ClearStallMarkers(download);
            download.Failed(
                $"No download progress for {timeoutHours} hour{(timeoutHours == 1 ? string.Empty : "s")}; treated as a failed download (stalled download handling)");
            logger.LogInformation(
                "Download {DownloadId} has made no progress for {Hours}h; failing it as stalled",
                LogRedaction.SanitizeText(download.Id),
                timeoutHours);
        }

        /// <summary>
        /// The floor itself: has this download moved at least <paramref name="floorPercent"/> of
        /// its TotalSize since its tracking window started? If the window has not yet run for a
        /// full <paramref name="timeoutHours"/>, there is nothing to judge yet. If it has and the
        /// movement cleared the floor, the window slides forward from now rather than letting one
        /// good burst of bytes buy unlimited future trickling. If it has not, the download is
        /// failed the same way the 8-signal check fails a standstill, with its own message.
        /// </summary>
        private bool TryFailBelowFloor(Download download, DateTimeOffset now, int timeoutHours, decimal floorPercent)
        {
            var windowStartedAt = ReadStallTime(download, StallWindowStartedAtKey);
            var hasWindowStartedSize = long.TryParse(
                download.GetMetadataString(StallWindowStartedSizeKey),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var windowStartedSize);

            if (windowStartedAt is null || !hasWindowStartedSize || now < windowStartedAt.Value)
            {
                StartFloorWindow(download, now);
                return false;
            }

            if (now - windowStartedAt.Value < TimeSpan.FromHours(timeoutHours))
            {
                return false;
            }

            var moved = download.DownloadedSize - windowStartedSize;
            var movedPercent = moved <= 0 ? 0m : (decimal)moved / download.TotalSize * 100m;
            if (movedPercent >= floorPercent)
            {
                StartFloorWindow(download, now);
                return false;
            }

            ClearStallMarkers(download);
            download.Failed(
                $"Less than {floorPercent}% of its size moved in {timeoutHours} hour{(timeoutHours == 1 ? string.Empty : "s")}; treated as a failed download (stalled download handling)");
            logger.LogInformation(
                "Download {DownloadId} moved less than {FloorPercent}% of its total size in {Hours}h; failing it as stalled (floor)",
                LogRedaction.SanitizeText(download.Id),
                floorPercent,
                timeoutHours);
            return true;
        }

        private static void StartFloorWindow(Download download, DateTimeOffset now)
        {
            download.SetMetadata(StallWindowStartedAtKey, FormatStallTime(now));
            download.SetMetadata(StallWindowStartedSizeKey, download.DownloadedSize.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The longest gap between two observations that still counts as continuous watching. At
        /// least <see cref="StallObservationGapCap"/>, and longer when polling is configured slower
        /// than that, or a long interval would restart the clock on every poll and never fire.
        /// </summary>
        private TimeSpan StallObservationGapCapFor(DownloadClientConfiguration client)
        {
            var pollSeconds = Math.Max(client.GetPollingInterval(_pollingInterval), _pollingInterval);
            var twoPolls = TimeSpan.FromSeconds(2.0 * pollSeconds);
            return twoPolls > StallObservationGapCap ? twoPolls : StallObservationGapCap;
        }

        internal static bool IsStallCheckedClient(DownloadClientConfiguration client) =>
            StallCheckedClientTypes.Contains(client.Type, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Is this a download the client is trying to fetch and has not finished?
        /// </summary>
        /// <remarks>
        /// uploading, stalledUP, forcedUP and seeding also map to Downloading, so completeness is
        /// checked on its own: by progress, and by bytes when the size is known.
        /// </remarks>
        internal static bool IsStallEligible(Download download)
        {
            if (download.Status != DownloadStatus.Downloading
                || download.Progress >= 100
                || (download.TotalSize > 0 && download.DownloadedSize >= download.TotalSize))
            {
                return false;
            }

            var clientState = download.GetMetadataString(ClientStateKey);
            return clientState is null || !StallBusyClientStates.Contains(clientState);
        }

        private static void ClearStallMarkers(Download download)
        {
            download.Metadata.Remove(StallLastDownloadedSizeKey);
            download.Metadata.Remove(StallLastProgressKey);
            download.Metadata.Remove(StallProgressChangedAtKey);
            download.Metadata.Remove(StallLastObservedAtKey);
            download.Metadata.Remove(StallWindowStartedAtKey);
            download.Metadata.Remove(StallWindowStartedSizeKey);
        }

        private static string FormatStallTime(DateTimeOffset value) =>
            value.ToString("O", CultureInfo.InvariantCulture);

        private static DateTimeOffset? ReadStallTime(Download download, string key) =>
            DateTimeOffset.TryParse(
                download.GetMetadataString(key),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var value)
                ? value
                : null;
    }
}
