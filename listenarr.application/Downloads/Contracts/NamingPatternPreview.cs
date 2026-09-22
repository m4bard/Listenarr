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

namespace Listenarr.Application.Downloads.Contracts;

/// <summary>
/// Rendered samples for the three naming patterns in settings, produced by the same
/// renderer used at import and rename time so the settings screen cannot diverge from it.
/// </summary>
public class NamingPatternPreview
{
    public string FolderExample { get; set; } = string.Empty;
    public string SingleFileExample { get; set; } = string.Empty;
    public List<string> MultiFileExamples { get; set; } = new();

    /// <summary>
    /// True when the multi-file pattern renders the same name for every file in a
    /// multi-file audiobook, which would collide on disk.
    /// </summary>
    public bool MultiFileAmbiguous { get; set; }
}
