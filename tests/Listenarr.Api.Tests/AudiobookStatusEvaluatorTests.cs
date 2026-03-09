using System.Collections.Generic;
using Listenarr.Api.Services;
using Listenarr.Domain.Models;
using Xunit;

namespace Listenarr.Api.Tests
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
        public void ComputeStatus_ReturnsQualityMismatch_WhenNoFilesMatchPreferredFormats()
        {
            var profile = CreateProfile(cutoffQuality: "256kbps", preferredFormats: new List<string> { "m4b" });
            var files = new List<AudiobookFileStatusInfo>
            {
                new() { Format = "mp3", Bitrate = 320000 }
            };

            var status = AudiobookStatusEvaluator.ComputeStatus(false, true, null, profile, files);

            Assert.Equal(AudiobookStatusEvaluator.QualityMismatch, status);
        }

        [Fact]
        public void ComputeStatus_ReturnsQualityMatch_WhenDerivedQualityMeetsCutoffBoundary()
        {
            var profile = CreateProfile(cutoffQuality: "256kbps", preferredFormats: new List<string> { "m4b" });
            var files = new List<AudiobookFileStatusInfo>
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
            var files = new List<AudiobookFileStatusInfo>
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
            var files = new List<AudiobookFileStatusInfo>
            {
                new() { Format = "wv", Container = "wv" }
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
