using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search
{
    [Trait("Name", "AudiobookSearchQueryBuilderTests")]
    [Trait("Category", "AudiobookSearchQueryBuilder")]
    public sealed class AudiobookSearchQueryBuilderTests : BaseTests
    {
        private static Audiobook Book(string? title, string? author = null, string? series = null)
        {
            return new Audiobook
            {
                Title = title,
                Authors = author == null ? null : new List<string> { author },
                Series = series
            };
        }

        [Fact]
        [Trait("Method", "Build")]
        public void Build_BothEntryPointsProduceTheSameQuery()
        {
            // The automatic sweep and the download path used to build queries from
            // different field sets. Whatever the shared builder decides, they agree.
            var audiobook = Book("The Wonderful Wizard of Oz", "L. Frank Baum", "Oz");
            var classifier = new AutomaticSearchResultClassifier(
                Mock.Of<ILogger>());

            var automaticQuery = classifier.BuildSearchQuery(audiobook);
            var downloadQuery = DownloadSearchQueryBuilder.Build(audiobook);

            Assert.Equal(automaticQuery, downloadQuery);
            Assert.Equal("The Wonderful Wizard of Oz L. Frank Baum", downloadQuery);
        }

        [Fact]
        [Trait("Method", "Build")]
        public void Build_BothEntryPointsAgreeWhenTheSeriesIsNotInTheTitle()
        {
            // The agreement must not be an accident of one example. Here the series
            // survives into the query, and both paths still say the same thing.
            var audiobook = Book("Dracula", "Bram Stoker", "Gothic Horror");
            var classifier = new AutomaticSearchResultClassifier(
                Mock.Of<ILogger>());

            var automaticQuery = classifier.BuildSearchQuery(audiobook);
            var downloadQuery = DownloadSearchQueryBuilder.Build(audiobook);

            Assert.Equal(automaticQuery, downloadQuery);
            Assert.Equal("Dracula Bram Stoker Gothic Horror", downloadQuery);
        }

        [Fact]
        [Trait("Method", "Build")]
        public void Build_DoesNotRepeatASeriesAlreadyInTheTitle()
        {
            var query = AudiobookSearchQueryBuilder.Build(
                Book("The Wonderful Wizard of Oz", "L. Frank Baum", "Oz"));

            Assert.Equal("The Wonderful Wizard of Oz L. Frank Baum", query);
        }

        [Fact]
        [Trait("Method", "Build")]
        public void Build_MatchesASeriesInTheTitleIgnoringCaseAndPunctuation()
        {
            var query = AudiobookSearchQueryBuilder.Build(
                Book("Alice's Adventures in Wonderland", "Lewis Carroll", "alices adventures"));

            Assert.Equal("Alice's Adventures in Wonderland Lewis Carroll", query);
        }

        [Fact]
        [Trait("Method", "Build")]
        public void Build_AppendsASeriesTheTitleOnlyResemblesInPart()
        {
            // "Oz" is a word run in "Wizard of Oz" but not in "Ozymandias". A raw
            // substring test would drop the series here and lose real information.
            var query = AudiobookSearchQueryBuilder.Build(
                Book("Ozymandias", "Percy Bysshe Shelley", "Oz"));

            Assert.Equal("Ozymandias Percy Bysshe Shelley Oz", query);
        }

        [Fact]
        [Trait("Method", "Build")]
        public void Build_StripsAnEditionAnnotationBeforeTheSeriesContainmentCheck()
        {
            var query = AudiobookSearchQueryBuilder.Build(
                Book("The Marvelous Land of Oz (Unabridged)", "L. Frank Baum", "Oz"));

            Assert.Equal("The Marvelous Land of Oz L. Frank Baum", query);
        }

        [Fact]
        [Trait("Method", "Build")]
        public void Build_OmitsMissingFieldsWithoutLeavingSeparators()
        {
            Assert.Equal("Frankenstein", AudiobookSearchQueryBuilder.Build(Book("Frankenstein")));
            Assert.Equal(
                "Frankenstein Mary Shelley",
                AudiobookSearchQueryBuilder.Build(Book("Frankenstein", "Mary Shelley", "   ")));
            Assert.Equal(string.Empty, AudiobookSearchQueryBuilder.Build(Book(null)));
        }

        [Theory]
        [Trait("Method", "BuildQueryTitle")]
        [InlineData("Moby Dick (Unabridged)", "Moby Dick")]
        [InlineData("Moby Dick [Unabridged]", "Moby Dick")]
        [InlineData("Moby Dick (unabridged)", "Moby Dick")]
        [InlineData("Moby Dick (Abridged)", "Moby Dick")]
        [InlineData("Moby Dick (Unabridged Edition)", "Moby Dick")]
        [InlineData("Moby Dick (Dramatized Adaptation)", "Moby Dick")]
        [InlineData("Moby Dick (Dramatised Adaptation)", "Moby Dick")]
        [InlineData("Moby Dick (Audio Drama)", "Moby Dick")]
        [InlineData("Moby Dick (Unabridged) (Dramatized)", "Moby Dick")]
        public void BuildQueryTitle_RemovesDelimitedEditionAnnotations(string title, string expected)
        {
            Assert.Equal(expected, AudiobookSearchQueryBuilder.BuildQueryTitle(title));
        }

        [Theory]
        [Trait("Method", "BuildQueryTitle")]
        // A part number says which half of the work is wanted. Removing it would turn a
        // search for one release into a search for either, so it stays.
        [InlineData("Les Miserables (Part 1 of 5)")]
        [InlineData("The Marvelous Land of Oz, Book 2")]
        // Delimited, but not an edition annotation. These are title words.
        [InlineData("Hamlet (Prince of Denmark)")]
        [InlineData("The Rime of the Ancient Mariner (1834 Text)")]
        // No delimiters, so nothing is a candidate for removal at all.
        [InlineData("Unabridged Dictionary of the English Language")]
        [InlineData("An Abridged History of Rome")]
        public void BuildQueryTitle_KeepsTitleTextItMustNotRemove(string title)
        {
            Assert.Equal(title, AudiobookSearchQueryBuilder.BuildQueryTitle(title));
        }

        [Fact]
        [Trait("Method", "BuildQueryTitle")]
        public void BuildQueryTitle_KeepsTheStoredTitleWhenStrippingWouldEmptyIt()
        {
            Assert.Equal("(Unabridged)", AudiobookSearchQueryBuilder.BuildQueryTitle("(Unabridged)"));
        }

        [Fact]
        [Trait("Method", "BuildQueryTitle")]
        public void BuildQueryTitle_CollapsesWhitespaceLeftBehindByAStrippedAnnotation()
        {
            Assert.Equal(
                "Twenty Thousand Leagues Under the Sea",
                AudiobookSearchQueryBuilder.BuildQueryTitle(
                    "Twenty Thousand Leagues  (Unabridged) Under the Sea"));
            Assert.Equal(
                "Treasure Island",
                AudiobookSearchQueryBuilder.BuildQueryTitle("Treasure Island, (Unabridged)"));
        }

        [Fact]
        [Trait("Method", "BuildQueryTitle")]
        public void BuildQueryTitle_ReturnsEmptyForAnAbsentTitle()
        {
            Assert.Equal(string.Empty, AudiobookSearchQueryBuilder.BuildQueryTitle(null));
            Assert.Equal(string.Empty, AudiobookSearchQueryBuilder.BuildQueryTitle("   "));
        }
    }
}
