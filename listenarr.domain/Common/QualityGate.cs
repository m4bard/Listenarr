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
        /// A rung covers the label when their names match (either may be the broader of the two,
        /// so the bare "MP3" a loose parser emits lands on "MP3 320kbps"), and otherwise when the
        /// two share a codec group. The codec-group fallback is what lets a profile refuse a
        /// container label like "M4B" that names no rung of its own.
        ///
        /// A label the profile describes in neither way returns <see cref="QualityGateVerdict.NoOpinion"/>
        /// rather than a refusal. Listenarr's profile ladders are not exhaustive the way the
        /// *arr family's are: the settings UI deletes a codec's rungs when the codec is switched
        /// off instead of persisting them as not-allowed, so an absent codec cannot be read as a
        /// refusal without also refusing every codec the seeded default ladder never listed.
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
