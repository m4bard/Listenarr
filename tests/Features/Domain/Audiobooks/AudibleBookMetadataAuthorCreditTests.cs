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

namespace Listenarr.Tests.Features.Domain.Audiobooks
{
    /// <summary>
    /// What reaches storage when provider metadata becomes a library row.
    /// </summary>
    /// <remarks>
    /// The filter is applied here rather than on the way out, so that the stored author list is
    /// the one every consumer sees. The ASIN resolver is the reason it matters: it walks the
    /// stored names, looks each one up, and pairs any name that matches a book with that book's
    /// first resolved ASIN, so a translator left in the list is a translator wearing somebody
    /// else's identifier.
    /// </remarks>
    [Trait("Name", nameof(AudibleBookMetadataAuthorCreditTests))]
    [Trait("Category", "AuthorCredits")]
    public class AudibleBookMetadataAuthorCreditTests : BaseTests
    {
        private static AudibleBookMetadata Metadata(params string[] authors) =>
            new() { Title = "Untitled", Asin = "B000000000", Authors = authors.ToList() };

        [Fact]
        public void ToAudiobook_DoesNotStoreATranslatorAsAnAuthor()
        {
            var audiobook = Metadata("Fyodor Dostoevsky", "Constance Garnett - translator").ToAudiobook();

            Assert.Equal(new[] { "Fyodor Dostoevsky" }, audiobook.Authors);
        }

        [Fact]
        public void ToAudiobook_StoresTheAuthorWhenTheTranslatorIsCreditedFirst()
        {
            var audiobook = Metadata("Constance Garnett - translator", "Fyodor Dostoevsky").ToAudiobook();

            Assert.Equal(new[] { "Fyodor Dostoevsky" }, audiobook.Authors);
        }

        [Fact]
        public void ToAudiobook_LeavesASingleAuthorAlone()
        {
            // Control. Passes before the change as well as after, so a filter that had started
            // emptying the list wholesale would be caught here rather than looking like a pass.
            var audiobook = Metadata("Edgar Rice Burroughs").ToAudiobook();

            Assert.Equal(new[] { "Edgar Rice Burroughs" }, audiobook.Authors);
        }

        [Fact]
        public void ToAudiobook_KeepsBothGenuineCoAuthors()
        {
            var audiobook = Metadata("O. Henry", "William Sydney Porter").ToAudiobook();

            Assert.Equal(new[] { "O. Henry", "William Sydney Porter" }, audiobook.Authors);
        }

        [Fact]
        public void ToAudiobook_KeepsTheEditorsOfAnAnthologyCreditedToNobodyElse()
        {
            var credits = new[] { "Lisa Morton - editor", "Leslie S. Klinger - editor" };

            var audiobook = Metadata(credits).ToAudiobook();

            Assert.Equal(credits, audiobook.Authors);
        }

        [Fact]
        public void ToAudiobook_StillFallsBackToTheSingularAuthorField()
        {
            // The singular Author field is the fallback when no list arrives, and filtering
            // must not have swallowed it.
            var audiobook = new AudibleBookMetadata
            {
                Title = "Untitled",
                Asin = "B000000000",
                Author = "Jules Verne"
            }.ToAudiobook();

            Assert.Equal(new[] { "Jules Verne" }, audiobook.Authors);
        }

        [Fact]
        public void ToAudiobook_LeavesTheAuthorAsinListForTheResolverToFill()
        {
            // The pairing this fix is meant to protect: with one stored author there is at most
            // one resolved ASIN, so "the first ASIN on the book" and "this author's ASIN" are
            // the same value rather than two that can drift apart.
            var audiobook = Metadata("Fyodor Dostoevsky", "Constance Garnett - translator").ToAudiobook();

            Assert.Single(audiobook.Authors!);
            Assert.True(audiobook.AuthorAsins == null || audiobook.AuthorAsins.Count <= 1);
        }
    }
}
