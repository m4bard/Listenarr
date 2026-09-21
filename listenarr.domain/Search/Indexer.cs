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

namespace Listenarr.Domain.Search
{
    public class Indexer
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// User-friendly name for the indexer
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Type of indexer: Torrent or Usenet
        /// </summary>
        public string Type { get; set; } = string.Empty; // "Torrent" or "Usenet"

        /// <summary>
        /// Implementation type (e.g., "Newznab", "Torznab", "Custom")
        /// </summary>
        public string Implementation { get; set; } = string.Empty;

        /// <summary>
        /// Base URL for the indexer API
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// API key for authentication
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Newznab categories to search, comma-separated (e.g. "3030,3040")
        /// </summary>
        /// <remarks>
        /// New indexers start on <see cref="IndexerCategorySelection.AudiobookDefault"/>, so a
        /// caller that omits the field gets that rather than an unconstrained search. The property
        /// stays nullable: indexers stored before categories were required must keep loading.
        /// </remarks>
        [IndexerCategoriesRequired]
        public string? Categories { get; set; } = IndexerCategorySelection.AudiobookDefault;

        /// <summary>
        /// Anime categories (comma-separated or JSON array)
        /// </summary>
        public string? AnimeCategories { get; set; }

        /// <summary>
        /// Whether to enable RSS sync
        /// </summary>
        public bool EnableRss { get; set; } = true;

        /// <summary>
        /// Whether to enable automatic search
        /// </summary>
        public bool EnableAutomaticSearch { get; set; } = true;

        /// <summary>
        /// Whether to enable interactive search
        /// </summary>
        public bool EnableInteractiveSearch { get; set; } = true;

        /// <summary>
        /// Whether to search for anime using standard numbering
        /// </summary>
        public bool EnableAnimeStandardSearch { get; set; } = false;

        /// <summary>
        /// Whether the indexer is enabled
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Priority for search order (lower = higher priority)
        /// </summary>
        public int Priority { get; set; } = 25;

        /// <summary>
        /// Minimum age in minutes before NZBs are grabbed
        /// </summary>
        public int MinimumAge { get; set; } = 0;

        /// <summary>
        /// Retention in days (Usenet only)
        /// </summary>
        public int Retention { get; set; } = 0;

        /// <summary>
        /// Maximum size in MB (0 = unlimited)
        /// </summary>
        public int MaximumSize { get; set; } = 0;

        /// <summary>
        /// Additional configuration settings (JSON)
        /// </summary>
        public string? AdditionalSettings { get; set; }

        /// <summary>
        /// When the indexer was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the indexer was last updated
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the indexer was last tested
        /// </summary>
        public DateTime? LastTestedAt { get; set; }

        /// <summary>
        /// Result of last test
        /// </summary>
        public bool? LastTestSuccessful { get; set; }

        /// <summary>
        /// Error message from last test
        /// </summary>
        public string? LastTestError { get; set; }

        /// <summary>
        /// When the current run of failures began. Set once, when the indexer leaves rung 0, and
        /// deliberately NOT rewritten on each subsequent failure: "has been failing for more than
        /// six hours" is uncomputable if this tracks the most recent failure instead of the first.
        /// Cleared when the indexer walks all the way back down to rung 0.
        /// </summary>
        public DateTime? InitialFailure { get; set; }

        /// <summary>
        /// When the indexer most recently failed to answer. Rewritten on every failure.
        /// </summary>
        public DateTime? MostRecentFailure { get; set; }

        /// <summary>
        /// Position on the failure-backoff ladder. 0 means healthy; higher rungs mean longer
        /// cooldowns between attempts, up to the ceiling.
        /// </summary>
        public int EscalationLevel { get; set; }

        /// <summary>
        /// When the current cooldown expires. While this is in the future the indexer is skipped at
        /// search-provider selection rather than being asked and failing again.
        /// </summary>
        public DateTime? DisabledTill { get; set; }

        /// <summary>
        /// Why the indexer was last blocked, as the name of the query reason. A string rather than
        /// the enum itself because the enum lives in the application layer, which this project does
        /// not reference, and because it renders directly beside the existing last-test fields.
        /// </summary>
        public string? LastFailureReason { get; set; }
    }
}

