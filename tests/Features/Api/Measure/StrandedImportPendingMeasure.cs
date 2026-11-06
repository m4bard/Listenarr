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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Listenarr.Tests.Mocks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Listenarr.Tests.Features.Api.Measure
{
    /// <summary>
    /// Local measurement harness (not for upstream): drives the real controllers, repositories,
    /// processors and scan pipeline against a temp sqlite DB and temp directories, with only the
    /// download client replaced by a recording fake. Results go to MEASURE_OUT as JSON lines.
    /// </summary>
    public class StrandedImportPendingMeasure
    {
        private const string BookPresentTitle = "A Princess of Mars";
        private const string BookPresentAsin = "B008DFUGCQ";
        private const string BookPresentAuthor = "Edgar Rice Burroughs";
        private const string BookZeroTitle = "The Wonderful Wizard of Oz";
        private const string BookZeroAsin = "B007BR5KZA";
        private const string BookZeroAuthor = "L. Frank Baum";
        private const string BookStaleTitle = "Twenty Thousand Leagues Under the Sea";
        private const string BookStaleAsin = "B01FKWL15A";
        private const string BookStaleAuthor = "Jules Verne";

        private static readonly object OutLock = new();

        // ------------------------------------------------------------------ fake client

        private sealed class RecordingGateway : IDownloadClientGateway
        {
            public ConcurrentQueue<string> Calls { get; } = new();

            private void Rec(string text) => Calls.Enqueue(text);

            public Task<(bool Success, string Message)> TestConnectionAsync(DownloadClientConfiguration client, CancellationToken ct = default)
            {
                Rec("TestConnection");
                return Task.FromResult((true, "ok"));
            }

            public Task<DownloadClientSubmissionResult> AddAsync(DownloadClientConfiguration client, PreparedDownloadSubmission submission, CancellationToken ct = default)
            {
                Rec("Add");
                throw new NotSupportedException();
            }

            public Task<bool> RemoveAsync(DownloadClientConfiguration client, string id, bool deleteFiles = false, CancellationToken ct = default)
            {
                Rec($"Remove id={id} deleteFiles={deleteFiles} policy={client.RemoveCompletedDownloads}");
                return Task.FromResult(true);
            }

            public Task<List<QueueItem>> GetQueueAsync(DownloadClientConfiguration client, CancellationToken ct = default)
            {
                Rec("GetQueue");
                return Task.FromResult(new List<QueueItem>());
            }

            public Task<DownloadClientQueueSnapshot> GetQueueSnapshotAsync(DownloadClientConfiguration client, CancellationToken ct = default)
            {
                Rec("GetQueueSnapshot");
                return Task.FromResult(DownloadClientQueueSnapshot.Complete(new List<QueueItem>()));
            }

            public Task<QueueItem> GetQueueItemAsync(DownloadClientConfiguration client, Download download, QueueItem queueItem, CancellationToken ct = default)
            {
                Rec("GetQueueItem");
                return Task.FromResult(queueItem);
            }

            public Task<bool> MarkItemAsImportedAsync(DownloadClientConfiguration client, Download download, CancellationToken ct = default)
            {
                Rec("MarkItemAsImported");
                return Task.FromResult(true);
            }

            public Task<List<Download>> FetchDownloadsAsync(DownloadClientConfiguration client, List<Download> downloads, CancellationToken cancellationToken = default, ISet<string>? reportedDownloadIds = null)
            {
                Rec("FetchDownloads");
                return Task.FromResult(downloads);
            }
        }

        private sealed class ErrorFileLoggerProvider(string path) : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName) => new FileLogger(path, categoryName);
            public void Dispose() { }

            private sealed class FileLogger(string path, string category) : ILogger
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    if (!IsEnabled(logLevel)) return;
                    lock (OutLock)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.AppendAllText(path, $"{logLevel} {category}: {formatter(state, exception)} {exception}\n");
                    }
                }
            }
        }

        // ------------------------------------------------------------------ rig

        private sealed record FileFacts(long Size, string Sha256, ulong Inode);

        private sealed record Snapshot(
            Dictionary<string, FileFacts> Files,
            List<(int Id, int AudiobookId, string? Path)> LibraryRows,
            List<string> History,
            int DownloadHistoryCount,
            List<(string Id, string Status)> Downloads,
            List<(string Id, string DownloadId, string Status)> Jobs);

        private sealed class Rig : IDisposable
        {
            public required string Root { get; init; }
            public required ListenarrWebApplicationFactory Base { get; init; }
            public required WebApplicationFactoryHandle Handle { get; init; }
            public required RecordingGateway Gateway { get; init; }
            public string Lib => Path.Join(Root, "lib");
            public string Dl => Path.Join(Root, "dl");
            public string ClientId { get; set; } = string.Empty;
            public HttpClient Http => Handle.Http;

            public void Dispose()
            {
                Handle.Http.Dispose();
                Handle.Factory.Dispose();
                Base.Dispose();
            }
        }

        private sealed record WebApplicationFactoryHandle(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory, HttpClient Http);

        private static Rig NewRig(string caseName, string policy)
        {
            var shm = Environment.GetEnvironmentVariable("MEASURE_DIR") ?? "/dev/shm/STRANDED-measure";
            var root = Path.Join(shm, $"{caseName}-{Guid.NewGuid():N}"[..12]);
            Directory.CreateDirectory(Path.Join(root, "lib"));
            Directory.CreateDirectory(Path.Join(root, "dl"));

            var gateway = new RecordingGateway();
            var baseFactory = new ListenarrWebApplicationFactory();
            var factory = baseFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ILoggerFactory>();
                    services.AddSingleton<ILoggerFactory>(_ => LoggerFactory.Create(logging => logging.AddProvider(new ErrorFileLoggerProvider(Path.Join(shm, "server.log")))));
                    // hosted workers are off in the test host, so the pieces the pipeline needs are registered here and driven by hand
                    services.TryAddSingleton<IScanQueueService, ScanQueueService>();
                    services.TryAddSingleton<MoveScanHandoffRecoveryService>();
                    services.TryAddSingleton<ScanJobProcessor>();
                    services.TryAddSingleton<IScanJobProcessor>(sp => sp.GetRequiredService<ScanJobProcessor>());
                    services.TryAddSingleton<MovedDownloadCleanupProcessor>();
                    services.TryAddSingleton<IMovedDownloadCleanupProcessor>(sp => sp.GetRequiredService<MovedDownloadCleanupProcessor>());
                    services.TryAddSingleton<DownloadProcessingJobProcessor>();
                    services.TryAddSingleton<IDownloadImportProcessor>(sp => sp.GetRequiredService<DownloadProcessingJobProcessor>());
                    services.TryAddSingleton<DownloadProcessingJobCleanupProcessor>();
                    services.TryAddSingleton<IDownloadProcessingJobCleanupProcessor>(sp => sp.GetRequiredService<DownloadProcessingJobCleanupProcessor>());
                    services.RemoveAll<IDownloadClientGateway>();
                    services.AddSingleton<IDownloadClientGateway>(gateway);

                    services.RemoveAll<IFfmpegService>();
                    services.AddSingleton<IFfmpegService>(_ =>
                    {
                        var mock = new Mock<IFfmpegService>();
                        mock.Setup(f => f.GetFfprobePathAsync()).ReturnsAsync("/usr/bin/ffprobe");
                        mock.Setup(f => f.EnsureFfprobeInstalledAsync()).ReturnsAsync("/usr/bin/ffprobe");
                        return mock.Object;
                    });
                });
            });
            var http = factory.CreateClient();
            var tokenResponse = http.GetAsync("/api/v1/antiforgery/token").GetAwaiter().GetResult();
            tokenResponse.EnsureSuccessStatusCode();
            using (var tokenJson = JsonDocument.Parse(tokenResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()))
            {
                http.DefaultRequestHeaders.Add("X-XSRF-TOKEN", tokenJson.RootElement.GetProperty("token").GetString());
            }
            var rig = new Rig { Root = root, Base = baseFactory, Handle = new WebApplicationFactoryHandle(factory, http), Gateway = gateway };

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
            var client = new DownloadClientConfiguration
            {
                Id = Guid.NewGuid().ToString(),
                Name = "fake-qbit",
                Type = "qbittorrent",
                Host = "fake.invalid",
                Port = 1,
                IsEnabled = true,
                RemoveCompletedDownloads = policy,
                Settings = []
            };
            db.DownloadClientConfigurations.Add(client);
            db.SaveChanges();
            rig.ClientId = client.Id;

            var config = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var settings = config.GetApplicationSettingsAsync().GetAwaiter().GetResult();
            settings.OutputPath = rig.Lib;
            config.SaveApplicationSettingsAsync(settings).GetAwaiter().GetResult();
            var roots = scope.ServiceProvider.GetRequiredService<IRootFolderService>();
            var existing = roots.GetAllAsync().GetAwaiter().GetResult();
            File.AppendAllText(Path.Join(shm, "roots.log"), $"{caseName}: {string.Join(" | ", existing.Select(r => $"{r.Id}:{r.Path}:{r.IsDefault}"))}\n");
            foreach (var stale in existing)
            {
                roots.DeleteAsync(stale.Id).GetAwaiter().GetResult();
            }

            roots.CreateAsync(new RootFolder { Name = "lib", Path = rig.Lib, IsDefault = true }).GetAwaiter().GetResult();
            return rig;
        }

        private static async Task<int> AddBookAsync(Rig rig, string title, string asin, string author)
        {
            using var scope = rig.Handle.Factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
            var book = await repo.AddAsync(new Audiobook
            {
                Title = title,
                Asin = asin,
                Authors = [author],
                Monitored = true,
                BasePath = Path.Join(rig.Lib, author, title)
            });
            return book.Id;
        }

        private static async Task<string> AddStrandedDownloadAsync(Rig rig, int bookId, string title, string sourceDir, int fileCount)
        {
            using var scope = rig.Handle.Factory.Services.CreateScope();
            var downloads = scope.ServiceProvider.GetRequiredService<IDownloadRepository>();
            var jobs = scope.ServiceProvider.GetRequiredService<IDownloadProcessingJobRepository>();
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sourceDir))).ToLowerInvariant();
            var download = new Download
            {
                Id = Guid.NewGuid().ToString(),
                Title = title,
                AudiobookId = bookId,
                DownloadClientId = rig.ClientId,
                Status = DownloadStatus.ImportPending,
                DownloadPath = sourceDir,
                FinalPath = string.Empty,
                StartedAt = DateTime.UtcNow.AddDays(-30),
                Progress = 100,
                Metadata = new Dictionary<string, object> { ["TorrentHash"] = hash },
                ImportBlockMessages = []
            };
            await downloads.AddAsync(download);

            var job = new DownloadProcessingJob
            {
                Id = Guid.NewGuid().ToString(),
                DownloadId = download.Id,
                JobType = ProcessingJobType.MoveOrCopyFile,
                DownloadClientId = rig.ClientId,
                Status = ProcessingJobStatus.Completed,
                CreatedAt = DateTime.UtcNow.AddDays(-30),
                CompletedAt = DateTime.UtcNow.AddDays(-30),
                SourcePath = sourceDir
            };
            job.GetOrCreateCorrelationId();
            job.SetCheckpoint("FilesImported", fileCount);
            job.SetCheckpoint("ClientMarkedImported");
            job.SetCheckpoint("ScanEnqueued", Guid.NewGuid().ToString());
            job.JobData[Download.SourceRetainedMetadataKey] = false;
            job.SetCheckpoint("ImportCommitted");
            job.AddLogEntry("Job completed successfully");
            await jobs.AddAsync(job);
            return download.Id;
        }

        // ------------------------------------------------------------------ audio fixtures

        private static void MakeAudio(string path, int hz, string title)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var psi = new ProcessStartInfo("ffmpeg")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (var arg in new[]
            {
                "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"sine=frequency={hz}:duration=2",
                "-c:a", "libmp3lame", "-b:a", "64k", "-metadata", $"title={title}", path
            })
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi)!;
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        // ------------------------------------------------------------------ observation

        private static async Task<Snapshot> SnapAsync(Rig rig)
        {
            var files = new Dictionary<string, FileFacts>();
            foreach (var path in Directory.EnumerateFiles(rig.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var info = new FileInfo(path);
                var inode = GetInode(path);
                await using var stream = File.OpenRead(path);
                var sha = Convert.ToHexString(await SHA256.HashDataAsync(stream))[..12];
                files[Path.GetRelativePath(rig.Root, path)] = new FileFacts(info.Length, sha, inode);
            }

            using var scope = rig.Handle.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
            var rows = (await db.AudiobookFiles.AsNoTracking().OrderBy(f => f.Id).ToListAsync())
                .Select(f => (f.Id, f.AudiobookId, f.Path is null ? null : Path.GetRelativePath(rig.Root, f.Path)))
                .ToList();
            var history = (await db.History.AsNoTracking().OrderBy(h => h.Id).ToListAsync())
                .Select(h => $"{h.EventType}|{h.Outcome}|{h.Source}|{Trim(h.Message)}")
                .ToList();
            var downloadHistoryCount = await db.DownloadHistories.CountAsync();
            var downloads = (await db.Downloads.AsNoTracking().ToListAsync()).Select(d => (d.Id[..8], d.Status.ToString())).ToList();
            var jobs = (await db.DownloadProcessingJobs.AsNoTracking().ToListAsync()).Select(j => (j.Id[..8], j.DownloadId[..8], j.Status.ToString())).ToList();
            return new Snapshot(files, rows, history, downloadHistoryCount, downloads, jobs);
        }

        private static string Trim(string? value) => value is null ? "" : value.Length > 110 ? value[..110] : value;

        [System.Runtime.InteropServices.DllImport("libc", SetLastError = true, EntryPoint = "stat")]
        private static extern int LibcStat(string path, byte[] buf);

        private static ulong GetInode(string path)
        {
            var buf = new byte[256];
            return LibcStat(path, buf) == 0 ? BitConverter.ToUInt64(buf, 8) : 0;
        }

        private static JsonObject Diff(Rig rig, string step, Snapshot before, Snapshot after, IEnumerable<string>? extraClientCalls = null)
        {
            var added = after.Files.Keys.Except(before.Files.Keys).Order().ToList();
            var removed = before.Files.Keys.Except(after.Files.Keys).Order().ToList();
            var modified = before.Files.Keys.Intersect(after.Files.Keys).Where(k => before.Files[k] != after.Files[k]).Order().ToList();
            var rowsAdded = after.LibraryRows.Where(r => before.LibraryRows.All(b => b.Id != r.Id)).ToList();
            var rowsRemoved = before.LibraryRows.Where(r => after.LibraryRows.All(a => a.Id != r.Id)).ToList();
            var rowsChanged = after.LibraryRows.Where(r => before.LibraryRows.Any(b => b.Id == r.Id && b != r)).ToList();
            var newHistory = after.History.Skip(before.History.Count).ToList();
            var obj = new JsonObject
            {
                ["step"] = step,
                ["files_added"] = new JsonArray(added.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                ["files_removed"] = new JsonArray(removed.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                ["files_modified"] = new JsonArray(modified.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                ["library_rows_before"] = before.LibraryRows.Count,
                ["library_rows_after"] = after.LibraryRows.Count,
                ["library_rows_added"] = rowsAdded.Count,
                ["library_rows_removed"] = rowsRemoved.Count,
                ["library_rows_changed"] = rowsChanged.Count,
                ["history_new"] = new JsonArray(newHistory.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                ["download_history_before_after"] = $"{before.DownloadHistoryCount}->{after.DownloadHistoryCount}",
                ["downloads_after"] = new JsonArray(after.Downloads.Select(d => (JsonNode?)JsonValue.Create($"{d.Id}:{d.Status}")).ToArray()),
                ["jobs_after"] = new JsonArray(after.Jobs.Select(j => (JsonNode?)JsonValue.Create($"{j.Id}:{j.Status}")).ToArray()),
                ["client_calls_cumulative"] = new JsonArray((extraClientCalls ?? rig.Gateway.Calls).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray())
            };
            return obj;
        }

        private static void Emit(string caseName, JsonObject obj)
        {
            obj["case"] = caseName;
            var path = Environment.GetEnvironmentVariable("MEASURE_OUT") ?? "/dev/shm/STRANDED-measure/out.jsonl";
            lock (OutLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, obj.ToJsonString() + "\n");
            }
        }

        private sealed record ScanObs(int AudiobookId, string? Path, bool Authoritative, string AuthMode, string StatusAfter, string? Error);

        /// <summary>Takes every scan job off the real queue (hosted workers are off), then runs each through the real processor.</summary>
        private static async Task<List<ScanObs>> DrainAndRunScansAsync(Rig rig, bool run, string? onlyAuthoritative = null)
        {
            var queue = rig.Handle.Factory.Services.GetRequiredService<IScanQueueService>();
            var processor = rig.Handle.Factory.Services.GetRequiredService<IScanJobProcessor>();
            var obs = new List<ScanObs>();
            while (queue.Reader.TryRead(out var job))
            {
                var status = "NotRun";
                string? error = null;
                if (run)
                {
                    await processor.ProcessJobAsync(job, CancellationToken.None);
                    if (queue.TryGetJob(job.Id, out var after) && after != null)
                    {
                        status = after.Status;
                        error = after.Error;
                    }
                }

                obs.Add(new ScanObs(job.AudiobookId, job.Path is null ? null : System.IO.Path.GetRelativePath(rig.Root, job.Path), job.IsAuthoritativeScope, job.AuthorizationMode.ToString(), status, error));
            }

            return obs;
        }

        private static JsonArray ScansJson(List<ScanObs> scans) =>
            new(scans.Select(s => (JsonNode?)JsonNode.Parse(JsonSerializer.Serialize(s))).ToArray());

        private static async Task<(int Status, JsonNode? Body)> ManualImportAsync(Rig rig, string sourceDir, int bookId, IEnumerable<string> files, string action)
        {
            var body = new JsonObject
            {
                ["path"] = sourceDir,
                ["mode"] = "interactive",
                ["action"] = action,
                ["includeCompanionFiles"] = false,
                ["cleanupEmptySourceFolders"] = false,
                ["items"] = new JsonArray(files.Select(f => (JsonNode?)new JsonObject
                {
                    ["relativePath"] = Path.GetFileName(f),
                    ["fullPath"] = f,
                    ["matchedAudiobookId"] = bookId
                }).ToArray())
            };
            var response = await rig.Http.PostAsJsonAsync("/api/v1/library/manual-import", body);
            var text = await response.Content.ReadAsStringAsync();
            JsonNode? parsed = null;
            try { parsed = JsonNode.Parse(text); } catch (JsonException) { parsed = JsonValue.Create(text); }
            return ((int)response.StatusCode, parsed);
        }

        private static JsonNode? Summarise(JsonNode? body)
        {
            if (body is not JsonObject obj || obj["results"] is not JsonArray results) return body;
            return new JsonObject
            {
                ["importedCount"] = obj["importedCount"]?.DeepClone(),
                ["totalCount"] = obj["totalCount"]?.DeepClone(),
                ["results"] = new JsonArray(results.Select(r => (JsonNode?)new JsonObject
                {
                    ["success"] = r?["success"]?.DeepClone(),
                    ["skipped"] = r?["skipped"]?.DeepClone(),
                    ["skipReason"] = r?["skipReason"]?.DeepClone(),
                    ["error"] = r?["error"]?.DeepClone(),
                    ["effectiveAction"] = r?["effectiveAction"]?.DeepClone(),
                    ["sourceDisposition"] = r?["sourceDisposition"]?.DeepClone(),
                    ["warningCode"] = r?["warningCode"]?.DeepClone()
                }).ToArray())
            };
        }

        /// <summary>Imports one real file for the book through the manual-import API and runs its scan, so rows are written by the app itself.</summary>
        private static async Task SeedLibraryAsync(Rig rig, int bookId, string tag, int hz)
        {
            var first = Path.Join(rig.Root, "seed", tag);
            var file = Path.Join(first, $"{tag}.mp3");
            MakeAudio(file, hz, tag);
            var (status, seedBody) = await ManualImportAsync(rig, first, bookId, [file], "copy");
            Assert.True(status == 200, $"seed import {status}: {seedBody}");
            var scans = await DrainAndRunScansAsync(rig, run: true);
            Assert.True(scans.Count > 0, $"no scan after seed import: {Summarise(seedBody)?.ToJsonString()}");
        }

        private static async Task<HttpResponseMessage> DeleteDownloadAsync(Rig rig, string id, bool removeFromClient) =>
            await rig.Http.DeleteAsync($"/api/v1/downloads/{id}?removeFromClient={(removeFromClient ? "true" : "false")}");

        // ------------------------------------------------------------------ scenarios

        private static async Task<(Rig Rig, int BookId, string DownloadId, string SourceDir)> BuildPPresentAsync(string caseName, string policy)
        {
            var rig = NewRig(caseName, policy);
            var bookId = await AddBookAsync(rig, BookPresentTitle, BookPresentAsin, BookPresentAuthor);
            await SeedLibraryAsync(rig, bookId, "presentbook", 440);
            // the torrent's own copy of the same bytes: a separate file (own inode), as measured on the real state
            var libFile = Directory.EnumerateFiles(rig.Lib, "*.mp3", SearchOption.AllDirectories).Single();
            var sourceDir = Path.Join(rig.Dl, "present-release");
            Directory.CreateDirectory(sourceDir);
            File.Copy(libFile, Path.Join(sourceDir, "present-release.mp3"));
            var downloadId = await AddStrandedDownloadAsync(rig, bookId, BookPresentTitle, sourceDir, 1);
            return (rig, bookId, downloadId, sourceDir);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task M1_PPresent_DeleteDownloadRow(bool removeFromClient)
        {
            var name = removeFromClient ? "M1-control-A-removeFromClient-true" : "M1-removeFromClient-false";
            var (rig, bookId, downloadId, _) = await BuildPPresentAsync("m1", "remove");
            using var _r = rig;
            var before = await SnapAsync(rig);
            var callsBefore = rig.Gateway.Calls.Count;
            var scansLeftover = await DrainAndRunScansAsync(rig, run: false);
            var response = await DeleteDownloadAsync(rig, downloadId, removeFromClient);
            var after = await SnapAsync(rig);
            var scans = await DrainAndRunScansAsync(rig, run: false);
            var diff = Diff(rig, name, before, after);
            diff["http_status"] = (int)response.StatusCode;
            diff["http_body"] = await response.Content.ReadAsStringAsync();
            diff["client_calls_during_step"] = rig.Gateway.Calls.Count - callsBefore;
            diff["scans_enqueued_by_step"] = scans.Count;
            diff["scans_leftover_from_setup_discarded"] = scansLeftover.Count;
            diff["book_id"] = bookId;
            Emit(name, diff);
        }

        [Theory]
        [InlineData("remove")]
        [InlineData("remove_and_delete")]
        public async Task M2_PPresent_RetryImportThenMovedCleanup(string policy)
        {
            var name = $"M2-controlB-retry-import-policy-{policy}";
            var (rig, bookId, downloadId, sourceDir) = await BuildPPresentAsync("m2", policy);
            using var _r = rig;
            await DrainAndRunScansAsync(rig, run: false);
            var before = await SnapAsync(rig);

            var retry = await rig.Http.PostAsync($"/api/v1/downloads/{downloadId}/retry-import", null);
            var retryBody = await retry.Content.ReadAsStringAsync();
            var processor = rig.Handle.Factory.Services.GetRequiredService<IDownloadImportProcessor>();
            await processor.ProcessQueueAsync(CancellationToken.None);
            var afterRetry = await SnapAsync(rig);
            var scans = await DrainAndRunScansAsync(rig, run: false);
            var callsAfterRetry = rig.Gateway.Calls.ToList();
            var diff = Diff(rig, name + "/retry-import+processor", before, afterRetry, callsAfterRetry);
            diff["retry_http_status"] = (int)retry.StatusCode;
            diff["retry_http_body"] = retryBody;
            diff["scans_enqueued_by_step"] = scans.Count;
            Emit(name, diff);

            // MovedDownloadCleanupService: first right away (inside the 2 h grace), then 3 h later
            var cleanup = rig.Handle.Factory.Services.GetRequiredService<IMovedDownloadCleanupProcessor>();
            var snapA = await SnapAsync(rig);
            await cleanup.RunCycleAsync(CancellationToken.None);
            var snapB = await SnapAsync(rig);
            var d1 = Diff(rig, name + "/cleanup-immediately", snapA, snapB);
            d1["client_calls_new"] = rig.Gateway.Calls.Count - callsAfterRetry.Count;
            Emit(name, d1);

            using (var scope = rig.Handle.Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
                var dl = await db.Downloads.SingleAsync();
                dl.LastImportedAt = DateTime.UtcNow.AddHours(-3);
                var job = await db.DownloadProcessingJobs.SingleAsync();
                job.CompletedAt = DateTime.UtcNow.AddHours(-3);
                await db.SaveChangesAsync();
            }

            var callsBeforeLate = rig.Gateway.Calls.Count;
            await cleanup.RunCycleAsync(CancellationToken.None);
            var snapC = await SnapAsync(rig);
            var d2 = Diff(rig, name + "/cleanup-after-3h", snapB, snapC);
            d2["client_calls_new"] = new JsonArray(rig.Gateway.Calls.Skip(callsBeforeLate).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            d2["book_id"] = bookId;
            d2["source_dir_files_after"] = Directory.Exists(sourceDir) ? Directory.GetFiles(sourceDir).Length : -1;
            Emit(name, d2);
        }

        [Fact]
        public async Task M3_PZero_ManualImportThenDeleteRow()
        {
            const string name = "M3-pzero";
            var rig = NewRig("m3", "remove");
            using var _r = rig;
            var zeroBook = await AddBookAsync(rig, BookZeroTitle, BookZeroAsin, BookZeroAuthor);
            var staleBook = await AddBookAsync(rig, BookStaleTitle, BookStaleAsin, BookStaleAuthor);

            // P-zero source: 7 distinct audio files in the torrent's folder
            var zeroSource = Path.Join(rig.Dl, "zero-release");
            var zeroFiles = Enumerable.Range(1, 7).Select(i => Path.Join(zeroSource, $"Part {i:00}.mp3")).ToList();
            for (var i = 0; i < zeroFiles.Count; i++) MakeAudio(zeroFiles[i], 300 + (i * 50), $"Part {i + 1}");
            var downloadId = await AddStrandedDownloadAsync(rig, zeroBook, BookZeroTitle, zeroSource, 7);

            // control book: one file imported by the app, then its file removed from disk = a stale row
            await SeedLibraryAsync(rig, staleBook, "stalebook", 880);
            var staleFile = Directory.EnumerateFiles(Path.Join(rig.Lib, BookStaleAuthor), "*.mp3", SearchOption.AllDirectories).Single();
            File.Delete(staleFile);
            var staleSource = Path.Join(rig.Dl, "stale-release");
            var staleSourceFiles = Enumerable.Range(1, 7).Select(i => Path.Join(staleSource, $"Part {i:00}.mp3")).ToList();
            for (var i = 0; i < staleSourceFiles.Count; i++) MakeAudio(staleSourceFiles[i], 700 + (i * 20), $"Stale Part {i + 1}");
            await DrainAndRunScansAsync(rig, run: false);

            // --- P-zero manual import (Copy)
            var s0 = await SnapAsync(rig);
            var (status, body) = await ManualImportAsync(rig, zeroSource, zeroBook, zeroFiles, "copy");
            var s1 = await SnapAsync(rig);
            var scansFirst = await DrainAndRunScansAsync(rig, run: true);
            var s1b = await SnapAsync(rig);
            var d = Diff(rig, name + "/manual-import-copy-7(+its-focused-scan-run)", s0, s1b);
            d["http_status"] = status;
            d["response"] = Summarise(body);
            d["scans_enqueued_and_run"] = ScansJson(scansFirst);
            d["rows_for_book_after_import_before_scan"] = s1.LibraryRows.Count(r => r.AudiobookId == zeroBook);
            d["rows_for_book_after_scan"] = s1b.LibraryRows.Count(r => r.AudiobookId == zeroBook);
            d["distinct_paths_for_book"] = s1b.LibraryRows.Where(r => r.AudiobookId == zeroBook).Select(r => r.Path).Distinct().Count();
            d["distinct_content_hashes_in_book_folder"] = s1b.Files.Where(f => f.Key.StartsWith("lib/" + BookZeroAuthor, StringComparison.Ordinal)).Select(f => f.Value.Sha256).Distinct().Count();
            d["source_files_unchanged"] = zeroFiles.All(f => s1b.Files.TryGetValue(Path.GetRelativePath(rig.Root, f), out var facts) && facts == s0.Files[Path.GetRelativePath(rig.Root, f)]);
            Emit(name, d);

            // --- idempotency: the identical import again
            var (status2, body2) = await ManualImportAsync(rig, zeroSource, zeroBook, zeroFiles, "copy");
            var s2 = await SnapAsync(rig);
            var scansAfterRepeat = await DrainAndRunScansAsync(rig, run: true);
            var s3 = await SnapAsync(rig);
            var d2 = Diff(rig, name + "/repeat-identical-import+scan-run", s1b, s3);
            d2["http_status"] = status2;
            d2["response"] = Summarise(body2);
            d2["files_added_by_import_before_scan"] = s2.Files.Keys.Except(s1b.Files.Keys).Count();
            d2["scans_run"] = ScansJson(scansAfterRepeat);
            d2["rows_for_book_after_scan"] = s3.LibraryRows.Count(r => r.AudiobookId == zeroBook);
            Emit(name, d2);

            // --- control: same import into a book carrying 1 stale row
            var c0 = await SnapAsync(rig);
            var (cs, cb) = await ManualImportAsync(rig, staleSource, staleBook, staleSourceFiles, "copy");
            var cScans = await DrainAndRunScansAsync(rig, run: true);
            var c1 = await SnapAsync(rig);
            var dc = Diff(rig, name + "/control-stale-book-same-import+its-scan", c0, c1);
            dc["http_status"] = cs;
            dc["response"] = Summarise(cb);
            dc["scans_run"] = ScansJson(cScans);
            dc["stale_rows_before"] = c0.LibraryRows.Count(r => r.AudiobookId == staleBook);
            dc["stale_row_files_present_on_disk_before"] = c0.LibraryRows.Where(r => r.AudiobookId == staleBook).Count(r => c0.Files.ContainsKey(r.Path!));
            dc["rows_for_stale_book_after"] = c1.LibraryRows.Count(r => r.AudiobookId == staleBook);
            Emit(name, dc);

            // --- second control: an AUTHORITATIVE scan (what retry-import would enqueue) on the stale book
            using (var scope = rig.Handle.Factory.Services.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
                var queue = scope.ServiceProvider.GetRequiredService<IScanQueueService>();
                var book = (await repo.GetByIdAsync(staleBook))!;
                await queue.EnqueueScanAsync(book);
            }

            var a0 = await SnapAsync(rig);
            var aScans = await DrainAndRunScansAsync(rig, run: true);
            var a1 = await SnapAsync(rig);
            var da = Diff(rig, name + "/control-authoritative-scan-on-stale-book", a0, a1);
            da["scans_run"] = ScansJson(aScans);
            da["rows_for_stale_book_before"] = a0.LibraryRows.Count(r => r.AudiobookId == staleBook);
            da["rows_for_stale_book_after"] = a1.LibraryRows.Count(r => r.AudiobookId == staleBook);
            Emit(name, da);

            // --- delete the P-zero Downloads row, record only
            var callsBefore = rig.Gateway.Calls.Count;
            var x0 = await SnapAsync(rig);
            var del = await DeleteDownloadAsync(rig, downloadId, false);
            var x1 = await SnapAsync(rig);
            var xScans = await DrainAndRunScansAsync(rig, run: false);
            var dd = Diff(rig, name + "/delete-row-removeFromClient-false", x0, x1);
            dd["http_status"] = (int)del.StatusCode;
            dd["client_calls_during_step"] = rig.Gateway.Calls.Count - callsBefore;
            dd["scans_enqueued_by_step"] = xScans.Count;
            Emit(name, dd);

            // --- orphans: the Completed job outlives the row (its CompletedAt is 30 days old, as the real jobs are)
            var cleanupProcessor = rig.Handle.Factory.Services.GetRequiredService<IDownloadProcessingJobCleanupProcessor>();
            var y0 = await SnapAsync(rig);
            await cleanupProcessor.RunCycleAsync(CancellationToken.None);
            var y1 = await SnapAsync(rig);
            var dy = Diff(rig, name + "/job-retention-cleanup-after-row-deleted", y0, y1);
            dy["orphan_jobs_before"] = y0.Jobs.Count;
            dy["client_calls_total_whole_case"] = rig.Gateway.Calls.Count;
            Emit(name, dy);
        }

        [Fact]
        public async Task M5_Control_MoveActionRemovesSource_AndJobCleanupKeepsJobsWhileRowExists()
        {
            const string name = "M5-controls";
            var rig = NewRig("m5", "remove");
            using var _r = rig;
            var book = await AddBookAsync(rig, BookZeroTitle, BookZeroAsin, BookZeroAuthor);
            var source = Path.Join(rig.Dl, "move-release");
            var files = Enumerable.Range(1, 2).Select(i => Path.Join(source, $"Part {i:00}.mp3")).ToList();
            for (var i = 0; i < files.Count; i++) MakeAudio(files[i], 500 + (i * 50), $"M {i + 1}");
            var downloadId = await AddStrandedDownloadAsync(rig, book, BookZeroTitle, source, 2);

            // control for "job cleanup deletes jobs only once the row is gone": the row still exists, the 30-day-old job must survive
            var cleanupProcessor = rig.Handle.Factory.Services.GetRequiredService<IDownloadProcessingJobCleanupProcessor>();
            var j0 = await SnapAsync(rig);
            await cleanupProcessor.RunCycleAsync(CancellationToken.None);
            var j1 = await SnapAsync(rig);
            var dj = Diff(rig, name + "/job-cleanup-while-downloads-row-exists", j0, j1);
            dj["jobs_before"] = j0.Jobs.Count;
            dj["jobs_after_count"] = j1.Jobs.Count;
            Emit(name, dj);

            // control for "Copy leaves the source alone": the same API with action=move must remove source files, proving the harness would see a deletion
            var m0 = await SnapAsync(rig);
            var (status, body) = await ManualImportAsync(rig, source, book, files, "move");
            var m1 = await SnapAsync(rig);
            await DrainAndRunScansAsync(rig, run: true);
            var dm = Diff(rig, name + "/manual-import-MOVE-control", m0, m1);
            dm["http_status"] = status;
            dm["response"] = Summarise(body);
            dm["download_row_id"] = downloadId[..8];
            Emit(name, dm);
        }

        [Fact]
        public async Task M4_OrphanCleanupDoesNotTouchImportPending()
        {
            const string name = "M4-orphan-cleanup";
            var (rig, _, downloadId, _) = await BuildPPresentAsync("m4", "remove");
            using var _r = rig;
            // control: a Downloading row whose client item is not in the live queue IS removed by the same service
            string controlId;
            using (var scope = rig.Handle.Factory.Services.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IDownloadRepository>();
                var other = await AddBookAsync(rig, BookStaleTitle, BookStaleAsin, BookStaleAuthor);
                var control = new Download
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = "control-orphan",
                    AudiobookId = other,
                    DownloadClientId = rig.ClientId,
                    Status = DownloadStatus.Downloading,
                    StartedAt = DateTime.UtcNow.AddHours(-2),
                    DownloadPath = rig.Dl,
                    FinalPath = string.Empty,
                    Metadata = new Dictionary<string, object> { ["TorrentHash"] = "ffff" },
                    ImportBlockMessages = []
                };
                await repo.AddAsync(control);
                controlId = control.Id;
            }

            var before = await SnapAsync(rig);
            using (var scope = rig.Handle.Factory.Services.CreateScope())
            {
                var svc = scope.ServiceProvider.GetRequiredService<DownloadOrphanCleanupService>();
                var repo = scope.ServiceProvider.GetRequiredService<IDownloadRepository>();
                var client = (await scope.ServiceProvider.GetRequiredService<IConfigurationService>().GetDownloadClientConfigurationAsync(rig.ClientId))!;
                var tracked = (await repo.GetByClientAsync(rig.ClientId));
                var live = new List<QueueItem> { new() { Id = "some-other-live-item", Title = "x" } };
                var result = new ClientQueueFetchResult(client, live, false, false, null, null, "Live", DateTimeOffset.UtcNow);
                await svc.RemoveOrphansAsync(client, result, [], tracked);
            }

            var after = await SnapAsync(rig);
            var jobsBeforeCleanup = after.Jobs.Count;
            await rig.Handle.Factory.Services.GetRequiredService<IDownloadProcessingJobCleanupProcessor>().RunCycleAsync(CancellationToken.None);
            var afterJobCleanup = await SnapAsync(rig);
            var d = Diff(rig, name, before, afterJobCleanup);
            d["jobs_before_job_cleanup"] = jobsBeforeCleanup;
            d["import_pending_row_id"] = downloadId[..8];
            d["control_downloading_row_id"] = controlId[..8];
            Emit(name, d);
        }
    }
}
