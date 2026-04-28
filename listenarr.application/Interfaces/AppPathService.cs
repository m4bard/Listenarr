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

namespace Listenarr.Application.Interfaces
{
    public interface IAppPathService
    {
        string ContentRootPath { get; }
        string ConfigRootPath { get; }
        string LogsRootPath { get; }
        string FfmpegRootPath { get; }
        string ToolsRootPath { get; }
        string WwwRootPath { get; }
        string ResolveFromContentRoot(params string[] segments);
        string ResolveFromConfig(params string[] segments);
    }

    public sealed class AppPathService : IAppPathService
    {
        public string ContentRootPath { get; }
        public string ConfigRootPath { get; }
        public string LogsRootPath { get; }
        public string FfmpegRootPath { get; }
        public string ToolsRootPath { get; }
        public string WwwRootPath { get; }

        public AppPathService(IHostEnvironment hostEnvironment)
        {
            var contentRoot = string.IsNullOrWhiteSpace(hostEnvironment.ContentRootPath)
                ? AppContext.BaseDirectory
                : hostEnvironment.ContentRootPath;

            ContentRootPath = Path.GetFullPath(contentRoot);
            ConfigRootPath = ResolveFromContentRoot("config");
            LogsRootPath = ResolveFromConfig("logs");
            FfmpegRootPath = ResolveFromConfig("ffmpeg");
            ToolsRootPath = ResolveFromContentRoot("tools");
            WwwRootPath = ResolveFromContentRoot("wwwroot");
        }

        public string ResolveFromContentRoot(params string[] segments)
            => Combine(ContentRootPath, segments);

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
