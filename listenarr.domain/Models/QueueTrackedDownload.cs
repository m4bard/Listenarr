/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2025 Robbie Davis
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

namespace Listenarr.Domain.Models
{
    /// <summary>
    /// Lightweight projection used by queue reconciliation so Activity queries
    /// don't need to hydrate full Download entities.
    /// </summary>
    public class QueueTrackedDownload
    {
        public string Id { get; set; } = string.Empty;
        public string DownloadClientId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public DownloadStatus Status { get; set; }
        public DateTime StartedAt { get; set; }
        public long TotalSize { get; set; }
        public long DownloadedSize { get; set; }
        public string DownloadPath { get; set; } = string.Empty;
        public string FinalPath { get; set; } = string.Empty;
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}
