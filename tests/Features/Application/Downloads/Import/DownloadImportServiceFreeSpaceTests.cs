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
/// Exercises the free-space guard through the real import path
/// (DownloadImportService.ImportDownloadFilesAsync), not just the guard in isolation, so a
/// regression in how the two are wired together is caught here rather than only in
/// FreeSpaceImportGuardTests.
/// </summary>
[Trait("Name", "DownloadImportServiceFreeSpaceTests")]
[Trait("Category", "DownloadProcessingJob")]
public sealed class DownloadImportServiceFreeSpaceTests : BaseTests
{
    private readonly Mock<IDiskSpaceProbe> _diskSpaceProbe = new();

    public override async Task InitializeAsync()
    {
        _services.AddSingleton<IDiskSpaceProbe>(_diskSpaceProbe.Object);
        Init();
        await AddAuthorizedRootAsync(FileService.GetTempPath());
    }

    private void SetFreeBytes(long freeBytes)
    {
        long total = 0;
        long free = freeBytes;
        _diskSpaceProbe
            .Setup(p => p.TryGetDiskSpace(It.IsAny<string>(), out total, out free))
            .Returns(true);
    }

    [Fact]
    public async Task ImportDownloadFilesAsync_NotEnoughFreeSpace_RejectsWithoutWritingTheFile()
    {
        var basePath = FileService.GetTempDirectory("download-import-freespace-reject");
        var sourceDirectory = FileService.GetTempDirectory("download-import-freespace-reject-src");
        // 1 KB source file, but only 1 KB reported free with the default 100MB margin: the
        // guard's own arithmetic (required + margin) must reject this regardless of how small
        // the file is.
        var sourceFile = await FileService.GetFileAsync(sourceDirectory, "book.mp3", "x");
        SetFreeBytes(1024);
        var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithTitle("Not Enough Room")
            .WithBasePath(basePath)
            .Build());
        await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
            .WithCopyFileOnCompleted()
            .WithoutMetadataProcessing()
            .WithMinimumFreeSpaceWhenImporting(100)
            .Build());
        var service = _provider.GetRequiredService<IDownloadImportService>();

        var result = Assert.Single(await service.ImportDownloadFilesAsync(audiobook, [sourceFile]));

        Assert.False(result.Success);
        Assert.Equal("Not enough free space", result.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(basePath));
        var tracked = await _audiobookFileRepository.GetByAudiobookIdAsync(audiobook.Id);
        Assert.Empty(tracked);
    }

    [Fact]
    public async Task ImportDownloadFilesAsync_EnoughFreeSpace_Imports()
    {
        // Control for the rejection test above: identical setup, only the reported free space
        // differs. If the guard's comparison were inert (always rejecting, or not wired to the
        // real probe result), this case would fail instead of the rejection case, so the two
        // together demonstrate the guard is actually gating on the measured value.
        var basePath = FileService.GetTempDirectory("download-import-freespace-allow");
        var sourceDirectory = FileService.GetTempDirectory("download-import-freespace-allow-src");
        var sourceFile = await FileService.GetFileAsync(sourceDirectory, "book.mp3", "x");
        SetFreeBytes(500L * 1024 * 1024);
        var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithTitle("Enough Room")
            .WithBasePath(basePath)
            .Build());
        await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
            .WithCopyFileOnCompleted()
            .WithoutMetadataProcessing()
            .WithMinimumFreeSpaceWhenImporting(100)
            .Build());
        var service = _provider.GetRequiredService<IDownloadImportService>();

        var result = Assert.Single(await service.ImportDownloadFilesAsync(audiobook, [sourceFile]));

        Assert.True(result.Success);
        Assert.True(File.Exists(result.FinalPath));
    }

    [Fact]
    public async Task ImportDownloadFilesAsync_SkipFreeSpaceCheckWhenImporting_ImportsDespiteNoFreeSpace()
    {
        // The escape hatch is part of the feature, not an optional extra: a network filesystem
        // can report free space Listenarr cannot trust, so this setting must be able to
        // override a rejection the guard would otherwise make.
        var basePath = FileService.GetTempDirectory("download-import-freespace-skip");
        var sourceDirectory = FileService.GetTempDirectory("download-import-freespace-skip-src");
        var sourceFile = await FileService.GetFileAsync(sourceDirectory, "book.mp3", "x");
        SetFreeBytes(0);
        var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithTitle("Skip The Check")
            .WithBasePath(basePath)
            .Build());
        await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
            .WithCopyFileOnCompleted()
            .WithoutMetadataProcessing()
            .WithMinimumFreeSpaceWhenImporting(100)
            .WithSkipFreeSpaceCheckWhenImporting()
            .Build());
        var service = _provider.GetRequiredService<IDownloadImportService>();

        var result = Assert.Single(await service.ImportDownloadFilesAsync(audiobook, [sourceFile]));

        Assert.True(result.Success);
    }
}
