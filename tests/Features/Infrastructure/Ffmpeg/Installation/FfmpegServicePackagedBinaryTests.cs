using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Ffmpeg.Installation
{
    /// <summary>
    /// The NuGet option for upstream #791: ffprobe comes from a package restored at build time,
    /// so startup resolves a binary that is already on disk and never downloads one.
    /// </summary>
    [Trait("Name", "FfmpegServicePackagedBinaryTests")]
    [Trait("Category", "FfmpegService")]
    public class FfmpegServicePackagedBinaryTests : BaseTests
    {
        private static string ExecutableName => OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";

        [Fact]
        public async Task EnsureFfprobeInstalledAsync_PackagedBinary_ResolvesAndProbesRealAudio()
        {
            var legacyDirectory = Path.Join(FileService.GetTempPath(), "ffmpeg-legacy-unused");
            // A plain runner rather than SystemProcessRunner: that one regex-redacts every
            // process's stdout against process-wide secret values, and under the full parallel
            // suite another test's values corrupted the JSON. This test is about the binary.
            var service = CreateService(
                new UnredactedProcessRunner(),
                legacyDirectory,
                applicationBaseDirectory: null);

            var path = await service.EnsureFfprobeInstalledAsync();

            Assert.NotNull(path);
            Assert.StartsWith(Path.GetFullPath(AppContext.BaseDirectory), Path.GetFullPath(path));
            Assert.Equal(path, await service.GetFfprobePathAsync());

            // Run the packaged binary for real against a synthesized one-second WAV, through the
            // same RunFfprobeAsync path the scanner uses, so this proves the binary executes on
            // this platform and its output still maps.
            var wavPath = Path.Join(FileService.GetTempDirectory("ffprobe-real-audio"), "one-second.wav");
            await File.WriteAllBytesAsync(wavPath, BuildSilentWav(sampleRate: 8000, seconds: 1));

            var metadata = await service.RunFfprobeAsync(wavPath);

            Assert.InRange(metadata.Duration.TotalSeconds, 0.99, 1.01);
            Assert.Equal(8000, metadata.SampleRate);
            Assert.Equal(1, metadata.Channels);
            Assert.False(Directory.Exists(legacyDirectory));
        }

        [Fact]
        public async Task EnsureFfprobeInstalledAsync_NothingPackagedOrPresent_ReturnsNullAndTouchesNothing()
        {
            // Regression guard for the removed first-boot download: with no packaged binary and
            // no legacy one, startup used to create the ffmpeg directory and fetch an archive.
            // Now it reports absence and leaves the filesystem alone.
            var emptyApplicationBase = FileService.GetTempDirectory("ffprobe-empty-app");
            var legacyDirectory = Path.Join(FileService.GetTempPath(), "ffmpeg-never-created");
            var processRunner = new Mock<IProcessRunner>(MockBehavior.Strict);
            var service = CreateService(processRunner.Object, legacyDirectory, emptyApplicationBase);

            var path = await service.EnsureFfprobeInstalledAsync();

            Assert.Null(path);
            Assert.Null(await service.GetFfprobePathAsync());
            Assert.False(Directory.Exists(legacyDirectory));
            Assert.Empty(Directory.EnumerateFileSystemEntries(emptyApplicationBase));
        }

        [Fact]
        public void FfmpegInfrastructure_HasNoNetworkDependency()
        {
            var networkTypes = new[] { typeof(HttpClient), typeof(IHttpClientFactory), typeof(HttpMessageHandler) };
            var offenders = typeof(FfmpegService).Assembly
                .GetTypes()
                .Where(type => type.Namespace?.StartsWith("Listenarr.Infrastructure.Ffmpeg", StringComparison.Ordinal) == true)
                .SelectMany(type => type
                    .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType))
                    .Concat(type
                        .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .Select(field => field.FieldType))
                    .Where(memberType => networkTypes.Any(networkType => networkType.IsAssignableFrom(memberType)))
                    .Select(memberType => $"{type.FullName} -> {memberType.Name}"))
                .Distinct()
                .ToArray();

            Assert.True(
                offenders.Length == 0,
                $"ffprobe provisioning must not reach the network: {string.Join(", ", offenders)}");
        }

        [Fact]
        public async Task GetFfprobePathAsync_PackagedAndLegacyBothPresent_PrefersPackaged()
        {
            var applicationBase = FileService.GetTempDirectory("ffprobe-app");
            var packaged = Path.Join(applicationBase, ExecutableName);
            await File.WriteAllTextAsync(packaged, "packaged");
            var legacyDirectory = FileService.GetTempDirectory("ffprobe-legacy");
            await File.WriteAllTextAsync(Path.Join(legacyDirectory, ExecutableName), "downloaded by an older release");
            var service = CreateService(new Mock<IProcessRunner>().Object, legacyDirectory, applicationBase);

            Assert.Equal(packaged, await service.GetFfprobePathAsync());
        }

        [Fact]
        public async Task GetFfprobePathAsync_OnlyLegacyPresent_FallsBackToIt()
        {
            var applicationBase = FileService.GetTempDirectory("ffprobe-app-empty");
            var legacyDirectory = FileService.GetTempDirectory("ffprobe-legacy-only");
            var legacy = Path.Join(legacyDirectory, ExecutableName);
            await File.WriteAllTextAsync(legacy, "placed by hand");
            var service = CreateService(new Mock<IProcessRunner>().Object, legacyDirectory, applicationBase);

            Assert.Equal(legacy, await service.GetFfprobePathAsync());
        }

        [LinuxFact]
        [SupportedOSPlatform("linux")]
        public async Task EnsureFfprobeInstalledAsync_PackagedBinaryWithoutExecuteBit_IsMadeExecutable()
        {
            // nupkg entries carry no Unix mode, so a restored ffprobe can land as 0644.
            var applicationBase = FileService.GetTempDirectory("ffprobe-mode");
            var packaged = Path.Join(applicationBase, "ffprobe");
            await File.WriteAllTextAsync(packaged, "packaged");
            File.SetUnixFileMode(packaged, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            var service = CreateService(
                new Mock<IProcessRunner>(MockBehavior.Strict).Object,
                Path.Join(FileService.GetTempPath(), "ffmpeg-unused"),
                applicationBase);

            Assert.Equal(packaged, await service.EnsureFfprobeInstalledAsync());

            var mode = File.GetUnixFileMode(packaged);
            Assert.True(mode.HasFlag(UnixFileMode.UserExecute), $"mode was {mode}");
            Assert.True(mode.HasFlag(UnixFileMode.GroupExecute), $"mode was {mode}");
            Assert.True(mode.HasFlag(UnixFileMode.OtherExecute), $"mode was {mode}");
        }

        private static FfmpegService CreateService(
            IProcessRunner processRunner,
            string legacyDirectory,
            string? applicationBaseDirectory)
        {
            return new FfmpegService(
                new Mock<ILogger<FfmpegService>>().Object,
                processRunner,
                Mock.Of<IApplicationPathService>(service => service.FfmpegRootPath == legacyDirectory),
                applicationBaseDirectory,
                RuntimeInformation.RuntimeIdentifier);
        }

        private sealed class UnredactedProcessRunner : IProcessRunner
        {
            public async Task<ProcessResult> RunAsync(System.Diagnostics.ProcessStartInfo startInfo, int timeoutMs = 60000, CancellationToken cancellationToken = default)
            {
                using var process = System.Diagnostics.Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Process did not start");
                var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(timeoutMs);
                await process.WaitForExitAsync(timeout.Token);
                return new ProcessResult(process.ExitCode, await stdout, await stderr, false);
            }

            public System.Diagnostics.Process StartProcess(System.Diagnostics.ProcessStartInfo startInfo)
            {
                throw new NotSupportedException();
            }

            public IDisposable RegisterTransientSensitive(IEnumerable<string> values)
            {
                throw new NotSupportedException();
            }
        }

        private static byte[] BuildSilentWav(int sampleRate, int seconds)
        {
            const short channels = 1;
            const short bitsPerSample = 16;
            var dataLength = sampleRate * seconds * channels * (bitsPerSample / 8);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + dataLength);
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * (bitsPerSample / 8));
            writer.Write((short)(channels * (bitsPerSample / 8)));
            writer.Write(bitsPerSample);
            writer.Write("data"u8.ToArray());
            writer.Write(dataLength);
            writer.Write(new byte[dataLength]);
            writer.Flush();
            return stream.ToArray();
        }
    }
}
