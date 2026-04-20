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
namespace Listenarr.Api.Services
{
    /// <summary>
    /// Handler responsible for processing MoveOrCopy file jobs extracted from DownloadProcessingBackgroundService.
    /// Implementations encapsulate file naming, move/copy operations, verification and post-processing (scan enqueue).
    /// </summary>
    public interface IFileProcessingHandler
    {
        /// <summary>
        /// Process a MoveOrCopyFile job. The IServiceScope passed in can be used to resolve scoped services
        /// (DbContext, repositories) needed for the operation.
        /// </summary>
        Task HandleAsync(DownloadProcessingJob job, IServiceScope scope, CancellationToken cancellationToken);
    }
}
