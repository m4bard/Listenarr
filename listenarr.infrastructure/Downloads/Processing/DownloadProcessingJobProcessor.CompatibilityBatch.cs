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
using System.Security.Cryptography;
using System.Text;

namespace Listenarr.Infrastructure.Downloads.Processing
{
    public partial class DownloadProcessingJobProcessor
    {
        private static Guid ResolveCompatibilityBatchId(string jobId)
        {
            if (Guid.TryParse(jobId, out var parsed))
            {
                return parsed;
            }

            var hash = SHA256.HashData(
                Encoding.UTF8.GetBytes("download-import:" + jobId));
            return new Guid(hash.AsSpan(0, 16));
        }
    }
}
