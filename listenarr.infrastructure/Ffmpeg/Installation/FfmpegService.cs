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
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Ffmpeg.Installation
{
    /// <summary>
    /// Resolves and runs ffprobe. In this option for upstream #791 the binary is provided by the
    /// Openur.FFprobeStatic NuGet package and restored at build time, so nothing is downloaded at
    /// first boot and the version is pinned in Directory.Packages.props like any other dependency.
    /// A binary in the configured ffmpeg directory is used only when no packaged binary matches
    /// the running platform; while a packaged one exists, a hand-placed or previously downloaded
    /// copy there is ignored, even if the packaged one later fails to run.
    /// </summary>
    public partial class FfmpegService : IFfmpegService
    {
        // Must match the Openur.FFprobeStatic version in Directory.Packages.props; a test enforces it.
        internal const string PackagedVersion = "9.0.2.508";

        internal const string PackagedLicenseNotice =
            "ffprobe is a static FFmpeg build shipped with Listenarr in the Openur.FFprobeStatic "
            + PackagedVersion + " package. Review FFmpeg licensing (LGPL/GPL) at https://ffmpeg.org/legal.html";

        internal const string LegacyLicenseNotice =
            "ffprobe was found in the ffmpeg directory under the config directory and did not ship with Listenarr. "
            + "Its licence depends on where it came from; review FFmpeg licensing (LGPL/GPL) at https://ffmpeg.org/legal.html";

        private const UnixFileMode ExecuteBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

        private readonly string _applicationBaseDirectory;
        private readonly IReadOnlyList<string> _runtimeIdentifiers;
        private readonly bool _isWindows;
        private readonly string _legacyDirectory;
        private readonly ILogger<FfmpegService> _logger;
        private readonly IProcessRunner _processRunner;

        public FfmpegService(
            ILogger<FfmpegService> logger,
            IProcessRunner processRunner,
            IApplicationPathService applicationPathService,
            string? applicationBaseDirectory = null,
            string? runtimeIdentifier = null)
        {
            _logger = logger;
            _processRunner = processRunner;
            _applicationBaseDirectory = applicationBaseDirectory ?? AppContext.BaseDirectory;
            _runtimeIdentifiers = runtimeIdentifier == null
                ? FfprobeBinaryLocator.CurrentRuntimeIdentifiers()
                : new[] { runtimeIdentifier }.Concat(FfprobeBinaryLocator.CurrentRuntimeIdentifiers()).Distinct(StringComparer.Ordinal).ToArray();
            _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            _legacyDirectory = applicationPathService.FfmpegRootPath;
        }

        /// <summary>
        /// The packaged binary first, then one in the configured ffmpeg directory. A binary an older
        /// release downloaded into that directory is therefore superseded by the packaged one.
        /// </summary>
        private string? ResolveFfprobePath()
        {
            var packaged = ResolvePackagedFfprobePath();
            if (packaged != null)
            {
                return packaged;
            }

            var legacy = Path.Join(_legacyDirectory, FfprobeBinaryLocator.ExecutableName(_isWindows));
            return File.Exists(legacy) ? legacy : null;
        }

        private string? ResolvePackagedFfprobePath()
        {
            return FfprobeBinaryLocator.Locate(_applicationBaseDirectory, _runtimeIdentifiers, _isWindows);
        }

        /// <summary>
        /// Return the ffprobe path if a packaged or configured binary exists. Never downloads.
        /// </summary>
        public Task<string?> GetFfprobePathAsync()
        {
            var path = ResolveFfprobePath();
            if (path != null)
            {
                _logger.LogInformation("Found ffprobe at {Path}", path);
            }
            else
            {
                _logger.LogInformation(
                    "No ffprobe found for runtimes {Rids} under {ApplicationBase} or {LegacyDirectory}",
                    string.Join(", ", _runtimeIdentifiers),
                    _applicationBaseDirectory,
                    _legacyDirectory);
            }

            return Task.FromResult(path);
        }

        /// <summary>
        /// Resolve ffprobe and make sure it can be executed. Never downloads. NuGet restore leaves
        /// the binary executable by its owner only (0766 less the umask), and an unzip tool or an
        /// artifact transfer can strip even that, so the execute bits are restored here when this
        /// process owns the file. A foreign-owned install cannot be changed from here, which is
        /// why the Docker image sets the mode at build time.
        /// </summary>
        public Task<string?> EnsureFfprobeInstalledAsync()
        {
            var path = ResolveFfprobePath();
            if (path == null)
            {
                _logger.LogWarning(
                    "No packaged ffprobe for runtimes {Rids}; audio metadata extraction is unavailable until one is placed at {LegacyDirectory}",
                    string.Join(", ", _runtimeIdentifiers),
                    _legacyDirectory);
                return Task.FromResult<string?>(null);
            }

            if (!OperatingSystem.IsWindows())
            {
                EnsureExecutable(path);
            }

            return Task.FromResult<string?>(path);
        }

        [UnsupportedOSPlatform("windows")]
        private void EnsureExecutable(string path)
        {
            try
            {
                var mode = File.GetUnixFileMode(path);
                if ((mode & ExecuteBits) != ExecuteBits)
                {
                    File.SetUnixFileMode(path, mode | ExecuteBits);
                    _logger.LogInformation("Set execute permission on ffprobe at {Path}", path);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                _logger.LogWarning(ex, "Could not set execute permission on ffprobe at {Path}", path);
            }
        }
    }
}
