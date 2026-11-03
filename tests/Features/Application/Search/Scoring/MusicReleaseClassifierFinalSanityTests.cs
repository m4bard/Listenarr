using Listenarr.Tests.Common;
using Xunit.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Scoring
{
    [Trait("Name", "MusicReleaseClassifierFinalSanityTests")]
    [Trait("Category", "Application")]
    public sealed class MusicReleaseClassifierFinalSanityTests : BaseTests
    {
        private static SearchResult Result(string? category = null, string? title = "Some Audiobook Title", string? artist = null, string? album = null, long size = 0)
        {
            return new SearchResult { Title = title ?? string.Empty, Category = category ?? string.Empty, Artist = artist ?? string.Empty, Album = album ?? string.Empty, Size = size };
        }

        [Fact]
        public void FinalSanity_OrdinaryAudiobook_NoSignalsAtAll_IsAccepted()
        {
            var result = Result(category: "3030", title: "A Quiet Evening by the Sea");
            Assert.False(MusicReleaseClassifier.LooksLikeMusicRelease(result, out _));
        }

        [Fact]
        public void FinalSanity_DecisiveMusicCategory_StillRejectsPostRefactor()
        {
            var result = Result(category: "3010", title: "A Quiet Evening by the Sea");
            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);
            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }
    }
}
