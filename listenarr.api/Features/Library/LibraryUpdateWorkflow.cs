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

using Listenarr.Application.Common.Exceptions;
using Listenarr.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    public sealed class LibraryUpdateWorkflow
    {
        private readonly IAudiobookRepository _repo;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IAudiobookDestinationRewriteService _destinationRewriteService;
        private readonly ILogger<LibraryUpdateWorkflow> _logger;

        public LibraryUpdateWorkflow(
            IAudiobookRepository repo,
            IServiceScopeFactory scopeFactory,
            IAudiobookDestinationRewriteService destinationRewriteService,
            ILogger<LibraryUpdateWorkflow> logger)
        {
            _repo = repo;
            _scopeFactory = scopeFactory;
            _destinationRewriteService = destinationRewriteService;
            _logger = logger;
        }

        public async Task<IActionResult> UpdateAsync(int id, AudiobookUpdateRequest request)
        {
            var existingAudiobook = await _repo.GetByIdAsync(id);
            if (existingAudiobook == null)
            {
                return new NotFoundObjectResult(new { message = "Audiobook not found" });
            }

            var legacyIdentifierFieldsTouched = false;
            var basePathRewritten = false;

            if (request.BasePath != null)
            {
                var requestedBasePath = FileUtils.NormalizeStoredPath(request.BasePath);
                var existingBasePath = string.IsNullOrEmpty(existingAudiobook.BasePath)
                    ? string.Empty
                    : FileUtils.NormalizeStoredPath(existingAudiobook.BasePath);
                if (!string.Equals(requestedBasePath, existingBasePath, StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "Deprecated PUT /library/{AudiobookId} BasePath update received. Route destination changes through the move endpoint with moveFiles=false.",
                        id);

                    try
                    {
                        await _destinationRewriteService.RewriteDestinationAsync(
                            id,
                            request.BasePath,
                            existingAudiobook.BasePath);
                        basePathRewritten = true;
                    }
                    catch (ListenarrApplicationException ex)
                    {
                        return ToApplicationExceptionResult(ex);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                    {
                        _logger.LogError(ex, "Failed to compatibility-route BasePath update for audiobook {AudiobookId}", id);
                        return new ObjectResult(new { message = "Failed to update BasePath", error = ex.Message })
                        {
                            StatusCode = StatusCodes.Status500InternalServerError
                        };
                    }

                    existingAudiobook = await _repo.GetByIdAsync(id);
                    if (existingAudiobook == null)
                    {
                        return new NotFoundObjectResult(new { message = "Audiobook not found" });
                    }
                }
            }

            if (request.Title != null) existingAudiobook.Title = request.Title;
            if (request.Subtitle != null) existingAudiobook.Subtitle = request.Subtitle;
            if (request.Authors != null) existingAudiobook.Authors = request.Authors;
            if (!basePathRewritten && request.ImageUrl != null) existingAudiobook.ImageUrl = request.ImageUrl;
            if (request.PublishYear != null) existingAudiobook.PublishYear = request.PublishYear;
            if (request.PublishedDate != null) existingAudiobook.PublishedDate = request.PublishedDate;
            if (request.Description != null) existingAudiobook.Description = request.Description;
            if (request.Genres != null) existingAudiobook.Genres = request.Genres;
            if (request.Tags != null) existingAudiobook.Tags = request.Tags;
            if (request.Narrators != null) existingAudiobook.Narrators = request.Narrators;
            if (request.Isbn != null)
            {
                existingAudiobook.Isbn = request.Isbn;
                legacyIdentifierFieldsTouched = true;
            }

            if (request.Asin != null)
            {
                existingAudiobook.Asin = request.Asin;
                legacyIdentifierFieldsTouched = true;
            }

            if (request.OpenLibraryId != null)
            {
                existingAudiobook.OpenLibraryId = request.OpenLibraryId;
                legacyIdentifierFieldsTouched = true;
            }

            if (request.Publisher != null) existingAudiobook.Publisher = request.Publisher;
            if (request.Language != null) existingAudiobook.Language = request.Language;
            if (request.Runtime != null) existingAudiobook.Runtime = request.Runtime;
            if (request.Edition != null) existingAudiobook.Edition = request.Edition;
            if (request.Version != null) existingAudiobook.Version = request.Version;

            ApplySeriesMembershipUpdates(existingAudiobook, request);

            if (request.Explicit.HasValue) existingAudiobook.Explicit = request.Explicit.Value;
            if (request.Abridged.HasValue) existingAudiobook.Abridged = request.Abridged.Value;
            if (request.Monitored.HasValue) existingAudiobook.Monitored = request.Monitored.Value;

            if (!basePathRewritten && request.FilePath != null) existingAudiobook.FilePath = request.FilePath;
            if (request.FileSize.HasValue) existingAudiobook.FileSize = request.FileSize;
            if (request.Quality != null) existingAudiobook.Quality = request.Quality;

            await ApplyQualityProfileAsync(existingAudiobook, request);

            if (legacyIdentifierFieldsTouched)
            {
                AudiobookIdentifierMapper.SyncImportedIdentifiersFromLegacyFields(existingAudiobook);
            }

            await _repo.UpdateAsync(existingAudiobook);

            _logger.LogInformation("Updated audiobook '{Title}' (ID: {Id})", LogRedaction.SanitizeText(existingAudiobook.Title), id);

            return new OkObjectResult(new { message = "Audiobook updated successfully", audiobook = existingAudiobook });
        }

        private static IActionResult ToApplicationExceptionResult(ListenarrApplicationException exception) =>
            exception switch
            {
                ApplicationNotFoundException => new NotFoundObjectResult(new { message = exception.SafeDetail, code = exception.Code }),
                ApplicationConflictException => new ConflictObjectResult(new { message = exception.SafeDetail, code = exception.Code }),
                ApplicationValidationException => new BadRequestObjectResult(new { message = exception.SafeDetail, code = exception.Code }),
                _ => new ObjectResult(new { message = exception.SafeDetail, code = exception.Code })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                }
            };

        private static void ApplySeriesMembershipUpdates(Audiobook existingAudiobook, AudiobookUpdateRequest request)
        {
            var seriesMembershipsTouched =
                request.SeriesMemberships != null ||
                request.Series != null ||
                request.SeriesNumber != null;

            if (!seriesMembershipsTouched)
            {
                return;
            }

            var mergedSeries = request.Series ?? existingAudiobook.Series;
            var mergedSeriesNumber = request.SeriesNumber ?? existingAudiobook.SeriesNumber;
            var existingPrimaryMembership = AudiobookSeriesMembershipHelper.GetPrimaryMembership(existingAudiobook.SeriesMemberships);

            var normalizedMemberships = AudiobookSeriesMembershipHelper.Normalize(
                request.SeriesMemberships,
                mergedSeries,
                mergedSeriesNumber,
                existingPrimaryMembership?.SeriesAsin);

            if (existingAudiobook.SeriesMemberships == null)
            {
                existingAudiobook.SeriesMemberships = new List<AudiobookSeriesMembership>();
            }
            else
            {
                existingAudiobook.SeriesMemberships.Clear();
            }

            foreach (var membership in normalizedMemberships)
            {
                existingAudiobook.SeriesMemberships.Add(membership);
            }

            AudiobookSeriesMembershipHelper.ApplyPrimarySeriesFields(existingAudiobook);
        }

        private async Task ApplyQualityProfileAsync(Audiobook existingAudiobook, AudiobookUpdateRequest request)
        {
            if (!request.QualityProfileId.HasValue)
            {
                return;
            }

            if (request.QualityProfileId.Value == -1)
            {
                using var scope = _scopeFactory.CreateScope();
                var qualityProfileService = scope.ServiceProvider.GetRequiredService<IQualityProfileService>();
                var defaultProfile = await qualityProfileService.GetDefaultAsync();
                if (defaultProfile != null)
                {
                    existingAudiobook.QualityProfileId = defaultProfile.Id;
                    _logger.LogInformation("Assigned default quality profile '{ProfileName}' (ID: {ProfileId}) to audiobook '{Title}'",
                        defaultProfile.Name, defaultProfile.Id, existingAudiobook.Title);
                }
                else
                {
                    _logger.LogWarning("No default quality profile found. Audiobook '{Title}' quality profile set to null.", LogRedaction.SanitizeText(existingAudiobook.Title));
                    existingAudiobook.QualityProfileId = null;
                }

                return;
            }

            existingAudiobook.QualityProfileId = request.QualityProfileId.Value;
            _logger.LogInformation("Updated quality profile for audiobook '{Title}' to ID {ProfileId}",
                existingAudiobook.Title, request.QualityProfileId.Value);
        }
    }
}
