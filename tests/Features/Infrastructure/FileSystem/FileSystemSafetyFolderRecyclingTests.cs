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
using Listenarr.Infrastructure.FileSystem;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.FileSystem;

/// <summary>
/// The overlap and cross-filesystem guards of the folder recycle, tested at the
/// primitive. Through the delete endpoint both overlaps are already refused earlier, by
/// the bin path validation (a bin inside a root, or a root inside the bin), so an
/// endpoint test would pass whether or not this guard existed. The guard is still
/// needed: validation does not consider the configured output path, and the bin and the
/// root folders are edited independently.
/// </summary>
[Trait("Name", "FileSystemSafetyFolderRecyclingTests")]
[Trait("Category", "Infrastructure")]
public sealed class FileSystemSafetyFolderRecyclingTests : BaseTests
{
    private static RecycleFolderPreparationOutcome Prepare(
        string folder,
        string bin,
        string? relativeParent,
        out RecycleFolderContainer? container,
        out string reason)
    {
        using var source = PinnedDirectoryCreation.OpenPinnedHierarchyNoFollow(
            folder,
            createMissing: false);
        return FileSystemSafety.TryPrepareFolderRecycle(
            source,
            folder,
            bin,
            relativeParent,
            TimeProvider.System,
            out container,
            out reason);
    }

    [Fact]
    public async Task TryPrepareFolderRecycle_SeparateBin_CreatesTheMirroredFolder()
    {
        var root = FileService.GetTempDirectory("folder-recycle-ready");
        var bin = FileService.GetTempDirectory("folder-recycle-ready-bin");
        var folder = Path.Join(root, "Author", "Book");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Join(folder, "book.mp3"), "audio");

        var outcome = Prepare(folder, bin, "Author", out var container, out var reason);

        // The control for the two refusals below: the same call with a bin that does not
        // overlap succeeds, so a refusal there is the overlap and not a broken arrangement.
        using (container)
        {
            Assert.Equal(RecycleFolderPreparationOutcome.Ready, outcome);
            Assert.True(string.IsNullOrEmpty(reason), reason);
            Assert.Equal(Path.Join(bin, "Author", "Book"), container!.FullPath);
            Assert.True(Directory.Exists(container.FullPath));
        }
    }

    [Fact]
    public async Task TryPrepareFolderRecycle_BinInsideTheFolder_RefusesAndCreatesNothing()
    {
        var root = FileService.GetTempDirectory("folder-recycle-bin-inside");
        var folder = Path.Join(root, "Book");
        var bin = Path.Join(folder, "recycle");
        Directory.CreateDirectory(bin);
        await File.WriteAllTextAsync(Path.Join(folder, "book.mp3"), "audio");

        var outcome = Prepare(folder, bin, null, out var container, out _);

        // Emptying a folder into its own descendant would walk into the bin mid-delete
        // and try to recycle the bin into itself.
        Assert.Equal(RecycleFolderPreparationOutcome.Overlaps, outcome);
        Assert.Null(container);
        Assert.Empty(Directory.EnumerateFileSystemEntries(bin));
        Assert.True(File.Exists(Path.Join(folder, "book.mp3")));
    }

    [Fact]
    public async Task TryPrepareFolderRecycle_FolderInsideTheBin_RefusesAndCreatesNothing()
    {
        var bin = FileService.GetTempDirectory("folder-recycle-folder-inside");
        var folder = Path.Join(bin, "Book");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Join(folder, "book.mp3"), "audio");

        var outcome = Prepare(folder, bin, null, out var container, out _);

        Assert.Equal(RecycleFolderPreparationOutcome.Overlaps, outcome);
        Assert.Null(container);
        Assert.Single(Directory.EnumerateFileSystemEntries(bin));
        Assert.True(File.Exists(Path.Join(folder, "book.mp3")));
    }

    [Fact]
    public void TryPrepareFolderRecycle_NameTaken_SuffixesTheNewFolder()
    {
        var root = FileService.GetTempDirectory("folder-recycle-taken");
        var bin = FileService.GetTempDirectory("folder-recycle-taken-bin");
        var folder = Path.Join(root, "Book");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Join(bin, "Book"));

        var outcome = Prepare(folder, bin, null, out var container, out _);

        using (container)
        {
            Assert.Equal(RecycleFolderPreparationOutcome.Ready, outcome);
            Assert.Equal(Path.Join(bin, "Book_1"), container!.FullPath);
        }
    }

    [CrossVolumeFact]
    public void TryPrepareFolderRecycle_BinOnAnotherFilesystem_RefusesAndCreatesNothing()
    {
        var root = FileService.GetTempDirectory("folder-recycle-xdev");
        var folder = Path.Join(root, "Book");
        Directory.CreateDirectory(folder);
        var bin = Path.Join(
            Environment.GetEnvironmentVariable(
                CrossVolumeFactAttribute.DestinationPathEnvironmentVariable)!,
            $"folder-recycle-xdev-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bin);
        try
        {
            var outcome = Prepare(folder, bin, "Author", out var container, out _);

            Assert.Equal(RecycleFolderPreparationOutcome.CrossVolume, outcome);
            Assert.Null(container);
            Assert.Empty(Directory.EnumerateFileSystemEntries(bin));
        }
        finally
        {
            Directory.Delete(bin, recursive: true);
        }
    }
}
