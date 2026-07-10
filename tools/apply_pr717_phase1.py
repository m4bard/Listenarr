from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, content: str) -> None:
    (ROOT / path).write_text(content, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    content = read(path)
    count = content.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one match, found {count}")
    write(path, content.replace(old, new, 1))


workflow_path = "listenarr.api/Features/Library/LibraryUpdateWorkflow.cs"
replace_once(
    workflow_path,
    """            var legacyIdentifierFieldsTouched = false;
            var basePathRewritten = false;
""",
    """            var legacyIdentifierFieldsTouched = false;
            var basePathRewritten = false;
            var metadataUpdateRequested = HasMetadataUpdates(request);
""",
)
replace_once(
    workflow_path,
    """            await _repo.UpdateAsync(existingAudiobook);

            _logger.LogInformation("Updated audiobook '{Title}' (ID: {Id})", LogRedaction.SanitizeText(existingAudiobook.Title), id);
""",
    """            if (metadataUpdateRequested)
            {
                await _repo.UpdateAsync(existingAudiobook);
            }

            _logger.LogInformation("Updated audiobook '{Title}' (ID: {Id})", LogRedaction.SanitizeText(existingAudiobook.Title), id);
""",
)
replace_once(
    workflow_path,
    """        private static IActionResult ToApplicationExceptionResult(ListenarrApplicationException exception) =>
""",
    """        private static bool HasMetadataUpdates(AudiobookUpdateRequest request) =>
            request.Title != null
            || request.Subtitle != null
            || request.Authors != null
            || request.ImageUrl != null
            || request.PublishYear != null
            || request.PublishedDate != null
            || request.Series != null
            || request.SeriesNumber != null
            || request.SeriesMemberships != null
            || request.Description != null
            || request.Genres != null
            || request.Tags != null
            || request.Narrators != null
            || request.Isbn != null
            || request.Asin != null
            || request.OpenLibraryId != null
            || request.Publisher != null
            || request.Language != null
            || request.Runtime.HasValue
            || request.Edition != null
            || request.Version != null
            || request.Explicit.HasValue
            || request.Abridged.HasValue
            || request.Monitored.HasValue
            || request.FilePath != null
            || request.FileSize.HasValue
            || request.Quality != null
            || request.QualityProfileId.HasValue;

        private static IActionResult ToApplicationExceptionResult(ListenarrApplicationException exception) =>
""",
)

replace_once(
    "fe/src/types/index.ts",
    """  status?: AudiobookStatus
}

export interface History {
""",
    """  status?: AudiobookStatus
}

export interface AudiobookUpdateRequest {
  title?: string
  subtitle?: string
  authors?: string[]
  imageUrl?: string
  publishYear?: string
  publishedDate?: string
  series?: string
  seriesNumber?: string
  seriesMemberships?: AudiobookSeriesMembership[]
  description?: string
  genres?: string[]
  tags?: string[]
  narrators?: string[]
  isbn?: string[]
  asin?: string
  openLibraryId?: string
  publisher?: string
  language?: string
  runtime?: number
  edition?: string
  version?: string
  explicit?: boolean
  abridged?: boolean
  monitored?: boolean
  filePath?: string
  fileSize?: number
  basePath?: string
  quality?: string
  qualityProfileId?: number
}

export interface History {
""",
)
replace_once(
    "fe/src/services/api.ts",
    """  Audiobook,
  History,
""",
    """  Audiobook,
  AudiobookUpdateRequest,
  History,
""",
)
replace_once(
    "fe/src/services/api.ts",
    """  async updateAudiobook(
    id: number,
    audiobook: Partial<Audiobook>,
  ): Promise<{ message: string; audiobook: Audiobook }> {
""",
    """  async updateAudiobook(
    id: number,
    audiobook: AudiobookUpdateRequest,
  ): Promise<{ message: string; audiobook: Audiobook }> {
""",
)

write(
    "tests/Features/Api/Features/Library/LibraryUpdateWorkflowTests.cs",
    '''/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Features.Library;

[Trait("Area", "LibraryApi")]
[Trait("Category", "LibraryController")]
public sealed class LibraryUpdateWorkflowTests
{
    [Fact]
    public async Task UpdateAsync_DestinationOnlyRewrite_DoesNotIssueMetadataWrite()
    {
        var id = 42;
        var source = Path.Join(Path.GetTempPath(), $"listenarr-update-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"listenarr-update-target-{Guid.NewGuid():N}");
        var before = new Audiobook
        {
            Id = id,
            Title = "Book",
            BasePath = source,
            Explicit = true,
            Abridged = true,
            Monitored = false
        };
        var after = new Audiobook
        {
            Id = id,
            Title = "Book",
            BasePath = target,
            Explicit = true,
            Abridged = true,
            Monitored = false
        };

        var repository = new Mock<IAudiobookRepository>(MockBehavior.Strict);
        repository
            .SetupSequence(candidate => candidate.GetByIdAsync(id))
            .ReturnsAsync(before)
            .ReturnsAsync(after);
        var rewriteService = new Mock<IAudiobookDestinationRewriteService>(MockBehavior.Strict);
        rewriteService
            .Setup(candidate => candidate.RewriteDestinationAsync(
                id,
                target,
                source,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AudiobookDestinationRewriteResult(id, target, source));

        var workflow = new LibraryUpdateWorkflow(
            repository.Object,
            Mock.Of<IServiceScopeFactory>(),
            rewriteService.Object,
            NullLogger<LibraryUpdateWorkflow>.Instance);

        var result = await workflow.UpdateAsync(id, new AudiobookUpdateRequest { BasePath = target });

        Assert.IsType<OkObjectResult>(result);
        repository.Verify(candidate => candidate.UpdateAsync(It.IsAny<Audiobook>()), Times.Never);
        Assert.True(after.Explicit);
        Assert.True(after.Abridged);
        Assert.False(after.Monitored);
    }

    [Fact]
    public async Task UpdateAsync_DestinationAndMetadataUpdate_PreservesOmittedBooleans()
    {
        var id = 43;
        var source = Path.Join(Path.GetTempPath(), $"listenarr-update-source-{Guid.NewGuid():N}");
        var target = Path.Join(Path.GetTempPath(), $"listenarr-update-target-{Guid.NewGuid():N}");
        var before = new Audiobook
        {
            Id = id,
            Title = "Original",
            BasePath = source,
            Explicit = true,
            Abridged = true,
            Monitored = false
        };
        var after = new Audiobook
        {
            Id = id,
            Title = "Original",
            BasePath = target,
            Explicit = true,
            Abridged = true,
            Monitored = false
        };

        var repository = new Mock<IAudiobookRepository>(MockBehavior.Strict);
        repository
            .SetupSequence(candidate => candidate.GetByIdAsync(id))
            .ReturnsAsync(before)
            .ReturnsAsync(after);
        repository.Setup(candidate => candidate.UpdateAsync(after)).ReturnsAsync(true);
        var rewriteService = new Mock<IAudiobookDestinationRewriteService>(MockBehavior.Strict);
        rewriteService
            .Setup(candidate => candidate.RewriteDestinationAsync(
                id,
                target,
                source,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AudiobookDestinationRewriteResult(id, target, source));

        var workflow = new LibraryUpdateWorkflow(
            repository.Object,
            Mock.Of<IServiceScopeFactory>(),
            rewriteService.Object,
            NullLogger<LibraryUpdateWorkflow>.Instance);

        var result = await workflow.UpdateAsync(
            id,
            new AudiobookUpdateRequest
            {
                BasePath = target,
                Title = "Edited"
            });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Edited", after.Title);
        Assert.True(after.Explicit);
        Assert.True(after.Abridged);
        Assert.False(after.Monitored);
        repository.Verify(candidate => candidate.UpdateAsync(after), Times.Once);
    }
}
''',
)
