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

namespace Listenarr.Api.Features.Downloads;

public partial class ManualImportController
{
    private async Task WriteImportTagsBestEffortAsync(
        IAudiobookFileRegistrationLease registrationLease,
        Audiobook audiobook,
        string destinationPath)
    {
        // Artwork is worth writing for a book that has no ASIN. Anything matched
        // outside Audible is in that state, and gating the whole call on the ASIN
        // made the cover art setting silently inert for all of them.
        if (registrationLease.HasDurablePhysicalObjectIdentity
            && (!string.IsNullOrWhiteSpace(audiobook.Asin)
                || !string.IsNullOrWhiteSpace(audiobook.ImageUrl)))
        {
            try
            {
                await _metadataService.WriteImportTagsAsync(
                    registrationLease,
                    audiobook.Asin,
                    audiobook.ImageUrl);
            }
            catch (Exception exception) when (exception is not (
                OutOfMemoryException or StackOverflowException))
            {
                _logger.LogWarning(
                    exception,
                    "Manual import completed, but generation-bound tag enrichment failed for audiobook {AudiobookId} at {Path}",
                    audiobook.Id,
                    LogRedaction.SanitizeFilePath(destinationPath));
            }
        }
    }
}
