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

using Listenarr.Application.Interfaces;

namespace Listenarr.Infrastructure.Services
{
    /// <summary>
    /// Default implementation of <see cref="IApplicationPathService"/> that derives all
    /// well-known paths from a single content root, falling back to
    /// <see cref="AppContext.BaseDirectory"/> when no explicit root is supplied.
    /// All returned paths are fully qualified and normalised via <see cref="Path.GetFullPath(string)"/>.
    /// </summary>
    public sealed class ApplicationPathService : IApplicationPathService
    {
        /// <inheritdoc/>
        public string ContentRootPath { get; }
        /// <inheritdoc/>
        public string ConfigRootPath { get; }
        /// <inheritdoc/>
        public string LogsRootPath { get; }
        /// <inheritdoc/>
        public string FfmpegRootPath { get; }
        /// <inheritdoc/>
        public string ToolsRootPath { get; }
        /// <inheritdoc/>
        public string WwwRootPath { get; }

        /// <summary>
        /// Initialises a new instance of <see cref="ApplicationPathService"/>.
        /// </summary>
        /// <param name="contentRootPath">
        /// The absolute path to use as the content root.
        /// When <see langword="null"/> or whitespace, <see cref="AppContext.BaseDirectory"/> is used.
        /// </param>
        public ApplicationPathService(string? contentRootPath)
        {
            var contentRoot = string.IsNullOrWhiteSpace(contentRootPath)
                ? AppContext.BaseDirectory
                : contentRootPath;

            ContentRootPath = Path.GetFullPath(contentRoot);
            ConfigRootPath = ResolveFromContentRoot("config");
            LogsRootPath = ResolveFromConfig("logs");
            FfmpegRootPath = ResolveFromConfig("ffmpeg");
            ToolsRootPath = ResolveFromContentRoot("tools");
            WwwRootPath = ResolveFromContentRoot("wwwroot");
        }

        /// <inheritdoc/>
        public string ResolveFromContentRoot(params string[] segments)
            => Combine(ContentRootPath, segments);

        /// <inheritdoc/>
        public string ResolveFromConfig(params string[] segments)
            => Combine(ConfigRootPath, segments);

        private static string Combine(string basePath, params string[] segments)
        {
            var current = Path.GetFullPath(basePath);
            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                if (Path.IsPathRooted(segment))
                {
                    throw new ArgumentException("Path segments must be relative.", nameof(segments));
                }

                current = Path.Join(current, segment);
            }

            return Path.GetFullPath(current);
        }
    }
}
