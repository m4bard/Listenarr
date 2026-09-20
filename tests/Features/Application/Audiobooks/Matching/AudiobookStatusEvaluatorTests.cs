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

namespace Listenarr.Tests.Features.Application.Audiobooks.Matching
{
    public class AudiobookStatusEvaluatorTests
    {
        [Fact]
        public void ComputeStatus_ReturnsNoFile_WhenHasNoFiles()
        {
            var status = AudiobookStatusEvaluator.ComputeStatus(
                isDownloading: false,
                hasAnyFile: false,
                audiobookQuality: null,
                qualityProfile: null,
                files: null);

            Assert.Equal(AudiobookStatusEvaluator.NoFile, status);
        }

        [Fact]
        public void ComputeStatus_ReturnsQualityMatch_WhenAboveCutoffFileIsNotInAPreferredFormat()
        {
            // The file is well above the cutoff and its only sin is its container. PreferredFormats
            // used to gate the candidate list, so this returned QualityMismatch, which the library
            // view renders as "Below Cutoff" about a file that is nothing of the sort.
            var profile = CreateProfile(cutoffQuality: "256kbps", preferredFormats: new List<string> { "m4b" });
            var files = new List<AudiobookFormatSummary>
            {
                new() { Format = "mp3", Bitrate = 320000 }
            };

            var status = AudiobookStatusEvaluator.ComputeStatus(false, true, null, profile, files);

            Assert.Equal(AudiobookStatusEvaluator.QualityMatch, status);
        }

        [Fact]
        public void ComputeStatus_ReturnsQualityMismatch_WhenBelowCutoffFileIsAlsoNotInAPreferredFormat()
        {
            // Control for the test above: dropping the format gate must not make every book match.
            // Same non-preferred container, this time genuinely under the cutoff.
            var profile = CreateProfile(cutoffQuality: "256kbps", preferredFormats: new List<string> { "m4b" });
            var files = new List<AudiobookFormatSummary>
            {
                new() { Format = "mp3", Bitrate = 192000 }
            };

            var status = AudiobookStatusEvaluator.ComputeStatus(false, true, null, profile, files);

            Assert.Equal(AudiobookStatusEvaluator.QualityMismatch, status);
        }

        [Fact]
        public void ComputeStatus_ReturnsQualityMatch_WhenOneFileOfAMixedFormatBookIsAboveCutoff()
        {
            // Pins a consequence of dropping the format gate that is easy to miss: any one file
            // clearing the cutoff now satisfies the whole book, including a file whose container
            // the profile did not ask for. This is what AudiobookQualityCutoffEvaluator has always
            // done with the same stored files, so agreeing with it is the point, but the book below
            // would have read as a mismatch before.
            var profile = CreateProfile(cutoffQuality: "256kbps", preferredFormats: new List<string> { "m4b" });
            var files = new List<AudiobookFormatSummary>
            {
                new() { Format = "m4b", Bitrate = 64000 },
                new() { Format = "mp3", Bitrate = 320000 }
            };

            var status = AudiobookStatusEvaluator.ComputeStatus(false, true, null, profile, files);

            Assert.Equal(AudiobookStatusEvaluator.QualityMatch, status);
        }

        [Fact]
        public void ComputeStatus_ReturnsQualityMatch_WhenTheFileListIsEmptyRatherThanNull()
        {
            // The empty-files guard now covers null and empty in one line, and only the null shape
            // was pinned. A book with hasAnyFile but nothing to measure must not read as a mismatch.
            var profile = CreateProfile(cutoffQuality: "256kbps", preferredFormats: new List<string> { "m4b" });

            var status = AudiobookStatusEvaluator.ComputeStatus(
                false, true, null, profile, new List<AudiobookFormatSummary>());

            Assert.Equal(AudiobookStatusEvaluator.QualityMatch, status);
        }

        [Fact]
        public void ComputeStatus_ReturnsQualityMatch_WhenDerivedQualityMeetsCutoffBoundary()
        {
            var profile = CreateProfile(cutoffQuality: "256kbps", preferredFormats: new List<string> { "m4b" });
            var files = new List<AudiobookFormatSummary>
            {
                new() { Format = "m4b", Bitrate = 256000 }
            };

            var status = AudiobookStatusEvaluator.ComputeStatus(false, true, null, profile, files);

            Assert.Equal(AudiobookStatusEvaluator.QualityMatch, status);
        }

        [Fact]
        public void ComputeStatus_ReturnsQualityMismatch_WhenDerivedQualityIsBelowCutoff()
        {
            var profile = CreateProfile(cutoffQuality: "256kbps", preferredFormats: new List<string> { "m4b" });
            var files = new List<AudiobookFormatSummary>
            {
                new() { Format = "m4b", Bitrate = 192000 }
            };

            var status = AudiobookStatusEvaluator.ComputeStatus(false, true, null, profile, files);

            Assert.Equal(AudiobookStatusEvaluator.QualityMismatch, status);
        }

        [Fact]
        public void ComputeStatus_ReturnsQualityMatch_WhenOnlyLegacyFileSummaryExists()
        {
            var profile = CreateProfile(cutoffQuality: "256kbps", preferredFormats: new List<string> { "m4b" });

            var status = AudiobookStatusEvaluator.ComputeStatus(false, true, null, profile, files: null);

            Assert.Equal(AudiobookStatusEvaluator.QualityMatch, status);
        }

        [Fact]
        public void ComputeStatus_TreatsWavPackAsLossless()
        {
            var profile = new QualityProfile
            {
                Name = "Lossless Profile",
                CutoffQuality = "lossless",
                PreferredFormats = new List<string> { "wv" },
                Qualities = new List<QualityDefinition>
                {
                    new() { Quality = "lossless", Priority = 0 }
                }
            };
            var files = new List<AudiobookFormatSummary>
            {
                new() { Format = "wv", Container = "wv" }
            };

            var status = AudiobookStatusEvaluator.ComputeStatus(false, true, null, profile, files);

            Assert.Equal(AudiobookStatusEvaluator.QualityMatch, status);
        }

        [Fact]
        public void ComputeStatus_ReturnsQualityMatch_ForPathOnlyFile_WhenProbeMetadataMissing()
        {
            // Regression: when metadata processing is disabled / ffprobe is unavailable, the file
            // summary carries only a Path. The status evaluator must forward that Path to the
            // QualityMatcher (as AudiobookQualityCutoffEvaluator does) so a "book.flac" maps to the
            // FLAC lossless rung. Previously Path was dropped, so this resolved as quality-mismatch
            // and disagreed with the automatic-search cutoff.
            // PreferredFormats = ["flac"] is left on the profile deliberately: the status no longer
            // filters by it, and the extension is what tells the matcher which rung this file is on.
            var profile = new QualityProfile
            {
                Name = "Lossless Profile",
                CutoffQuality = "lossless",
                PreferredFormats = new List<string> { "flac" },
                Qualities = new List<QualityDefinition>
                {
                    new() { Quality = "lossless", Priority = 0 }
                }
            };
            var files = new List<AudiobookFormatSummary>
            {
                new() { Path = "/audiobooks/Author/Title/book.flac" }
            };

            var status = AudiobookStatusEvaluator.ComputeStatus(false, true, null, profile, files);

            Assert.Equal(AudiobookStatusEvaluator.QualityMatch, status);
        }

        [Fact]
        public void ComputeStatus_ReturnsQualityMatch_ForPathOnlyLossyFile_WhenOnlyTheExtensionIdentifiesTheCodec()
        {
            // Generality beyond FLAC: the path-extension fallback is format-agnostic. A metadata-less
            // book.m4b must reach the matcher and resolve on the AAC group through its extension
            // alone, rather than being reported as quality-mismatch.
            var profile = new QualityProfile
            {
                Name = "AAC Profile",
                CutoffQuality = "AAC 256kbps",
                PreferredFormats = new List<string> { "m4b" },
                Qualities = new List<QualityDefinition>
                {
                    new() { Quality = "AAC 256kbps", Codec = "AAC", Bitrate = 256, Priority = 0 }
                }
            };
            var files = new List<AudiobookFormatSummary>
            {
                new() { Path = "/audiobooks/Author/Title/book.m4b" }
            };

            var status = AudiobookStatusEvaluator.ComputeStatus(false, true, null, profile, files);

            Assert.Equal(AudiobookStatusEvaluator.QualityMatch, status);
        }

        private static QualityProfile CreateProfile(string cutoffQuality, List<string> preferredFormats)
        {
            return new QualityProfile
            {
                Name = "Test Profile",
                CutoffQuality = cutoffQuality,
                PreferredFormats = preferredFormats,
                Qualities = new List<QualityDefinition>
                {
                    new() { Quality = "320kbps", Priority = 0 },
                    new() { Quality = "256kbps", Priority = 1 },
                    new() { Quality = "192kbps", Priority = 2 }
                }
            };
        }
    }
}
