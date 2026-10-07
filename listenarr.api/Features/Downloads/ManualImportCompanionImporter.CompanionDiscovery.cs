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
 */

using Listenarr.Domain.Common;

namespace Listenarr.Api.Features.Downloads;

public sealed partial class ManualImportCompanionImporter
{
    /// <summary>
    /// Names, once per batch, every file that sits below a selected file's directory and would
    /// otherwise vanish without a trace: <paramref name="companionFiles"/> was built with
    /// <c>SearchOption.TopDirectoryOnly</c>, so a file one or more directories deeper is never a
    /// candidate at all and never reaches any of this importer's per-file log lines. This method
    /// re-walks the same directories recursively purely to find what that scope left out, and
    /// logs a warning identifying the file and the selected directory it is nested under, so the
    /// loss is observable instead of silent. It does not change what gets imported.
    /// </summary>
    private void LogNestedCompanionCandidatesSkipped(
        IReadOnlyCollection<string?> selectedDirectories,
        IReadOnlyCollection<string> selectedSourceFiles,
        IReadOnlyCollection<string> companionFiles,
        FileSystemPathSemantics sourceSemantics,
        IEnumerable<string> importBlacklist)
    {
        var selectedSourceFileSet = selectedSourceFiles as HashSet<string>
            ?? new HashSet<string>(selectedSourceFiles, sourceSemantics.Comparer);
        var companionFileSet = companionFiles as HashSet<string>
            ?? new HashSet<string>(companionFiles, sourceSemantics.Comparer);

        foreach (var directory in selectedDirectories)
        {
            if (directory == null || !_fileSystem.DirectoryExists(directory))
            {
                continue;
            }

            var nestedCandidates = _fileSystem
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(file => !FileUtils.IsBlacklistedFile(file, importBlacklist))
                .Select(Path.GetFullPath)
                .Where(file => !selectedSourceFileSet.Contains(file)
                    && !companionFileSet.Contains(file))
                .Distinct(sourceSemantics.Comparer);

            foreach (var nestedFile in nestedCandidates)
            {
                _logger.LogWarning(
                    "Skipping possible companion file {FilePath} because it is nested under a " +
                    "subdirectory of {SelectedDirectory} rather than sitting directly beside the " +
                    "selected file; manual import only sweeps up files at the same directory level " +
                    "as the file they accompany, never into nested subdirectories",
                    nestedFile,
                    directory);
            }
        }
    }
}
