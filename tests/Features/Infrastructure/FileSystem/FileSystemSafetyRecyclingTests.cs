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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.FileSystem;

[Trait("Name", "FileSystemSafetyRecyclingTests")]
[Trait("Category", "Infrastructure")]
public sealed class FileSystemSafetyRecyclingTests : BaseTests
{
    [Fact]
    public async Task TryRecycleFile_ConfiguredBin_MovesTheFileInsteadOfUnlinkingIt()
    {
        var root = FileService.GetTempDirectory("recycle-move-root");
        var bin = FileService.GetTempDirectory("recycle-move-bin");
        var file = await FileService.GetFileAsync(root, "book.m4b", "audio");

        var outcome = global::Listenarr.Infrastructure.FileSystem.FileSystemSafety.TryRecycleFile(
            file,
            [root],
            expectedPhysicalObjectIdentity: null,
            bin,
            relativeSubfolder: null,
            TimeProvider.System,
            out var recycledPath,
            out var reason);

        Assert.Equal(
            global::Listenarr.Infrastructure.FileSystem.RecycleFileOutcome.Recycled,
            outcome);
        Assert.True(string.IsNullOrEmpty(reason), reason);

        // The control that makes this a measurement rather than an assertion about a
        // return value: the bytes have to be in the bin AND gone from the library. A
        // recycle that silently unlinked would satisfy the second half only, and a
        // recycle that silently copied would satisfy the first half only.
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(recycledPath));
        Assert.Equal("audio", await File.ReadAllTextAsync(recycledPath));
    }

    [Fact]
    public async Task TryRecycleFile_NameAlreadyTakenInBin_SuffixesRatherThanOverwrites()
    {
        var root = FileService.GetTempDirectory("recycle-collide-root");
        var bin = FileService.GetTempDirectory("recycle-collide-bin");
        var occupant = await FileService.GetFileAsync(bin, "book.m4b", "first-copy");
        var file = await FileService.GetFileAsync(root, "book.m4b", "second-copy");

        var outcome = global::Listenarr.Infrastructure.FileSystem.FileSystemSafety.TryRecycleFile(
            file,
            [root],
            expectedPhysicalObjectIdentity: null,
            bin,
            relativeSubfolder: null,
            TimeProvider.System,
            out var recycledPath,
            out var reason);

        Assert.Equal(
            global::Listenarr.Infrastructure.FileSystem.RecycleFileOutcome.Recycled,
            outcome);
        Assert.True(string.IsNullOrEmpty(reason), reason);

        // The already-recycled copy must survive. If the rename replaced instead of
        // refusing, this assertion reads "second-copy" and the older file is gone.
        Assert.Equal("first-copy", await File.ReadAllTextAsync(occupant));
        Assert.Equal("second-copy", await File.ReadAllTextAsync(recycledPath));
        Assert.NotEqual(occupant, recycledPath);
    }

    [Fact]
    public async Task TryRecycleFile_TrackedGenerationChanged_RefusesAndLeavesBothSides()
    {
        var root = FileService.GetTempDirectory("recycle-generation-root");
        var bin = FileService.GetTempDirectory("recycle-generation-bin");
        var file = await FileService.GetFileAsync(root, "book.m4b", "audio");

        var outcome = global::Listenarr.Infrastructure.FileSystem.FileSystemSafety.TryRecycleFile(
            file,
            [root],
            expectedPhysicalObjectIdentity: "not-the-identity-of-this-file",
            bin,
            relativeSubfolder: null,
            TimeProvider.System,
            out _,
            out var reason);

        Assert.Equal(
            global::Listenarr.Infrastructure.FileSystem.RecycleFileOutcome.Blocked,
            outcome);
        Assert.False(string.IsNullOrEmpty(reason));

        // A refused recycle must not be a half-move. The file stays where it was and
        // nothing appears in the bin.
        Assert.True(File.Exists(file));
        Assert.Empty(Directory.EnumerateFileSystemEntries(bin));
    }

    [Fact]
    public async Task TryRecycleFile_SubfolderGiven_MirrorsTheLibraryLayout()
    {
        var root = FileService.GetTempDirectory("recycle-subfolder-root");
        var bin = FileService.GetTempDirectory("recycle-subfolder-bin");
        var bookFolder = Path.Join(root, "Author", "Title");
        Directory.CreateDirectory(bookFolder);
        var file = await FileService.GetFileAsync(bookFolder, "01.m4b", "audio");

        var outcome = global::Listenarr.Infrastructure.FileSystem.FileSystemSafety.TryRecycleFile(
            file,
            [root],
            expectedPhysicalObjectIdentity: null,
            bin,
            Path.Join("Author", "Title"),
            TimeProvider.System,
            out var recycledPath,
            out var reason);

        Assert.Equal(
            global::Listenarr.Infrastructure.FileSystem.RecycleFileOutcome.Recycled,
            outcome);
        Assert.True(string.IsNullOrEmpty(reason), reason);
        Assert.Equal(Path.Join(bin, "Author", "Title", "01.m4b"), recycledPath);
        Assert.True(File.Exists(recycledPath));
    }

    [Fact]
    public async Task TryRecycleFile_SubfolderEscapesTheBin_IsFlattenedRatherThanHonoured()
    {
        var root = FileService.GetTempDirectory("recycle-escape-root");
        var bin = FileService.GetTempDirectory("recycle-escape-bin");
        var file = await FileService.GetFileAsync(root, "book.m4b", "audio");

        var outcome = global::Listenarr.Infrastructure.FileSystem.FileSystemSafety.TryRecycleFile(
            file,
            [root],
            expectedPhysicalObjectIdentity: null,
            bin,
            Path.Join("..", "..", "escaped"),
            TimeProvider.System,
            out var recycledPath,
            out var reason);

        Assert.Equal(
            global::Listenarr.Infrastructure.FileSystem.RecycleFileOutcome.Recycled,
            outcome);
        Assert.True(string.IsNullOrEmpty(reason), reason);

        // The property that matters is containment, not the exact name: the traversal
        // segments are dropped and the ordinary one is kept, so the file lands at
        // bin/escaped/book.m4b. If ".." had been honoured the file would be two levels
        // above the bin, which is the failure this guards.
        Assert.Equal(Path.Join(bin, "escaped", "book.m4b"), recycledPath);
        Assert.StartsWith(bin + Path.DirectorySeparatorChar, recycledPath, StringComparison.Ordinal);
        Assert.True(File.Exists(recycledPath));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task TryRecycleFile_EveryCollisionNameTaken_RefusesAndStampedBeforeTrying()
    {
        var root = FileService.GetTempDirectory("recycle-exhaust-root");
        var bin = FileService.GetTempDirectory("recycle-exhaust-bin");
        await FileService.GetFileAsync(bin, "book.m4b", "occupant");
        for (var attempt = 1; attempt < 64; attempt++)
        {
            await FileService.GetFileAsync(bin, $"book_{attempt}.m4b", "occupant");
        }

        var file = await FileService.GetFileAsync(root, "book.m4b", "audio");
        var longAgo = DateTime.UtcNow.AddYears(-5);
        File.SetLastWriteTimeUtc(file, longAgo);

        var outcome = global::Listenarr.Infrastructure.FileSystem.FileSystemSafety.TryRecycleFile(
            file,
            [root],
            expectedPhysicalObjectIdentity: null,
            bin,
            relativeSubfolder: null,
            TimeProvider.System,
            out _,
            out var reason);

        // Every candidate name is taken, so the suffix loop runs out rather than
        // overwriting anything, and the source stays put.
        Assert.Equal(
            global::Listenarr.Infrastructure.FileSystem.RecycleFileOutcome.Blocked,
            outcome);
        Assert.False(string.IsNullOrEmpty(reason));
        Assert.True(File.Exists(file));

        // And this is the ordering assertion. The stamp has to happen BEFORE the rename,
        // or a file sits in the bin carrying an ancient timestamp until the stamp lands,
        // and a sweep running in that window removes it. A failed rename is the only
        // deterministic way to observe which side of the move the stamp is on: if it
        // were stamped afterwards, this file would still read five years old.
        Assert.True(
            File.GetLastWriteTimeUtc(file) > longAgo.AddYears(1),
            "The recycle time was stamped after the rename rather than before it.");
    }

    [Fact]
    public async Task TryRecycleFile_NoBinConfigured_RefusesRatherThanDeleting()
    {
        var root = FileService.GetTempDirectory("recycle-unconfigured-root");
        var file = await FileService.GetFileAsync(root, "book.m4b", "audio");

        var outcome = global::Listenarr.Infrastructure.FileSystem.FileSystemSafety.TryRecycleFile(
            file,
            [root],
            expectedPhysicalObjectIdentity: null,
            recycleBinDirectory: string.Empty,
            relativeSubfolder: null,
            TimeProvider.System,
            out _,
            out var reason);

        Assert.Equal(
            global::Listenarr.Infrastructure.FileSystem.RecycleFileOutcome.Blocked,
            outcome);
        Assert.False(string.IsNullOrEmpty(reason));

        // The important half: an unconfigured bin must never be read as permission to
        // unlink. The caller decides to delete, this primitive never does it by default.
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task TryRecycleFile_RecycleTime_IsStampedSoRetentionAgesFromDeletion()
    {
        var root = FileService.GetTempDirectory("recycle-stamp-root");
        var bin = FileService.GetTempDirectory("recycle-stamp-bin");
        var file = await FileService.GetFileAsync(root, "book.m4b", "audio");

        // An old mtime is the case that matters. Retention measured from this would sweep
        // the file on the first cycle after it was recycled.
        var longAgo = DateTime.UtcNow.AddYears(-5);
        File.SetLastWriteTimeUtc(file, longAgo);

        var before = DateTime.UtcNow.AddSeconds(-5);
        var outcome = global::Listenarr.Infrastructure.FileSystem.FileSystemSafety.TryRecycleFile(
            file,
            [root],
            expectedPhysicalObjectIdentity: null,
            bin,
            relativeSubfolder: null,
            TimeProvider.System,
            out var recycledPath,
            out var reason);

        Assert.Equal(
            global::Listenarr.Infrastructure.FileSystem.RecycleFileOutcome.Recycled,
            outcome);
        Assert.True(string.IsNullOrEmpty(reason), reason);

        var stamped = File.GetLastWriteTimeUtc(recycledPath);
        Assert.True(
            stamped >= before,
            $"Recycle time was not stamped; the moved file still reads {stamped:o}, near the original {longAgo:o}.");
    }
}
