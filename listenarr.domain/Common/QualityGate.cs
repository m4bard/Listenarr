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

namespace Listenarr.Domain.Common
{
    /// <summary>What a quality profile has to say about one quality label.</summary>
    public enum QualityGateVerdict
    {
        /// <summary>The profile carries a rung covering this quality and permits it.</summary>
        Allowed,

        /// <summary>The profile carries a rung covering this quality and refuses it.</summary>
        Refused,

        /// <summary>The profile describes nothing that covers this quality.</summary>
        NoOpinion
    }

    /// <summary>
    /// Reads a quality profile's <see cref="QualityDefinition.Allowed"/> flags as a gate, and
    /// nothing else as a gate.
    ///
    /// A profile expresses two separate things about a release. Its quality rungs say what is
    /// permitted; its <see cref="QualityProfile.PreferredFormats"/> say what is preferred among
    /// the permitted. Only the first belongs here. The scorer applies the second as a score
    /// adjustment, which is where a preference belongs and where it cannot widen the gate.
    /// Sonarr and Readarr keep the same two apart: their QualityAllowedByProfileSpecification is
    /// a boolean veto that no custom-format score can override.
    /// </summary>
    public static class QualityGate
    {
        /// <summary>
        /// Whether <paramref name="profile"/> permits <paramref name="qualityLabel"/>.
        ///
        /// Two rules, in order. A name match wins outright: the label and a rung name, either of
        /// which may be the broader, so the bare "MP3" a loose parser emits lands on "MP3 320kbps".
        /// Only when no rung name matches does the codec group decide, which is what lets a profile
        /// refuse a container label like "M4B" that names no rung of its own.
        ///
        /// Note what that ordering costs. Once the fallback is reached the gate is working at codec
        /// granularity, and one permitted rung in the group permits the whole group. A profile that
        /// refuses "MP3 VBR" refuses a release labelled exactly that, and permits one labelled
        /// "MP3 V0" as long as some other MP3 rung is permitted. Per-rung refusal only bites on
        /// labels the ladder names.
        ///
        /// A label neither rule covers returns <see cref="QualityGateVerdict.NoOpinion"/> rather
        /// than a refusal, because in this codebase an absent rung does not mean a refused one.
        /// QualityProfileService.EnsureProfileHasRequiredQualitiesAsync re-adds any of eleven
        /// AAC and MP3 rungs missing from the default profile, with Allowed set to true, on every
        /// read of it. Deletion is undone; Allowed=false survives. Reading absence as refusal would
        /// therefore refuse FLAC, OPUS and every other codec that seeded ladder never lists, on the
        /// stock default profile, while doing nothing an operator asked for.
        ///
        /// The cost of that choice, which is real: a codec switched off in the settings UI has its
        /// rungs deleted rather than kept as not-allowed, so codec-level refusal made that way is
        /// still not honoured here, and a label that maps to no codec group at all (AAX and MP4
        /// among them, see <see cref="QualityMatcher.CodecGroupOfLabel"/>) cannot be refused by any
        /// profile. Making the ladder exhaustive, the way Sonarr and Readarr do, is the fix for
        /// both and is a larger change than this one.
        /// </summary>
        public static QualityGateVerdict Evaluate(string? qualityLabel, QualityProfile? profile)
        {
            if (string.IsNullOrWhiteSpace(qualityLabel))
            {
                return QualityGateVerdict.NoOpinion;
            }

            var rungs = profile?.Qualities?
                .Where(rung => rung != null && !string.IsNullOrWhiteSpace(rung.Quality))
                .ToList();

            if (rungs == null || rungs.Count == 0)
            {
                return QualityGateVerdict.NoOpinion;
            }

            var label = qualityLabel.Trim().ToLowerInvariant();

            var named = rungs
                .Where(rung => NamesTheSameQuality(label, rung.Quality.Trim().ToLowerInvariant()))
                .ToList();

            if (named.Count > 0)
            {
                return Verdict(named);
            }

            var group = QualityMatcher.CodecGroupOfLabel(qualityLabel);
            if (group == null)
            {
                return QualityGateVerdict.NoOpinion;
            }

            var peers = rungs
                .Where(rung => string.Equals(QualityMatcher.CodecGroupOfRung(rung), group, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return peers.Count == 0 ? QualityGateVerdict.NoOpinion : Verdict(peers);
        }

        /// <summary>
        /// Whether the profile refuses this label outright. A label it says nothing about is not
        /// a refusal.
        /// </summary>
        public static bool Refuses(string? qualityLabel, QualityProfile? profile)
            => Evaluate(qualityLabel, profile) == QualityGateVerdict.Refused;

        /// <summary>
        /// One permitted rung among those covering the label is enough. Where a broad label such
        /// as "MP3" covers several rungs the release could be any of them, so the permissive
        /// reading is the honest one.
        /// </summary>
        private static QualityGateVerdict Verdict(List<QualityDefinition> covering)
            => covering.Any(rung => rung.Allowed) ? QualityGateVerdict.Allowed : QualityGateVerdict.Refused;

        private static bool NamesTheSameQuality(string label, string rung)
            => label.Length > 0
               && rung.Length > 0
               && (label.Contains(rung, StringComparison.Ordinal) || rung.Contains(label, StringComparison.Ordinal));
    }
}
