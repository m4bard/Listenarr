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

namespace Listenarr.Infrastructure.Ffmpeg.Installation
{
    /// <summary>
    /// Finds the ffprobe binary restored from the Openur.FFprobeStatic NuGet package, the same
    /// package Sonarr ships. The package carries one native asset per RID under
    /// <c>runtimes/&lt;rid&gt;/native/</c>. A RID-specific publish (every release artifact and
    /// the CI Docker images) flattens the current RID's asset next to the application; a
    /// RID-agnostic build (<c>dotnet run</c>, the tests, the root Dockerfile) keeps the
    /// <c>runtimes</c> tree. Both layouts are checked, and only for the requested RID, so a
    /// binary built for another platform is never picked up.
    /// </summary>
    internal static class FfprobeBinaryLocator
    {
        public static string ExecutableName(bool isWindows) => isWindows ? "ffprobe.exe" : "ffprobe";

        public static IReadOnlyList<string> CandidatePaths(string applicationBaseDirectory, string runtimeIdentifier, bool isWindows)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);

            var executable = ExecutableName(isWindows);
            return
            [
                Path.Join(applicationBaseDirectory, executable),
                Path.Join(applicationBaseDirectory, "runtimes", runtimeIdentifier, "native", executable)
            ];
        }

        public static string? Locate(string applicationBaseDirectory, string runtimeIdentifier, bool isWindows)
        {
            return CandidatePaths(applicationBaseDirectory, runtimeIdentifier, isWindows).FirstOrDefault(File.Exists);
        }
    }
}
