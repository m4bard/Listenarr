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

using System.ComponentModel.DataAnnotations;

namespace Listenarr.Domain.Audiobooks
{
    /// <summary>
    /// Requires a quality profile's cutoff to name one of the profile's own qualities,
    /// and for that quality to be allowed.
    /// </summary>
    /// <remarks>
    /// This runs only where DataAnnotations run: model binding of an inbound request body.
    /// Reading a profile back out of the database does not go through it, so a profile that
    /// was stored before this rule existed still loads and still serialises.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class ValidCutoffAttribute : ValidationAttribute
    {
        public const string CutoffMessage = "Cutoff must be an allowed quality or group";

        public ValidCutoffAttribute()
            : base(CutoffMessage)
        {
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (validationContext.ObjectInstance is not QualityProfile profile)
            {
                return ValidationResult.Success;
            }

            return IsAllowedCutoff(value as string, profile.Qualities)
                ? ValidationResult.Success
                : new ValidationResult(
                    ErrorMessage ?? CutoffMessage,
                    [validationContext.MemberName ?? nameof(QualityProfile.CutoffQuality)]);
        }

        /// <summary>
        /// A cutoff is valid when it names a quality the profile carries and that quality is allowed.
        /// </summary>
        /// <remarks>
        /// Readarr and Sonarr express the same rule as
        /// <c>return cutoffItem is { Allowed: true };</c>
        /// (src/Readarr.Api.V1/Profiles/Quality/QualityCutoffValidator.cs:28 and
        /// src/Sonarr.Api.V3/Profiles/Quality/QualityCutoffValidator.cs:28). They select the item
        /// with SingleOrDefault, which throws when a profile carries the same rung twice; Listenarr
        /// stores its rungs as a JSON list with no uniqueness constraint, so the first match is
        /// taken here instead of turning a malformed payload into a 500.
        /// </remarks>
        public static bool IsAllowedCutoff(string? cutoffQuality, IEnumerable<QualityDefinition>? qualities)
        {
            if (string.IsNullOrWhiteSpace(cutoffQuality) || qualities is null)
            {
                return false;
            }

            var cutoffItem = qualities.FirstOrDefault(quality =>
                string.Equals(quality.Quality, cutoffQuality, StringComparison.Ordinal));

            return cutoffItem is { Allowed: true };
        }
    }
}
