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
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Tests.Features.Api.Features.Library
{
    /// <summary>
    /// The recursive folder-contents branch of the filesystem delete, which is the branch a
    /// normal single-item delete takes: the book has its own folder under a root. The
    /// per-tracked-file branch is covered by the recycle tests in the main file.
    /// </summary>
    public partial class LibraryController_DeleteFilesystemTests
    {
        private static readonly DateTime LongAgoUtc =
            new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private sealed record FolderBook(
            Audiobook Audiobook,
            string BookFolder,
            IReadOnlyDictionary<string, string> Contents);

        /// <summary>
        /// A book in its own folder with a tracked audio file, an untracked sidecar and a
        /// nested folder. Every file is back-dated so a test can tell a recycled file that
        /// was stamped from one that kept its original timestamp.
        /// </summary>
        private async Task<FolderBook> ArrangeFolderBookAsync(
            int id,
            string bookFolder,
            string? rootPath,
            string audioRelativePath = "Jack of Shadows.mp3")
        {
            var contents = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [audioRelativePath] = "audio",
                ["cover.jpg"] = "cover",
                [Path.Join("Extras", "notes.txt")] = "notes"
            };
            foreach (var (relative, content) in contents)
            {
                var path = Path.Join(bookFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content);
                File.SetLastWriteTimeUtc(path, LongAgoUtc);
            }

            if (rootPath != null)
            {
                await AddAuthorizedRootAsync(new RootFolderBuilder()
                    .WithId(id)
                    .WithPath(rootPath)
                    .Build());
            }

            var audioPath = Path.Join(bookFolder, audioRelativePath);
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithId(id)
                .WithTitle($"Folder Recycle Book {id}")
                .WithBasePath(bookFolder)
                .WithFilePath(audioPath)
                .Build());
            await AddTrackedGenerationAsync(audiobook, audioPath);
            var reloaded = await _audiobookRepository.GetByIdAsync(audiobook.Id);
            Assert.NotNull(reloaded);
            return new FolderBook(reloaded!, bookFolder, contents);
        }

        private async Task ConfigureRecycleBinAsync(string binPath) =>
            await _applicationSettingsRepository.SaveAsync(
                new ApplicationSettingsBuilder()
                    .WithRecycleBinPath(binPath)
                    .Build());

        private static async Task AssertRecycledIntoAsync(
            string recycledFolder,
            FolderBook book,
            DateTime notBeforeUtc)
        {
            foreach (var (relative, content) in book.Contents)
            {
                var recycled = Path.Join(recycledFolder, relative);
                Assert.True(File.Exists(recycled), $"'{relative}' did not reach the recycle bin.");
                Assert.Equal(content, await File.ReadAllTextAsync(recycled));

                // Retention ages a bin entry by its last write time. A file that kept its
                // 2001 timestamp would be removed by the very next sweep.
                Assert.True(
                    File.GetLastWriteTimeUtc(recycled) >= notBeforeUtc,
                    $"'{relative}' kept its original timestamp, so retention would sweep it at once.");
            }
        }

        /// <summary>
        /// Every file the arrangement created must exist in exactly one place, the library
        /// or the bin, with its bytes intact. This is the property a failed recycle must
        /// keep: whatever else goes wrong, nothing is destroyed.
        /// </summary>
        private static async Task AssertNothingLostAsync(FolderBook book, string binPath)
        {
            var binFiles = Directory.Exists(binPath)
                ? Directory.EnumerateFiles(binPath, "*", SearchOption.AllDirectories).ToList()
                : [];
            foreach (var (relative, content) in book.Contents)
            {
                var original = Path.Join(book.BookFolder, relative);
                var inLibrary = File.Exists(original);
                var inBin = binFiles
                    .Where(path => path.EndsWith(
                        Path.DirectorySeparatorChar + relative,
                        StringComparison.Ordinal))
                    .ToList();
                Assert.True(
                    inLibrary ^ inBin.Count == 1,
                    $"'{relative}' was in the library: {inLibrary}, and in the bin {inBin.Count} times.");
                var survivor = inLibrary ? original : inBin[0];
                Assert.Equal(content, await File.ReadAllTextAsync(survivor));
            }
        }

        [Fact]
        public async Task DeleteFolder_NoRecycleBinConfigured_StillDeletesPermanently()
        {
            var root = FileService.GetTempDirectory("listenarr-folder-nobin");
            var unconfiguredBin = FileService.GetTempDirectory("listenarr-folder-nobin-bin");
            var book = await ArrangeFolderBookAsync(
                700,
                Path.Join(root, "Author", "Jack of Shadows"),
                root);

            var result = await _provider.GetRequiredService<LibraryController>()
                .DeleteAudiobook(book.Audiobook.Id, deleteFiles: true, deleteFolder: true);

            // The control for everything below: the same arrangement with no bin reaches
            // the recursive branch and removes the folder outright, so a later test that
            // finds the files in the bin is measuring the bin and not a delete that never
            // ran.
            Assert.IsType<OkObjectResult>(result);
            Assert.False(Directory.Exists(book.BookFolder));
            Assert.Empty(Directory.EnumerateFileSystemEntries(unconfiguredBin));
        }

        [Fact]
        public async Task DeleteFolder_RecycleBinConfigured_MovesTheFolderContentsIntoTheBin()
        {
            var root = FileService.GetTempDirectory("listenarr-folder-bin");
            var bin = FileService.GetTempDirectory("listenarr-folder-bin-bin");
            await ConfigureRecycleBinAsync(bin);
            var book = await ArrangeFolderBookAsync(
                701,
                Path.Join(root, "Author", "Jack of Shadows"),
                root);
            var started = DateTime.UtcNow.AddSeconds(-5);

            var result = await _provider.GetRequiredService<LibraryController>()
                .DeleteAudiobook(book.Audiobook.Id, deleteFiles: true, deleteFolder: true);

            Assert.IsType<OkObjectResult>(result);
            Assert.False(Directory.Exists(book.BookFolder));

            // The library layout is mirrored in the bin, the same way the per-file branch
            // does it, so the book is found where it was rather than flattened.
            await AssertRecycledIntoAsync(
                Path.Join(bin, "Author", "Jack of Shadows"),
                book,
                started);
        }

        [Fact]
        public async Task DeleteFilesOnly_RecycleBinConfigured_RecyclesContentsAndKeepsTheFolder()
        {
            var root = FileService.GetTempDirectory("listenarr-folder-bin-keep");
            var bin = FileService.GetTempDirectory("listenarr-folder-bin-keep-bin");
            await ConfigureRecycleBinAsync(bin);
            var book = await ArrangeFolderBookAsync(
                702,
                Path.Join(root, "Author", "Jack of Shadows"),
                root);
            var started = DateTime.UtcNow.AddSeconds(-5);

            var result = await _provider.GetRequiredService<LibraryController>()
                .DeleteAudiobook(book.Audiobook.Id, deleteFiles: true, deleteFolder: false);

            Assert.IsType<OkObjectResult>(result);
            Assert.True(Directory.Exists(book.BookFolder));
            Assert.Empty(Directory.EnumerateFileSystemEntries(book.BookFolder));
            await AssertRecycledIntoAsync(
                Path.Join(bin, "Author", "Jack of Shadows"),
                book,
                started);
        }

        [Fact]
        public async Task DeleteFolder_SameFolderAlreadyInBin_KeepsTheEarlierCopyAndRecyclesBesideIt()
        {
            var root = FileService.GetTempDirectory("listenarr-folder-bin-collide");
            var bin = FileService.GetTempDirectory("listenarr-folder-bin-collide-bin");
            await ConfigureRecycleBinAsync(bin);
            var earlier = Path.Join(bin, "Author", "Jack of Shadows");
            Directory.CreateDirectory(earlier);
            var earlierAudio = Path.Join(earlier, "Jack of Shadows.mp3");
            await File.WriteAllTextAsync(earlierAudio, "earlier deletion");
            var book = await ArrangeFolderBookAsync(
                703,
                Path.Join(root, "Author", "Jack of Shadows"),
                root);
            var started = DateTime.UtcNow.AddSeconds(-5);

            var result = await _provider.GetRequiredService<LibraryController>()
                .DeleteAudiobook(book.Audiobook.Id, deleteFiles: true, deleteFolder: true);

            Assert.IsType<OkObjectResult>(result);

            // Readarr merges into an existing bin folder and overwrites same-named files
            // (DiskTransferService.TransferFolder -> TransferFile(..., overwrite: true)).
            // That destroys the earlier deletion, which is the one thing a bin must not do.
            Assert.Equal("earlier deletion", await File.ReadAllTextAsync(earlierAudio));
            Assert.Single(Directory.EnumerateFiles(earlier, "*", SearchOption.AllDirectories));
            await AssertRecycledIntoAsync(
                Path.Join(bin, "Author", "Jack of Shadows_1"),
                book,
                started);
        }

        [Fact]
        public async Task DeleteFolder_BinInsideTheFolderBeingDeleted_RefusesAndTouchesNothing()
        {
            // The book folder is outside every root, so bin validation (which refuses a
            // bin inside a ROOT) has nothing to say about it. Only the delete can catch a
            // bin that sits inside the very folder it is about to empty.
            var root = FileService.GetTempDirectory("listenarr-folder-bin-nested-root");
            var outside = FileService.GetTempDirectory("listenarr-folder-bin-nested");
            var bookFolder = Path.Join(outside, "Jack of Shadows");
            var bin = Path.Join(bookFolder, "recycle");
            Directory.CreateDirectory(bin);
            await ConfigureRecycleBinAsync(bin);
            var book = await ArrangeFolderBookAsync(704, bookFolder, root);

            var result = await _provider.GetRequiredService<LibraryController>()
                .DeleteAudiobook(book.Audiobook.Id, deleteFiles: true, deleteFolder: true);

            var failure = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, failure.StatusCode);
            foreach (var relative in book.Contents.Keys)
            {
                Assert.True(File.Exists(Path.Join(bookFolder, relative)), $"'{relative}' was moved or deleted.");
            }
            Assert.Empty(Directory.EnumerateFileSystemEntries(bin));
            Assert.NotNull(await _audiobookRepository.GetByIdAsync(book.Audiobook.Id));
        }

        [Fact]
        public async Task DeleteFolder_BookOutsideEveryRoot_NoBin_ReachesTheRecursiveDelete()
        {
            // Control for the two nesting tests: the same out-of-root arrangement with no
            // bin configured does reach the recursive branch and empties the folder. If
            // it did not, a refusal in those tests would prove nothing about the bin.
            var root = FileService.GetTempDirectory("listenarr-folder-outside-root");
            var outside = FileService.GetTempDirectory("listenarr-folder-outside");
            var book = await ArrangeFolderBookAsync(
                707,
                Path.Join(outside, "Jack of Shadows"),
                root);

            var result = await _provider.GetRequiredService<LibraryController>()
                .DeleteAudiobook(book.Audiobook.Id, deleteFiles: true, deleteFolder: true);

            Assert.IsType<OkObjectResult>(result);
            Assert.False(Directory.Exists(book.BookFolder));
        }

        [Fact]
        public async Task DeleteFolder_FolderInsideTheBin_RefusesAndTouchesNothing()
        {
            var root = FileService.GetTempDirectory("listenarr-folder-in-bin-root");
            var bin = FileService.GetTempDirectory("listenarr-folder-in-bin");
            await ConfigureRecycleBinAsync(bin);
            var book = await ArrangeFolderBookAsync(
                708,
                Path.Join(bin, "Jack of Shadows"),
                root);

            var result = await _provider.GetRequiredService<LibraryController>()
                .DeleteAudiobook(book.Audiobook.Id, deleteFiles: true, deleteFolder: true);

            var failure = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, failure.StatusCode);
            foreach (var relative in book.Contents.Keys)
            {
                Assert.True(File.Exists(Path.Join(book.BookFolder, relative)), $"'{relative}' was moved or deleted.");
            }
            Assert.Single(Directory.EnumerateFileSystemEntries(bin));
        }

        [LinuxFact]
        public async Task DeleteFolder_RecycleFailsPartWay_ReportsFailureAndLosesNothing()
        {
            var root = FileService.GetTempDirectory("listenarr-folder-bin-midway");
            var bin = FileService.GetTempDirectory("listenarr-folder-bin-midway-bin");
            await ConfigureRecycleBinAsync(bin);
            var book = await ArrangeFolderBookAsync(
                705,
                Path.Join(root, "Author", "Jack of Shadows"),
                root,
                audioRelativePath: Path.Join("Disc 2", "Jack of Shadows.mp3"));

            // The tracked file sits in a folder the process can read but not write, so
            // moving it out fails after the preflight has already passed. Renaming needs
            // write access to the source directory; reading and opening do not.
            var lockedFolder = Path.Join(book.BookFolder, "Disc 2");
            File.SetUnixFileMode(
                lockedFolder,
                UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                Assert.ThrowsAny<UnauthorizedAccessException>(() =>
                    File.WriteAllText(Path.Join(lockedFolder, "probe"), "x"));

                var result = await _provider.GetRequiredService<LibraryController>()
                    .DeleteAudiobook(book.Audiobook.Id, deleteFiles: true, deleteFolder: true);

                var failure = Assert.IsType<ObjectResult>(result);
                Assert.Equal(500, failure.StatusCode);
                Assert.NotNull(await _audiobookRepository.GetByIdAsync(book.Audiobook.Id));
                Assert.True(File.Exists(Path.Join(book.BookFolder, "Disc 2", "Jack of Shadows.mp3")));
                await AssertNothingLostAsync(book, bin);
            }
            finally
            {
                File.SetUnixFileMode(
                    lockedFolder,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        [CrossVolumeFact]
        public async Task DeleteFolder_BinOnAnotherFilesystem_RefusesBeforeMovingAnything()
        {
            var root = FileService.GetTempDirectory("listenarr-folder-bin-xdev");
            var bin = Path.Join(
                Environment.GetEnvironmentVariable(
                    CrossVolumeFactAttribute.DestinationPathEnvironmentVariable)!,
                $"listenarr-folder-bin-xdev-{Guid.NewGuid():N}");
            Directory.CreateDirectory(bin);
            try
            {
                await ConfigureRecycleBinAsync(bin);
                var book = await ArrangeFolderBookAsync(
                    706,
                    Path.Join(root, "Author", "Jack of Shadows"),
                    root);

                var result = await _provider.GetRequiredService<LibraryController>()
                    .DeleteAudiobook(book.Audiobook.Id, deleteFiles: true, deleteFolder: true);

                // A rename cannot cross filesystems. Refusing up front is the only outcome
                // that is safe: a per-file move that discovered the boundary half way
                // would leave a book split between the library and the bin.
                var failure = Assert.IsType<ObjectResult>(result);
                Assert.Equal(500, failure.StatusCode);
                foreach (var relative in book.Contents.Keys)
                {
                    Assert.True(File.Exists(Path.Join(book.BookFolder, relative)), $"'{relative}' was moved or deleted.");
                }
                Assert.Empty(Directory.EnumerateFiles(bin, "*", SearchOption.AllDirectories));
            }
            finally
            {
                Directory.Delete(bin, recursive: true);
            }
        }
    }
}
