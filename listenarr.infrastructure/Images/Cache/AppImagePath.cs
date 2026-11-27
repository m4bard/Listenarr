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

using System.Text.RegularExpressions;

namespace Listenarr.Infrastructure.Images.Cache
{
    /// <summary>
    /// Recognises Listenarr's own image path ("/api/v{n}/images/{id}", optionally with a "?url=" query).
    /// Such a path is not a download source: on Linux Uri.TryCreate reads it as an absolute file:// URI.
    /// </summary>
    internal static partial class AppImagePath
    {
        [GeneratedRegex(@"^/api/v\d+(?:\.\d+)?/images/[^/?#\\]+(?:\?[^#]*)?$", RegexOptions.IgnoreCase)]
        private static partial Regex AppImagePathRegex();

        public static bool IsAppImagePath(string imageUrl) => AppImagePathRegex().IsMatch(imageUrl);
    }
}
