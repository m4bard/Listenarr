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
    /// <summary>
    /// Retires the destination generation this import published when registration
    /// refused it, so that a refused manual import leaves the library folder as it
    /// was found rather than holding a file no catalog row points at.
    /// </summary>
    /// <remarks>
    /// The removal is deliberately narrow, because the target sits inside a user's
    /// library folder and the cost of getting it wrong is data loss rather than a
    /// failed import. It runs only when all of the following hold:
    /// the destination pathname was proven free when this import reserved it, so
    /// the file there was written by this import and not adopted from disk;
    /// the catalog held no audiobook file at that destination;
    /// and the registration lease still resolves the visible destination to the
    /// exact generation the preparation step published.
    /// Any one of those failing leaves the file alone and logs why.
    ///
    /// A refusal that happens before a lease is issued is deliberately not handled
    /// here. Without a lease there is no way to tell a destination this import
    /// published from an unrelated file that appeared at the same pathname, and
    /// guessing would trade a stray file for a deleted one.
    /// </remarks>
    private void RemoveRefusedRegistrationPublication(
        Audiobook audiobook,
        IAudiobookFileRegistrationLease registrationLease,
        ManualImportDestinationReservation destinationReservation,
        AudiobookFileOwnershipCheckResult ownership,
        string destinationPath,
        IReadOnlyList<string> allowedDestinationRoots)
    {
        if (destinationReservation.ReusesExistingFile)
        {
            _logger.LogInformation(
                "Left the refused manual-import destination in place for audiobook {AudiobookId} because it existed before this import: {Path}",
                audiobook.Id,
                LogRedaction.SanitizeFilePath(destinationPath));
            return;
        }

        if (ownership.Outcome
            != AudiobookFileOwnershipCheckOutcome.Available)
        {
            _logger.LogInformation(
                "Left the refused manual-import destination in place for audiobook {AudiobookId} because the catalog already owns a file there: {Path}",
                audiobook.Id,
                LogRedaction.SanitizeFilePath(destinationPath));
            return;
        }

        if (!registrationLease.MatchesCurrentPublication())
        {
            _logger.LogWarning(
                "Left the refused manual-import destination in place for audiobook {AudiobookId} because it no longer holds the generation this import published: {Path}",
                audiobook.Id,
                LogRedaction.SanitizeFilePath(destinationPath));
            return;
        }

        if (!_fileSystem.TryValidateMutationTarget(
                destinationPath,
                allowedDestinationRoots,
                out var safeDestinationPath,
                out var reason))
        {
            _logger.LogWarning(
                "Left the refused manual-import destination in place for audiobook {AudiobookId}: {Reason}",
                audiobook.Id,
                LogRedaction.SanitizeText(reason));
            return;
        }

        // Windows opens the stable-read handle behind the lease with share-read
        // only, so the pathname cannot be unlinked while the lease holds it.
        // Release it here; the caller's using block disposes it again, which the
        // lease treats as a no-op.
        registrationLease.Dispose();

        try
        {
            _fileSystem.DeleteFile(safeDestinationPath);
            _logger.LogInformation(
                "Removed the destination published for audiobook {AudiobookId} after its manual import was refused at registration: {Path}",
                audiobook.Id,
                LogRedaction.SanitizeFilePath(safeDestinationPath));
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "A refused manual import could not remove the destination it published for audiobook {AudiobookId}: {Path}",
                audiobook.Id,
                LogRedaction.SanitizeFilePath(safeDestinationPath));
        }
    }
}
