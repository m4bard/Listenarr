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

namespace Listenarr.Tests.Features.Domain.Utils
{
    /// <summary>
    /// The gate itself, away from the scorer. These pin the two rules and, just as importantly,
    /// the shape of what the gate refuses to decide.
    /// </summary>
    [Trait("Area", "Search")]
    [Trait("Name", "QualityGateTests")]
    [Trait("Category", "Domain")]
    public sealed class QualityGateTests : BaseTests
    {
        private static QualityProfile WithRungs(params (string Quality, bool Allowed)[] rungs)
            => new()
            {
                Qualities = rungs
                    .Select((rung, index) => new QualityDefinition
                    {
                        Quality = rung.Quality,
                        Allowed = rung.Allowed,
                        Priority = index
                    })
                    .ToList()
            };

        [Fact]
        public void NoLadder_MeansNoOpinion()
        {
            Assert.Equal(QualityGateVerdict.NoOpinion, QualityGate.Evaluate("FLAC", null));
            Assert.Equal(QualityGateVerdict.NoOpinion, QualityGate.Evaluate("FLAC", new QualityProfile()));
            Assert.Equal(QualityGateVerdict.NoOpinion, QualityGate.Evaluate(null, WithRungs(("FLAC", false))));
            Assert.Equal(QualityGateVerdict.NoOpinion, QualityGate.Evaluate("   ", WithRungs(("FLAC", false))));
        }

        [Fact]
        public void ARungNamingTheLabel_DecidesIt()
        {
            var profile = WithRungs(("MP3 320kbps", true), ("MP3 64kbps", false));

            Assert.Equal(QualityGateVerdict.Allowed, QualityGate.Evaluate("MP3 320kbps", profile));
            Assert.Equal(QualityGateVerdict.Refused, QualityGate.Evaluate("MP3 64kbps", profile));
            Assert.Equal(QualityGateVerdict.Allowed, QualityGate.Evaluate("mp3 320KBPS", profile));
        }

        [Fact]
        public void ABroaderLabelCoveringSeveralRungs_TakesThePermissiveReading()
        {
            var profile = WithRungs(("MP3 320kbps", true), ("MP3 64kbps", false));

            // "MP3" names both rungs, and the release could be either of them.
            Assert.Equal(QualityGateVerdict.Allowed, QualityGate.Evaluate("MP3", profile));

            // The control: with every rung it names refused, the same label is refused.
            Assert.Equal(
                QualityGateVerdict.Refused,
                QualityGate.Evaluate("MP3", WithRungs(("MP3 320kbps", false), ("MP3 64kbps", false))));
        }

        [Fact]
        public void ANameMatch_ShortCircuitsTheCodecGroup()
        {
            // "MP3 64kbps" names a refused rung. Its codec group also holds an allowed rung, and
            // the name match has to win or per-rung refusal would mean nothing.
            var profile = WithRungs(("MP3 320kbps", true), ("MP3 64kbps", false));

            Assert.Equal(QualityGateVerdict.Refused, QualityGate.Evaluate("MP3 64kbps", profile));
        }

        [Fact]
        public void ALabelNamingNoRung_FallsBackToItsCodecGroup()
        {
            // "M4B" is an AAC container and names no rung of its own.
            Assert.Equal(
                QualityGateVerdict.Allowed,
                QualityGate.Evaluate("M4B", WithRungs(("AAC 256kbps", true), ("AAC 64kbps", false))));

            Assert.Equal(
                QualityGateVerdict.Refused,
                QualityGate.Evaluate("M4B", WithRungs(("AAC 256kbps", false), ("AAC 64kbps", false))));

            // The control: a codec the ladder does not carry is not refused by it.
            Assert.Equal(
                QualityGateVerdict.NoOpinion,
                QualityGate.Evaluate("M4B", WithRungs(("MP3 320kbps", false))));
        }

        [Fact]
        public void TheCodecGroupFallback_WorksAtCodecGranularityAndThatIsVisible()
        {
            // Documented consequence, pinned so it is a decision rather than a surprise: the
            // profile refuses the VBR rung it names, and permits an equivalent label it does not,
            // because that label falls through to the codec group where another rung is allowed.
            var profile = WithRungs(("MP3 320kbps", true), ("MP3 VBR", false));

            Assert.Equal(QualityGateVerdict.Refused, QualityGate.Evaluate("MP3 VBR", profile));
            Assert.Equal(QualityGateVerdict.Allowed, QualityGate.Evaluate("MP3 V0", profile));
        }

        [Fact]
        public void ALabelWithNoCodecGroup_IsNeverRefused()
        {
            // Every rung refused, so anything the gate can place is refused. These are the labels
            // it cannot place, and AAX is the one that matters: it is the Audible container, the
            // scorer ranks it second only to FLAC, and no profile can currently refuse it.
            var refuseEverything = WithRungs(
                ("AAC 320kbps", false),
                ("MP3 320kbps", false),
                ("FLAC", false),
                ("OPUS 128kbps", false));

            Assert.Equal(QualityGateVerdict.Refused, QualityGate.Evaluate("FLAC", refuseEverything));

            foreach (var unplaceable in new[] { "AAX", "MP4", "WMA", "AC3" })
            {
                Assert.Equal(QualityGateVerdict.NoOpinion, QualityGate.Evaluate(unplaceable, refuseEverything));
            }

            // The control that keeps this honest: a bare bitrate has no codec group either, but
            // it is placed by the name rule before the group is ever consulted.
            Assert.Equal(QualityGateVerdict.Refused, QualityGate.Evaluate("320kbps", refuseEverything));
        }

        [Fact]
        public void SeededDefaultLadder_HasNoOpinionAboutTheCodecsItDoesNotList()
        {
            // The eleven rungs QualityProfileService seeds into the default profile, all allowed,
            // which is the shape a stock install actually has.
            var seeded = WithRungs(
                ("AAC 320kbps", true), ("AAC 256kbps", true), ("AAC 192kbps", true),
                ("AAC 128kbps", true), ("AAC 64kbps", true),
                ("MP3 320kbps", true), ("MP3 256kbps", true), ("MP3 VBR", true),
                ("MP3 192kbps", true), ("MP3 128kbps", true), ("MP3 64kbps", true));

            Assert.Equal(QualityGateVerdict.Allowed, QualityGate.Evaluate("MP3 320kbps", seeded));
            Assert.Equal(QualityGateVerdict.Allowed, QualityGate.Evaluate("M4B", seeded));
            Assert.Equal(QualityGateVerdict.NoOpinion, QualityGate.Evaluate("FLAC", seeded));
            Assert.Equal(QualityGateVerdict.NoOpinion, QualityGate.Evaluate("OPUS", seeded));
        }
    }
}
