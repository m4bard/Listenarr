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

namespace Listenarr.Tests.Features.Application.Search.Parsing
{
    /// <summary>
    /// The title and position strings here are real ones. The bundle cases and most of the
    /// awkward negatives are taken from books that exist in the public m4bard/listenarr-testdata
    /// corpus, which is machine-verified against live Audnex metadata, so a maintainer can
    /// generate a library containing them and see the same answers. Inventing fixtures is how
    /// a detector ends up only ever asked the question in the form where it happens to be right.
    /// </summary>
    [Trait("Category", "Search")]
    [Trait("Name", "ReleaseShapeDetectorTests")]
    public class ReleaseShapeDetectorTests : BaseTests
    {
        [Theory]
        // Real Audible positions on omnibus editions.
        [InlineData("1-4")]     // The Father Brown Collection: Books 1-4
        [InlineData("1-2")]     // The Thirty-Nine Steps (Richard Hannay)
        [InlineData("1-6")]     // The Barsoom Collection: Books 1-6
        [InlineData("1-8")]     // Anne Of Green Gables -Complete 8-Book Box Set
        [InlineData("1 - 4")]
        [InlineData("1-3, 5")]
        [InlineData("2, 3")]
        public void IsBundleSeriesNumber_TrueForPositionsCoveringMoreThanOneBook(string seriesNumber)
        {
            Assert.True(ReleaseShapeDetector.IsBundleSeriesNumber(seriesNumber));
        }

        [Theory]
        [InlineData("1")]
        [InlineData("0")]               // a real corpus position: a prequel slot
        [InlineData("12")]
        [InlineData("13")]
        // A fractional position is a novella between two books, not a bundle. Upstream #795
        // exists about these, and a "contains something that is not a digit" check calls it wrong.
        [InlineData("1.5")]
        // "2, Dramatized" is a real Audible position. It has a comma and it is one book, so a
        // comma alone cannot be the signal.
        [InlineData("2, Dramatized")]
        [InlineData("Dramatized")]
        // Grouped digits are one number, not a list of two.
        [InlineData("20,000")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void IsBundleSeriesNumber_FalseForASinglePosition(string? seriesNumber)
        {
            Assert.False(ReleaseShapeDetector.IsBundleSeriesNumber(seriesNumber));
        }

        [Theory]
        // Corpus titles.
        [InlineData("The Father Brown Collection: Books 1-4")]
        [InlineData("The Barsoom Collection: Books 1-6")]
        [InlineData("Anne Of Green Gables -Complete 8-Book Box Set")]
        [InlineData("Daniel Defoe Box Set: A Journal of the Plague Year & The Apparition of Mrs. Veal")]
        // Release-title shapes, which are not the tidy product titles above.
        [InlineData("Edgar Rice Burroughs - The Barsoom Collection Books 1-6 (Omnibus) [M4B 64k]")]
        [InlineData("Father Brown Mysteries Vol. 1-3 unabridged")]
        [InlineData("Sherlock Holmes - Volumes 1 - 12 [MP3]")]
        [InlineData("The Complete Trilogy")]
        [InlineData("Box Set")]
        [InlineData("Dune 1-6 [M4B]")]
        [InlineData("Anne of Green Gables Complete Series")]
        public void LooksLikeBundle_TrueForMultiBookReleaseTitles(string title)
        {
            Assert.True(ReleaseShapeDetector.LooksLikeBundle(title));
        }

        [Theory]
        // The adversarial ones. Each has digits and, in some cases, a hyphen.
        [InlineData("Catch-22")]
        [InlineData("1984")]
        [InlineData("813")]                                   // corpus: Leblanc
        [InlineData("20,000 Leagues Under the Sea")]           // corpus: Verne
        [InlineData("100% - The Story of a Patriot")]          // corpus: Sinclair
        [InlineData("The Thirty-Nine Steps")]                  // corpus: Buchan
        [InlineData("Arthur Conan Doyle - A Study in Scarlet 1887-1927 [M4B]")]
        [InlineData("A Princess of Mars [MP3 64-128kbps]")]
        // Ordinary single books that mention a book or a part.
        [InlineData("Book One")]
        [InlineData("Part 1")]
        [InlineData("The Jungle Book")]                        // corpus: Kipling
        [InlineData("The Second Jungle Book")]                 // corpus: Kipling
        [InlineData("Volume 1")]
        [InlineData("1 Book")]
        [InlineData("")]
        [InlineData(null)]
        public void LooksLikeBundle_FalseForSingleBookReleaseTitles(string? title)
        {
            Assert.False(ReleaseShapeDetector.LooksLikeBundle(title));
        }

        [Fact]
        public void LooksLikeBundle_UsesTheSameVocabularyAsTheAudiobookOnlyFilter()
        {
            // The filter and the detector read one list. If somebody adds a phrase to the
            // filter's copy this stays true; if somebody reintroduces a second copy, it does not.
            Assert.All(
                ReleaseShapeDetector.BundlePhrases,
                phrase => Assert.True(
                    ReleaseShapeDetector.LooksLikeBundle($"Some Author - {phrase} [M4B]"),
                    $"'{phrase}' is in BundlePhrases but LooksLikeBundle does not recognise it"));
        }

        [Fact]
        public void LooksLikeBundle_IsCaseInsensitive()
        {
            Assert.True(ReleaseShapeDetector.LooksLikeBundle("some author - box set [m4b]"));
            Assert.True(ReleaseShapeDetector.LooksLikeBundle("SOME AUTHOR - BOOKS 1-4"));
        }
    }
}
