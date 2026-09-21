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
using System.Text.RegularExpressions;

namespace Listenarr.Domain.Common
{
    /// <summary>
    /// QualityMatcher's internals: rung projection, label parsing, codec canonicalisation and
    /// the bitrate comparison the public surface is built from.
    ///
    /// Split out of QualityMatcher.cs, which had grown past the 500-line per-file ceiling
    /// BackendArchitectureTests.ActiveProductionSourceFiles_RemainFocused enforces. Nothing is
    /// altered by the move: these members are the same members, in the same order, with the same
    /// accessibility, and the class is partial only so they can live here.
    /// </summary>
    public static partial class QualityMatcher
    {
        // ---- internals --------------------------------------------------------------------

        private readonly record struct EffectiveRungInfo(QualityDefinition Source, string? Codec, int? BitrateKbps, bool IsLossless)
        {
            public int Priority => Source.Priority;
        }

        private static EffectiveRungInfo Best(IEnumerable<EffectiveRungInfo> rungs)
            => rungs.OrderBy(r => r.Priority).First();

        private static EffectiveRungInfo Worst(IEnumerable<EffectiveRungInfo> rungs)
            => rungs.OrderByDescending(r => r.Priority).First();

        /// <summary>
        /// The rung the profile stops upgrading at, or null with <paramref name="cutoffBlank"/>
        /// set when the profile is not upgrading at all.
        ///
        /// Null with <paramref name="cutoffBlank"/> false is the other case and means something
        /// else entirely: CutoffQuality names nothing the profile allows, because the rung is
        /// absent or present but not Allowed. A caller reading that as satisfied has the answer
        /// backwards. Matching is case-insensitive, so a cutoff differing from its rung only in
        /// case still resolves.
        /// </summary>
        /// <remarks>
        /// A profile with <see cref="QualityProfile.UpgradeAllowed"/> false counts as blank here
        /// even when it carries a real cutoff, because it is not going to upgrade past anything.
        /// Before that flag existed the only way to record "upgrades off" was to blank the cutoff,
        /// so the two arms of this test used to be the same arm, and callers that already treat a
        /// blank cutoff as satisfied keep the answer they had.
        ///
        /// Readarr and Sonarr reach the same outcome by a different route, and the difference is
        /// worth naming because it is where a reviewer will look. They do not switch the cutoff
        /// off; they lower it, with
        /// <c>var cutoff = profile.UpgradeAllowed ? profile.Cutoff : profile.FirstAllowedQuality().Id;</c>
        /// (src/NzbDrone.Core/DecisionEngine/Specifications/UpgradableSpecification.cs:99 in
        /// Readarr; Sonarr's :126 is the same line, spelling its own helper FirststAllowedQuality).
        /// The flag is then checked again on its own, and refuses the upgrade outright: Sonarr
        /// returns UpgradeableRejectReason.UpgradesNotAllowed at :64 and Readarr's
        /// CheckUpgradeAllowed returns false at :171-174. So the file is not replaced there
        /// either.
        ///
        /// Listenarr has one question instead of two, and answers it here. That keeps the flag and
        /// the blank cutoff it replaces on the same code path, which is what lets every profile
        /// that has been recording upgrades-off as a blank cutoff keep the answer it had. The cost
        /// is that "meets cutoff" reports true for a file the family would call below cutoff while
        /// still declining to replace it, so the two agree on what happens and disagree on what to
        /// call it.
        /// </remarks>
        public static QualityDefinition? ResolveCutoff(QualityProfile? profile, out bool cutoffBlank)
        {
            cutoffBlank = false;
            if (profile?.Qualities == null || profile.Qualities.Count == 0
                || !profile.UpgradeAllowed
                || string.IsNullOrWhiteSpace(profile.CutoffQuality))
            {
                cutoffBlank = true;
                return null;
            }

            return FindAllowedRung(profile, profile.CutoffQuality);
        }

        /// <summary>
        /// A profile's Allowed, labelled rungs. The single definition of "Allowed" a caller should
        /// filter a profile's <see cref="QualityProfile.Qualities"/> through before treating any of
        /// them (a cutoff included) as resolvable, so no other layer re-derives its own answer to
        /// "does Allowed matter here" and drifts from this one.
        /// </summary>
        public static IEnumerable<QualityDefinition> AllowedQualities(QualityProfile profile)
            => profile.Qualities
                .Where(q => q.Allowed && !string.IsNullOrWhiteSpace(q.Quality));

        private static IEnumerable<QualityDefinition> MatchableQualities(QualityProfile profile)
            => profile.Qualities
                .Where(q => !string.IsNullOrWhiteSpace(q.Quality));

        private static QualityDefinition? FindAllowedRung(QualityProfile profile, string label)
            => AllowedQualities(profile)
                .FirstOrDefault(q => string.Equals(q.Quality, label, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Resolve a rung's effective (codec group, bitrate-kbps, lossless) using the structured
        /// fields when present and parsing the <see cref="QualityDefinition.Quality"/> label otherwise
        /// (seed/legacy rungs only set Quality + Priority).
        /// </summary>
        private static EffectiveRungInfo EffectiveRung(QualityDefinition rung)
        {
            if (!string.IsNullOrWhiteSpace(rung.Codec))
            {
                return new EffectiveRungInfo(rung, CanonicalCodec(rung.Codec), rung.Bitrate, rung.IsLossless);
            }

            var (codec, bitrate, lossless) = ParseQualityLabel(rung.Quality);
            return new EffectiveRungInfo(rung, codec, rung.Bitrate ?? bitrate, rung.IsLossless || lossless);
        }

        private static (string? Codec, int? BitrateKbps, bool IsLossless) ParseQualityLabel(string quality)
        {
            var lower = (quality ?? string.Empty).Trim().ToLowerInvariant();

            int? bitrate = null;
            var match = Regex.Match(lower, @"\d{2,}");
            if (match.Success && int.TryParse(match.Value, out var kb))
            {
                bitrate = kb;
            }

            if (Contains(lower, "flac")) return ("FLAC", bitrate, true);
            if (Contains(lower, "alac")) return ("ALAC", bitrate, true);
            // Every MPEG-4 container carries AAC, so they all resolve to the AAC group: "M4B" and
            // "M4A" as before, "MP4" because MapCodec has always accepted it here and the two
            // diverging left a label this method could not place, and "AAX"/"AAXC" because those
            // are Audible's MPEG-4 containers and the scorer ranks AAX second only to FLAC.
            // "aaxc" is covered by the "aax" test.
            if (Contains(lower, "aac") || Contains(lower, "m4b") || Contains(lower, "m4a")
                || Contains(lower, "mp4") || Contains(lower, "aax")) return ("AAC", bitrate, false);
            if (Contains(lower, "mp3")) return ("MP3", bitrate, false);
            if (Contains(lower, "opus")) return ("OPUS", bitrate, false);
            if (Contains(lower, "vorbis") || Contains(lower, "ogg")) return ("OGG Vorbis", bitrate, false);
            if (Contains(lower, "aiff")) return ("AIFF", bitrate, true);
            if (Contains(lower, "ape")) return ("APE", bitrate, true);
            if (Contains(lower, "dsd")) return ("DSD", bitrate, true);
            if (Contains(lower, "wav") || Contains(lower, "wv")) return ("WavPack", bitrate, true);
            if (Contains(lower, "lossless")) return (null, bitrate, true);

            // Bare bitrate (e.g. "320kbps") acts as a codec-agnostic wildcard rung.
            return (null, bitrate, false);
        }

        /// <summary>Map a file's codec/container/format/extension onto the set of profile codec groups it satisfies.</summary>
        private static HashSet<string> MapCodec(AudioQualityInput file)
        {
            var tokens = new List<string>();
            AddToken(tokens, file.Codec);
            AddToken(tokens, file.Container);
            AddToken(tokens, file.Format);
            if (!string.IsNullOrWhiteSpace(file.Path))
            {
                AddToken(tokens, System.IO.Path.GetExtension(file.Path)?.TrimStart('.'));
            }

            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool Any(string needle) => tokens.Any(t => Contains(t, needle));

            if (Any("flac")) groups.Add("FLAC");
            if (Any("alac")) groups.Add("ALAC");
            if (Any("aiff")) groups.Add("AIFF");
            if (Any("ape")) groups.Add("APE");
            if (Any("dsd")) groups.Add("DSD");
            if (Any("wav") || Any("wv")) groups.Add("WavPack");
            if (Any("mp3")) groups.Add("MP3");
            if (Any("opus")) groups.Add("OPUS");
            if (Any("vorbis") || Any("ogg")) groups.Add("OGG Vorbis");
            // AAC commonly lives in M4B/M4A/MP4/AAX containers; cover the legacy "M4B" codec group
            // too. This family is deliberately kept in step across ParseQualityLabel, MapCodec and
            // CanonicalCodec, because when they disagree a label the gate can place maps to a codec
            // group the matcher cannot, or the reverse. The three do NOT agree outside it:
            // CanonicalCodec handles no aiff, ape, dsd, wav/wv or lossless and returns the raw
            // string for them, which mostly hides behind the gate's OrdinalIgnoreCase comparison.
            // That predates this change; do not read the MPEG-4 agreement as a general property.
            if (Any("aac") || Any("m4b") || Any("m4a") || Any("mp4") || Any("aax"))
            {
                groups.Add("AAC");
                groups.Add("M4B");
            }

            return groups;
        }

        /// <summary>Codec groups that represent lossless audio (see <see cref="MapCodec"/>).</summary>
        private static readonly HashSet<string> LosslessGroups =
            new(StringComparer.OrdinalIgnoreCase) { "FLAC", "ALAC", "AIFF", "APE", "DSD", "WavPack" };

        private static bool IsLosslessFile(AudioQualityInput file)
        {
            // Derive lossless-ness from the same mapped codec groups used for matching, so the
            // path extension fallback (e.g. "book.flac" with no codec/container/format metadata)
            // is honoured consistently — otherwise such a file maps to the FLAC group yet is
            // treated as lossy and filtered off the FLAC rung.
            return MapCodec(file).Overlaps(LosslessGroups);
        }

        /// <summary>
        /// How far below a rung a file may report and still count as that rung, as a fraction.
        ///
        /// A file encoded at a nominal bitrate almost never reports exactly that figure. A "128kbps"
        /// AAC file commonly reports something like 127241 bps, which rounds to 127 kbps, and a
        /// strict `rung &lt;= file` comparison then excludes the 128 rung and drops the file a whole
        /// tier. Applied to a real library that misclassifies most lossy files, because the failure
        /// is systematic rather than occasional.
        ///
        /// Five percent is far smaller than the gap between adjacent rungs in any ordinary profile.
        /// Across 64, 96, 128, 192, 256 and 320 the narrowest gap is 256 to 320, a fifth of the
        /// higher rung and four times this tolerance, so a constant-bitrate file cannot be promoted
        /// across a real tier boundary. It only absorbs encoder variance.
        /// </summary>
        private const double RungBitrateTolerance = 0.05;

        /// <summary>
        /// Whether a file's bitrate reaches a rung, allowing for the difference between a nominal
        /// bitrate and what an encoder actually reports.
        /// </summary>
        private static bool MeetsRung(int fileKbps, int rungKbps)
        {
            if (fileKbps >= rungKbps)
            {
                return true;
            }

            // At least one whole kbps of slack, so the smallest rungs are not left with a tolerance
            // that rounds away to nothing.
            var slack = Math.Max(1d, rungKbps * RungBitrateTolerance);
            return rungKbps - fileKbps <= slack;
        }

        /// <summary>Convert a bitrate to kbps, guarding values already expressed in kbps.</summary>
        private static int? NormalizeKbps(int? bitsPerSecond)
        {
            if (bitsPerSecond is not int bps || bps <= 0)
            {
                return null;
            }

            return bps >= 1000 ? (int)Math.Round(bps / 1000d) : bps;
        }

        private static string CanonicalCodec(string codec)
        {
            var lower = codec.Trim().ToLowerInvariant();
            if (Contains(lower, "flac")) return "FLAC";
            if (Contains(lower, "alac")) return "ALAC";
            if (Contains(lower, "aac") || Contains(lower, "m4b") || Contains(lower, "m4a")
                || Contains(lower, "mp4") || Contains(lower, "aax")) return "AAC";
            if (Contains(lower, "mp3")) return "MP3";
            if (Contains(lower, "opus")) return "OPUS";
            if (Contains(lower, "vorbis") || Contains(lower, "ogg")) return "OGG Vorbis";
            return codec.Trim();
        }

        private static void AddToken(List<string> tokens, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                tokens.Add(value.Trim().ToLowerInvariant());
            }
        }

        private static bool Contains(string haystack, string needle)
            => haystack.Contains(needle, StringComparison.Ordinal);
    }
}
