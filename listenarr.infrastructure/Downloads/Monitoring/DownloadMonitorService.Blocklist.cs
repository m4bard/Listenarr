/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Microsoft.Extensions.DependencyInjection;

namespace Listenarr.Infrastructure.Downloads.Monitoring
{
    /// <summary>
    /// The blocklist write on the failure path, and the reasoning for where it sits.
    ///
    /// Its own file because DownloadMonitorService.cs is within a couple of lines of the 500-line
    /// cap that BackendArchitectureTests enforces, and this argument is longer than the code it
    /// explains. AudiobookRepository.Blocklist.cs is the same split for the same reason.
    /// </summary>
    public partial class DownloadMonitorProcessor
    {
        private async Task BlockFailedReleaseAsync(
            IServiceScope scope,
            Download download,
            string errorMessage)
        {
            // Recording that a release failed sits beside recording the failure itself, above
            // the FailedDownloadHandlingEnabled gate, and it is deliberately not gated on any
            // setting.
            //
            // The earlier arrangement had the write below the gate and the search-side filter
            // above it, which left the setting half-applied and silently misleading: an operator
            // who switched failed-download handling off stopped accruing rows and went on being
            // blocked by every row already written, with no UI to see them. Gating both sides was
            // the other way to make that coherent. This one is better because gating the read
            // needs a second concept, rows that exist but are ignored, and leaves the operator
            // with a list that does nothing.
            //
            // And the lever the setting is for survives: Listenarr's analogue of Readarr's
            // AutoRedownloadFailed is FailedDownloadAutoSearch, and it still gates the auto-search
            // below this write. What an operator loses is not "stop chasing my failures", it is
            // "stop recording which release failed", which is bookkeeping rather than handling.
            //
            // The family corroborates rather than decides it, because Listenarr has a setting the
            // family never had and silence about one is not a decision. For the record, neither
            // Readarr nor Sonarr gates a blocklist on anything: the write is unconditional in
            // BlocklistService.Handle(DownloadFailedEvent) (Readarr
            // src/NzbDrone.Core/Blocklisting/BlocklistService.cs:174-197) and the read is
            // unconditional in BlocklistSpecification
            // (src/NzbDrone.Core/DecisionEngine/Specifications/BlocklistSpecification.cs:22-31).
            // Both have two failure-related settings, AutoRedownloadFailed and
            // AutoRedownloadFailedFromInteractiveSearch (Readarr ConfigService.cs:140-145 and
            // :147-152), and both gate the re-download alone
            // (RedownloadFailedDownloadService.cs:39 and :45; Sonarr's are :40 and :46).
            //
            // The behaviour change this makes: an operator with failed-download handling off now
            // accrues rows where before they did not. The way out of a wrong one is the delete and
            // clear endpoints, not a setting that suppresses the list while leaving it populated.
            //
            // Only downloads the client accepted and then failed reach this method. A
            // release the client refused at submission never gets here, which is what keeps
            // a qBittorrent 409 out of the blocklist: that answer means the client already
            // holds the release, so blocking it would ban something the user is currently
            // downloading. The carve-out is structural rather than a condition to remember.
            if (download.AudiobookId.HasValue)
            {
                var blocklistService = scope.ServiceProvider.GetRequiredService<IBlocklistService>();
                // Read back the identity stamped on the download when it was grabbed. This method
                // must not work one out for itself: by the time a download fails, its TotalSize
                // has been overwritten from the client's queue snapshot and its OriginalUrl may be
                // a spent per-fetch link, so anything derived here disagrees with what the search
                // side derives from the indexer's listing and the row never matches. A live
                // install wrote one correctly formatted row after the first failure and then
                // grabbed the identical release more than a hundred times over the next eleven
                // hours.
                var identifier = ReleaseIdentity.ForGrabbed(download);
                if (identifier is not null)
                {
                    await blocklistService.BlockAsync(
                        download.AudiobookId.Value,
                        identifier,
                        // The title as advertised, and never a placeholder standing in for it.
                        // Title is a lookup key now, so a literal like "Unknown" would be a key
                        // that every untitled failure shares; an empty column contributes nothing
                        // and the key's own title still carries the match.
                        download.Title,
                        ReleaseIdentity.SizeForGrabbed(download),
                        errorMessage);
                }
            }
        }
    }
}
