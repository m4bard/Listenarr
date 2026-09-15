using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search
{
    [Trait("Name", "AudiobookSearchQueryBuilderTests")]
    [Trait("Category", "AudiobookSearchQueryBuilder")]
    public sealed class AudiobookSearchQueryBuilderTests : BaseTests
    {
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
