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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Listenarr.Tests.Features.Infrastructure.Library.Scanning
{
    /// <summary>
    /// Path attribution in ScanFileDiscovery, exercised against real directory trees.
    ///
    /// When an audiobook has no BasePath the scan root falls back to the whole library, so every
    /// audio file under it becomes a candidate and attribution rests entirely on the path.
    /// Books used below are public domain (H. Rider Haggard, Jules Verne).
    /// </summary>
    public class ScanFileDiscoveryReproTests : IDisposable
    {
        private readonly string _root;

        public ScanFileDiscoveryReproTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "listenarr-scan-" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
            GC.SuppressFinalize(this);
        }

        private void AddBook(string relativeFolder, params string[] fileNames)
        {
            var dir = Path.Combine(_root, relativeFolder);
            Directory.CreateDirectory(dir);
            foreach (var name in fileNames)
            {
                File.WriteAllText(Path.Combine(dir, name), "not really audio");
            }
        }

        private static Audiobook Book(string title, string author) => new()
        {
            Title = title,
            Authors = new List<string> { author },
        };

        private List<string> Scan(Audiobook audiobook) =>
            ScanFileDiscovery.FindMatchingAudioFiles(_root, audiobook, Guid.NewGuid(), NullLogger.Instance);

        private static List<string> Names(IEnumerable<string> paths) =>
            paths.Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList()!;

        // -----------------------------------------------------------------
        // The bug: the author names a shelf, not a book.
        // -----------------------------------------------------------------

        [Fact]
        public void ScanningOneBook_DoesNotClaimTheOtherBooksByTheSameAuthor()
        {
            AddBook("H. Rider Haggard/H. Rider Haggard - She", "She.m4b");
            AddBook("H. Rider Haggard/H. Rider Haggard - Allan Quatermain", "Allan Quatermain.m4b");
            AddBook("H. Rider Haggard/H. Rider Haggard - King Solomon's Mines", "King Solomon's Mines.m4b");

            var found = Scan(Book("She", "H. Rider Haggard"));

            Assert.Equal(new List<string> { "She.m4b" }, Names(found));
        }

        [Fact]
        public void BasePath_IsTheBookFolder_NotTheAuthorFolder()
        {
            AddBook("H. Rider Haggard/H. Rider Haggard - She", "She.m4b");
            AddBook("H. Rider Haggard/H. Rider Haggard - Allan Quatermain", "Allan Quatermain.m4b");

            var basePath = ScanPathPlanner.CalculateBasePath(Scan(Book("She", "H. Rider Haggard")));

            // If the author counted as a match, the common parent of every Haggard file would be
            // the AUTHOR folder -- and that would be stored as this book's BasePath, which is
            // what rename and move then operate on.
            Assert.EndsWith("H. Rider Haggard - She", basePath);
        }

        // -----------------------------------------------------------------
        // Layouts that must keep working.
        // -----------------------------------------------------------------

        [Fact]
        public void TitleInTheFolderName_Matches()
        {
            // The common Audnex/Plex shape: {Author}/{Author} - {Series} - {Title}/
            AddBook("Jules Verne/Jules Verne - Captain Nemo - Twenty Thousand Leagues Under the Sea",
                "Twenty Thousand Leagues Under the Sea.m4b");

            var found = Scan(Book("Twenty Thousand Leagues Under the Sea", "Jules Verne"));

            Assert.Single(found);
        }

        [Fact]
        public void TitleInTheFileName_Matches_EvenWhenTheFolderDoesNotCarryIt()
        {
            AddBook("Jules Verne/Audiobooks", "Around the World in Eighty Days.mp3");

            var found = Scan(Book("Around the World in Eighty Days", "Jules Verne"));

            Assert.Single(found);
        }

        [Fact]
        public void EveryFileInAMatchedBookFolder_IsClaimed()
        {
            // A multi-part book: the folder identifies it, so all its parts belong to it --
            // including parts whose own filenames do not repeat the title.
            AddBook("Jules Verne/1870 - Twenty Thousand Leagues Under the Sea",
                "Part 01.mp3", "Part 02.mp3", "Part 03.mp3");

            var found = Scan(Book("Twenty Thousand Leagues Under the Sea", "Jules Verne"));

            Assert.Equal(3, found.Count);
        }

        [Fact]
        public void ASiblingBookInTheSameSeriesFolder_IsNotClaimed()
        {
            AddBook("Jules Verne/Captain Nemo/1870 - Twenty Thousand Leagues Under the Sea", "book.m4b");
            AddBook("Jules Verne/Captain Nemo/1874 - The Mysterious Island", "book.m4b");

            var found = Scan(Book("The Mysterious Island", "Jules Verne"));

            Assert.Single(found);
            Assert.Contains("The Mysterious Island", found[0]);
        }

        // -----------------------------------------------------------------
        // The trade-off, stated explicitly.
        // -----------------------------------------------------------------

        [Fact]
        public void APathCarryingNeitherTitleNorAnythingIdentifying_IsLeftUnmatched()
        {
            // Previously this matched -- on the author alone -- and so did every other book by
            // the same author. Unmatched is the correct outcome for a path that cannot identify
            // a book: a miss is recoverable, a wrong link is not. Embedded tags are what should
            // claim these.
            AddBook("Elfie Donnelly/Bibi und Tina/61", "61 - track.mp3");

            var found = Scan(Book("Retten die Biber", "Elfie Donnelly"));

            Assert.Empty(found);
        }
    }
}
