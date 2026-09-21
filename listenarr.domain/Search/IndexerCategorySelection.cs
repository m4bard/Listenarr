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
using System.Globalization;

namespace Listenarr.Domain.Search
{
    /// <summary>
    /// Rules for the Newznab/Torznab category list an indexer searches.
    /// </summary>
    /// <remarks>
    /// A category-less Newznab/Torznab query is sent without a <c>cat=</c> parameter, which those
    /// servers read as "every category". A generic audiobook title can then match a music release,
    /// so the category list is what keeps a broad query inside the audiobook part of the catalogue.
    /// </remarks>
    public static class IndexerCategorySelection
    {
        /// <summary>
        /// Newznab standard category 3030, "Audio/Audiobook". This is the only audiobook category
        /// in the standard, so it is the whole of the default for an audiobook-only manager.
        /// </summary>
        public const string AudiobookDefault = "3030";

        /// <summary>
        /// Implementations whose outbound query carries the configured category list.
        /// </summary>
        /// <remarks>
        /// Only these send <c>cat=</c>. MyAnonamouse sends its own fixed category list and
        /// Internet Archive searches a named collection, so neither reads this field and neither
        /// is asked for one.
        /// </remarks>
        private static readonly string[] CategoryAwareImplementations = ["Newznab", "Torznab"];

        /// <summary>
        /// Whether an indexer of this implementation must carry at least one category.
        /// </summary>
        public static bool RequiresCategories(string? implementation)
            => !string.IsNullOrWhiteSpace(implementation)
                && CategoryAwareImplementations.Contains(implementation.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether a stored category list names at least one usable category.
        /// </summary>
        /// <remarks>
        /// Categories are Newznab numeric identifiers, so a value carrying no number at all cannot
        /// constrain a search however it is spelled.
        /// </remarks>
        public static bool HasUsableCategory(string? categories)
        {
            if (string.IsNullOrWhiteSpace(categories))
            {
                return false;
            }

            foreach (var token in categories.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                // NumberStyles.None rejects a sign, so a negative token never parses here. The
                // comparison is what rules out "0", which is not a Newznab category.
                if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var categoryId)
                    && categoryId > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Refuses to bind an indexer whose implementation searches by category but which names none.
    /// </summary>
    /// <remarks>
    /// This deliberately is not <see cref="RequiredAttribute"/>. Entity Framework reads
    /// <c>[Required]</c> when it builds the model, so using it here would make the column
    /// <c>NOT NULL</c> and orphan every indexer already stored without categories. A custom
    /// validation attribute is invisible to Entity Framework and runs only during model binding,
    /// so stored rows keep loading and only a save is refused.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class IndexerCategoriesRequiredAttribute : ValidationAttribute
    {
        private const string DefaultErrorMessage =
            "At least one category is required. Without one, Newznab and Torznab search every "
            + "category, so unrelated releases can match.";

        /// <summary>
        /// This validator reads the rest of the indexer, so it cannot run without a context.
        /// </summary>
        public override bool RequiresValidationContext => true;

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            // Null is "the request did not mention categories", which is not the same as clearing
            // them. An omitted field leaves a stored list alone and gets the default on create;
            // only a supplied value that names no category is refused.
            if (value is null
                || validationContext.ObjectInstance is not Indexer indexer
                || !IndexerCategorySelection.RequiresCategories(indexer.Implementation)
                || IndexerCategorySelection.HasUsableCategory(value as string))
            {
                return ValidationResult.Success;
            }

            var memberName = validationContext.MemberName ?? nameof(Indexer.Categories);
            return new ValidationResult(ErrorMessage ?? DefaultErrorMessage, [memberName]);
        }
    }
}
