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
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Downloads.Import;

/// <summary>
/// Exercises the free-space guard against the real IDiskSpaceProbe (not a mock), with a
/// destination several folder levels below an authorized root, none of which exist on disk
/// yet. This is the exact shape ApplicationSettings.FolderNamingPattern's default,
/// {Author}/{Series}/{Title}, produces for a brand new author: DiskSpaceProbe.TryGetDiskSpace
/// requires Directory.Exists and returns false for a missing path, so probing only the
/// immediate parent of BasePath would always report "cannot measure" here and the guard
/// would silently never fire. DownloadImportServiceFreeSpaceTests mocks IDiskSpaceProbe with
/// It.IsAny&lt;string&gt;(), which returns true for any path and so cannot catch this class of
/// bug; this class deliberately does not mock the probe.
/// </summary>
[Trait("Name", "DownloadImportServiceFreeSpaceRealProbeTests")]
[Trait("Category", "DownloadProcessingJob")]
public sealed class DownloadImportServiceFreeSpaceRealProbeTests : BaseTests
{
    public override async Task InitializeAsync()
    {
        Init();
        await AddAuthorizedRootAsync(FileService.GetTempPath());
    }

    [Fact]
    public async Task ImportDownloadFilesAsync_NestedUncreatedDestination_StillMeasuresRealFreeSpace()
    {
        // An enormous margin: the real temp filesystem will never report this much free
        // space, so a rejection here proves the probe reached a real, existing ancestor and
        // measured it. If the ancestor walk were broken (reverted to probing only the
        // immediate, nonexistent parent), TryGetDiskSpace would return false, the guard would
        // log "could not be determined" and allow the import regardless of this margin, and
        // this test would fail because the import would succeed instead.
        const int impossibleMarginMegabytes = int.MaxValue;
        var basePath = Path.Join(
            FileService.GetTempPath(),
            $"New Author {Guid.NewGuid():N}",
            "New Series",
            "New Book");
        Assert.False(Directory.Exists(basePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(basePath)));
        var sourceDirectory = FileService.GetTempDirectory(
            $"download-import-freespace-real-probe-src-{Guid.NewGuid():N}");
        var sourceFile = await FileService.GetFileAsync(sourceDirectory, "book.mp3", "x");
        var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithTitle("Never Created Yet")
            .WithBasePath(basePath)
            .Build());
        await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
            .WithCopyFileOnCompleted()
            .WithoutMetadataProcessing()
            .WithMinimumFreeSpaceWhenImporting(impossibleMarginMegabytes)
            .Build());
        var service = _provider.GetRequiredService<IDownloadImportService>();

        var result = Assert.Single(await service.ImportDownloadFilesAsync(audiobook, [sourceFile]));

        Assert.False(result.Success);
        Assert.Equal("Not enough free space", result.Message);
    }
}
