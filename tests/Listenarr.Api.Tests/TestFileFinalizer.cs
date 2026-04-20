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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Listenarr.Api.Services;

namespace Listenarr.Api.Tests
{
    // Lightweight test fallback for IFileFinalizer used by tests that don't register an IImportService.
    internal class TestFileFinalizer : IFileFinalizer
    {
        private readonly IImportService? _importService;

        public TestFileFinalizer(IImportService? importService)
        {
            _importService = importService;
        }

        public Task<List<ImportResult>> ImportFilesFromDirectoryAsync(string downloadId, int? audiobookId, IEnumerable<string> files, ApplicationSettings settings)
        {
            if (_importService != null)
            {
                return _importService.ImportFilesFromDirectoryAsync(downloadId, audiobookId, files, settings);
            }

            var results = files.Select(f => new ImportResult
            {
                Success = true,
                SourcePath = f,
                FinalPath = f
            }).ToList();

            return Task.FromResult(results);
        }

        public Task<ImportResult> ImportSingleFileAsync(string downloadId, int? audiobookId, string sourcePath, ApplicationSettings settings)
        {
            if (_importService != null)
            {
                return _importService.ImportSingleFileAsync(downloadId, audiobookId, sourcePath, settings);
            }

            var result = new ImportResult
            {
                Success = true,
                SourcePath = sourcePath,
                FinalPath = sourcePath
            };

            return Task.FromResult(result);
        }
    }
}
