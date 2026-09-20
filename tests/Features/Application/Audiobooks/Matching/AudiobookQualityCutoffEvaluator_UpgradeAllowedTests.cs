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

namespace Listenarr.Tests.Features.Application.Audiobooks.Matching
{
    /// <summary>
    /// AudiobookQualityCutoffEvaluator is deliberately not changed by the UpgradeAllowed flag, and
    /// these pin that. What a blank cutoff itself answers is not this branch's decision: that
    /// moved on fix/blank-cutoff-search-loop (commit 17d79d2b1), which stopped special-casing a
    /// blank cutoff to "keep searching" and let it fall through to QualityMatcher instead, the
    /// same domain rule AudiobookStatusEvaluator already read it by. So a blank cutoff now
    /// answers satisfied (True), and the pin below expects that value, with and without the
    /// flag. What these tests still guard is narrower and independent of which way the blank
    /// case reads: that the flag never makes it read differently with upgrades off than with a
    /// blank cutoff alone. The rest of this file covers the state only the flag can produce,
    /// upgrades off with a cutoff still named, answered through QualityMatcher rather than by
    /// comparing against a cutoff nobody is upgrading towards.
    /// </summary>
    [Trait("Name", nameof(AudiobookQualityCutoffEvaluator_UpgradeAllowedTests))]
    [Trait("Category", "Application")]
    public class AudiobookQualityCutoffEvaluator_UpgradeAllowedTests : BaseTests
    {
        private static QualityProfileBuilder StructuredProfile() =>
            new QualityProfileBuilder().WithName("Structured").WithStructuredDefaults();

        /// <summary>
        /// A profile that recorded upgrades-off the old way, by blanking the cutoff, still gets
        /// the same answer as a blank cutoff on its own. That agreement is what this pins, not
        /// any particular value: the value itself moved from False to True on
        /// fix/blank-cutoff-search-loop, which stopped treating a blank cutoff as "keep
        /// searching" and let it fall through to QualityMatcher like every other blank-cutoff
        /// read in the codebase. This test only guards against the flag reintroducing a
        /// difference the blank case no longer has.
        /// </summary>
        [Fact]
        public async Task BlankCutoff_WithUpgradesOff_AnswersTheSameAsABlankCutoffAlone()
        {
            var withFlag = await EvaluateAsync(
                StructuredProfile().WithCutoff("").WithUpgradesDisabled().Build());
            var withoutFlag = await EvaluateAsync(StructuredProfile().WithCutoff("").Build());

            // Named, not merely compared. Asserting only that the two agree would still pass if
            // the flag made a blank cutoff disagree with itself, which is exactly what this is
            // meant to catch. The value here is True because a blank cutoff means satisfied as of
            // fix/blank-cutoff-search-loop (commit 17d79d2b1); it was False before that branch.
            Assert.True(withoutFlag);
            Assert.Equal(withoutFlag, withFlag);
        }

        /// <summary>
        /// The state only this branch can produce. The profile names a cutoff the file is well
        /// below, but upgrades are off, so there is nothing to search for and the cutoff counts as
        /// met.
        /// </summary>
        [Fact]
        public async Task NamedCutoff_WithUpgradesOff_IsMet_EvenThoughTheFileIsBelowIt()
        {
            var met = await EvaluateAsync(
                StructuredProfile().WithCutoff("AAC 320kbps").WithUpgradesDisabled().Build());

            Assert.True(met);
        }

        /// <summary>
        /// The control. The identical profile and file with upgrades on is still short of the
        /// cutoff, so the answer above is the flag and not the file.
        /// </summary>
        [Fact]
        public async Task NamedCutoff_WithUpgradesOn_IsNotMet_ForTheSameFile()
        {
            var profile = StructuredProfile().WithCutoff("AAC 320kbps").Build();

            Assert.True(profile.UpgradeAllowed);
            Assert.False(await EvaluateAsync(profile));
        }

        /// <summary>
        /// A second control, so the harness is not simply answering "met" for everything: a file
        /// at or above the named cutoff is met with upgrades on too.
        /// </summary>
        [Fact]
        public async Task NamedCutoff_WithUpgradesOn_IsMet_WhenTheFileReachesIt()
        {
            var profile = StructuredProfile().WithCutoff("AAC 64kbps").Build();

            Assert.True(await EvaluateAsync(profile));
        }

        /// <summary>
        /// Runs the evaluator over one stored 128kbps AAC file, which sits below AAC 320kbps and
        /// at or above AAC 64kbps on the structured ladder.
        /// </summary>
        private static async Task<bool> EvaluateAsync(QualityProfile profile)
        {
            const int audiobookId = 4242;
            var audiobook = new Audiobook { Id = audiobookId, QualityProfile = profile };

            var file = AudiobookFile.CreateUnresolved($"/library/book-{audiobookId}.m4b");
            file.AudiobookId = audiobookId;
            file.Codec = "aac";
            file.Bitrate = 128_000;

            var downloadRepository = new Mock<IDownloadRepository>(MockBehavior.Strict);
            downloadRepository
                .Setup(repository => repository.GetByAudiobookIdAsync(
                    audiobookId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Download>());

            var fileRepository = new Mock<IAudiobookFileRepository>(MockBehavior.Strict);
            fileRepository
                .Setup(repository => repository.GetByAudiobookIdAsync(
                    audiobookId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<AudiobookFile> { file });

            return await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook,
                downloadRepository.Object,
                fileRepository.Object);
        }
    }
}
