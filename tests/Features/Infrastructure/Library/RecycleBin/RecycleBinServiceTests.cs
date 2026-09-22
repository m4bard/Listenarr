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
using Listenarr.Application.Library.RecycleBin;
using Listenarr.Infrastructure.Library.RecycleBin;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.RecycleBin;

[Trait("Name", "RecycleBinServiceTests")]
[Trait("Category", "Infrastructure")]
public sealed class RecycleBinServiceTests : BaseTests
{
    private static RecycleBinService BuildService(
        string? binPath,
        int retentionDays,
        IEnumerable<string> rootPaths,
        TimeProvider? timeProvider = null)
    {
        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.GetApplicationSettingsAsync())
            .ReturnsAsync(new ApplicationSettings
            {
                RecycleBinPath = binPath ?? string.Empty,
                RecycleBinCleanupDays = retentionDays
            });

        var rootFolderService = new Mock<IRootFolderService>();
        rootFolderService
            .Setup(service => service.GetAllAsync())
            .ReturnsAsync(rootPaths.Select(path => new RootFolder { Path = path }).ToList());

        return new RecycleBinService(
            configurationService.Object,
            rootFolderService.Object,
            timeProvider ?? TimeProvider.System,
            NullLogger<RecycleBinService>.Instance);
    }

    [Fact]
    public async Task ValidatePathAsync_EmptyPath_IsAccepted()
    {
        var service = BuildService(binPath: null, retentionDays: 7, rootPaths: []);

        var result = await service.ValidatePathAsync(string.Empty);

        // Empty means the feature is off, which is the family default: Readarr
        // ConfigService.cs:93 and Sonarr ConfigService.cs:100 both default it to "".
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePathAsync_BinInsideARootFolder_IsRefused()
    {
        var root = FileService.GetTempDirectory("recycle-validate-root");
        var binInsideRoot = Path.Join(root, "recycled");
        var service = BuildService(binInsideRoot, retentionDays: 7, rootPaths: [root]);

        var result = await service.ValidatePathAsync(binInsideRoot);

        // The control: the same service accepts a sibling directory, so a refusal here is
        // about containment and not about the validator refusing everything.
        Assert.False(result.IsValid);
        Assert.Equal(RecycleBinPathRejection.InsideRootFolder, result.Rejection);

        var sibling = FileService.GetTempDirectory("recycle-validate-sibling");
        var siblingResult = await service.ValidatePathAsync(sibling);
        Assert.True(siblingResult.IsValid);
    }

    [Fact]
    public async Task ValidatePathAsync_BinContainingARootFolder_IsRefused()
    {
        var bin = FileService.GetTempDirectory("recycle-validate-outer");
        var rootInsideBin = Path.Join(bin, "library");
        Directory.CreateDirectory(rootInsideBin);
        var service = BuildService(bin, retentionDays: 7, rootPaths: [rootInsideBin]);

        var result = await service.ValidatePathAsync(bin);

        Assert.False(result.IsValid);
        Assert.Equal(RecycleBinPathRejection.ContainsRootFolder, result.Rejection);
    }

    [Fact]
    public async Task ValidatePathAsync_RelativePath_IsRefused()
    {
        var service = BuildService(binPath: null, retentionDays: 7, rootPaths: []);

        var result = await service.ValidatePathAsync(Path.Join("some", "relative", "bin"));

        Assert.False(result.IsValid);
        Assert.Equal(RecycleBinPathRejection.NotAbsolute, result.Rejection);
    }

    [Fact]
    public async Task EmptyAsync_RemovesEverythingRegardlessOfAge()
    {
        var root = FileService.GetTempDirectory("recycle-empty-root");
        var bin = FileService.GetTempDirectory("recycle-empty-bin");
        var recent = await FileService.GetFileAsync(bin, "recent.m4b", "recent");
        var nested = Path.Join(bin, "Author", "Title");
        Directory.CreateDirectory(nested);
        var old = await FileService.GetFileAsync(nested, "old.m4b", "old");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddYears(-3));

        var service = BuildService(bin, retentionDays: 7, rootPaths: [root]);
        var result = await service.EmptyAsync();

        Assert.Equal(2, result.FilesRemoved);
        Assert.False(File.Exists(recent));
        Assert.False(File.Exists(old));

        // The bin root itself has to survive, or the configured path stops existing and
        // the next delete has nowhere to go.
        Assert.True(Directory.Exists(bin));
    }

    [Fact]
    public async Task CleanupAsync_RetentionZero_RemovesNothing()
    {
        var root = FileService.GetTempDirectory("recycle-retention-zero-root");
        var bin = FileService.GetTempDirectory("recycle-retention-zero-bin");
        var ancient = await FileService.GetFileAsync(bin, "ancient.m4b", "ancient");
        File.SetLastWriteTimeUtc(ancient, DateTime.UtcNow.AddYears(-10));

        var service = BuildService(bin, retentionDays: 0, rootPaths: [root]);
        var result = await service.CleanupAsync();

        // Zero means keep until emptied by hand. Reading it as "keep for zero days" would
        // purge the bin on the first cycle, and this file is old enough that any
        // age-based sweep would take it.
        Assert.Equal(0, result.FilesRemoved);
        Assert.True(File.Exists(ancient));
    }

    [Fact]
    public async Task CleanupAsync_RetentionSet_RemovesOnlyWhatIsOlderThanIt()
    {
        var root = FileService.GetTempDirectory("recycle-retention-root");
        var bin = FileService.GetTempDirectory("recycle-retention-bin");
        var fresh = await FileService.GetFileAsync(bin, "fresh.m4b", "fresh");
        var stale = await FileService.GetFileAsync(bin, "stale.m4b", "stale");
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow.AddDays(-1));
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-30));

        var service = BuildService(bin, retentionDays: 7, rootPaths: [root]);
        var result = await service.CleanupAsync();

        // Two files, one each side of the cutoff. A sweep that ignored the timestamp and
        // deleted everything would also report a removal, so the surviving file is what
        // makes this a measurement rather than a count.
        Assert.Equal(1, result.FilesRemoved);
        Assert.True(File.Exists(fresh));
        Assert.False(File.Exists(stale));
    }

    [Fact]
    public async Task CleanupAsync_BinHasSinceMovedInsideARootFolder_SweepsNothing()
    {
        var root = FileService.GetTempDirectory("recycle-drifted-root");
        var binInsideRoot = Path.Join(root, "recycled");
        Directory.CreateDirectory(binInsideRoot);
        var stale = await FileService.GetFileAsync(binInsideRoot, "stale.m4b", "stale");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-30));

        var service = BuildService(binInsideRoot, retentionDays: 7, rootPaths: [root]);
        var result = await service.CleanupAsync();

        // The path was valid when it was saved and a root folder was added around it
        // later. The previous test proves a 30 day old file IS swept when the path is
        // valid, so this one isolates the revalidation rather than the age check.
        Assert.Equal(0, result.FilesRemoved);
        Assert.True(File.Exists(stale));
    }

    [DirectoryLinkFact]
    public async Task EmptyAsync_BinContainsALinkToTheLibrary_DoesNotFollowIt()
    {
        var root = FileService.GetTempDirectory("recycle-symlink-root");
        var bin = FileService.GetTempDirectory("recycle-symlink-bin");
        var precious = await FileService.GetFileAsync(root, "precious.m4b", "library-content");
        Directory.CreateSymbolicLink(Path.Join(bin, "sneaky"), root);
        var ordinary = await FileService.GetFileAsync(bin, "ordinary.m4b", "recycled");

        var service = BuildService(bin, retentionDays: 7, rootPaths: [root]);
        var result = await service.EmptyAsync();

        // The control: the bin genuinely had something to remove, and it was removed, so
        // a pass here is not the sweep failing to run. What must NOT happen is the sweep
        // walking through the link and deleting library content.
        Assert.False(File.Exists(ordinary));
        Assert.Equal(1, result.FilesRemoved);
        Assert.True(
            File.Exists(precious),
            "The sweep followed a directory link out of the bin and deleted library content.");
        Assert.Equal("library-content", await File.ReadAllTextAsync(precious));
    }

    [Fact]
    public async Task CleanupAsync_NoBinConfigured_DoesNothing()
    {
        var service = BuildService(binPath: null, retentionDays: 7, rootPaths: []);

        var result = await service.CleanupAsync();

        Assert.Equal(0, result.FilesRemoved);
        await Task.CompletedTask;
    }
}
