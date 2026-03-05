using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Listenarr.Api.Hubs;
using Listenarr.Infrastructure.Models;
using System.IO;

namespace Listenarr.Api.Services
{
    public class UnmatchedScanBackgroundService : BackgroundService
    {
        private static readonly string[] AudioExtensions = { ".m4b", ".mp3", ".flac", ".ogg", ".opus", ".m4a", ".aac", ".wav" };

        private readonly IUnmatchedScanQueueService _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UnmatchedScanBackgroundService> _logger;
        private readonly IHubContext<SettingsHub> _hubContext;

        public UnmatchedScanBackgroundService(
            IUnmatchedScanQueueService queue,
            IServiceScopeFactory scopeFactory,
            ILogger<UnmatchedScanBackgroundService> logger,
            IHubContext<SettingsHub> hubContext)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("UnmatchedScanBackgroundService started");
            if (_queue is not UnmatchedScanQueueService sq) return;

            await foreach (var job in sq.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    _logger.LogInformation("Processing unmatched scan job {JobId} for {Path}", job.Id, job.RootFolderPath);
                    _queue.UpdateJob(job.Id, "Processing");

                    var results = await ScanAsync(job.RootFolderPath, stoppingToken);

                    _queue.UpdateJob(job.Id, "Completed", results);
                    _logger.LogInformation("Unmatched scan job {JobId} completed: {Count} unmatched items", job.Id, results.Count);

                    await _hubContext.Clients.All.SendAsync(
                        "UnmatchedScanComplete",
                        new { jobId = job.Id.ToString(), count = results.Count },
                        stoppingToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unmatched scan job {JobId} failed", job.Id);
                    _queue.UpdateJob(job.Id, "Failed", error: ex.Message);

                    await _hubContext.Clients.All.SendAsync(
                        "UnmatchedScanComplete",
                        new { jobId = job.Id.ToString(), count = 0, error = ex.Message },
                        stoppingToken);
                }
            }
        }

        private async Task<List<UnmatchedFileResult>> ScanAsync(string rootFolderPath, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();

            // Load all tracked file paths (normalized) from DB
            var trackedPaths = await db.AudiobookFiles
                .Where(f => f.Path != null)
                .Select(f => f.Path!)
                .ToListAsync(ct);

            var trackedNormalized = new HashSet<string>(
                trackedPaths.Select(NormalizePath),
                StringComparer.OrdinalIgnoreCase);

            // Walk the root folder tree
            var candidates = CollectAudioFiles(rootFolderPath);

            // Filter to untracked files
            var unmatched = candidates
                .Where(f => !trackedNormalized.Contains(NormalizePath(f)))
                .ToList();

            // Group by parent folder (each folder = one audiobook)
            var grouped = unmatched.GroupBy(f => Path.GetDirectoryName(f) ?? rootFolderPath);

            var results = new List<UnmatchedFileResult>();
            foreach (var group in grouped)
            {
                var files = group.ToList();
                var representative = files.OrderBy(f => f).First();
                var parsed = PathMetadataParser.Parse(representative, rootFolderPath);

                // Relative path from root (use book folder)
                var bookFolder = group.Key;
                var relativeFolder = bookFolder.Length > rootFolderPath.Length
                    ? bookFolder[(rootFolderPath.Length)..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : bookFolder;

                var totalSize = files.Sum(f =>
                {
                    try { return new FileInfo(f).Length; } catch { return 0L; }
                });

                results.Add(new UnmatchedFileResult
                {
                    FullPath = representative,
                    RelativePath = relativeFolder,
                    BookFolder = bookFolder,
                    Size = totalSize,
                    FileCount = files.Count,
                    Title = parsed.Title,
                    Author = parsed.Author,
                    Series = parsed.Series,
                    SeriesNumber = parsed.SeriesNumber,
                    Year = parsed.Year,
                    Narrator = parsed.Narrator,
                    Description = parsed.Description,
                    CoverPath = parsed.CoverPath,
                    Format = Path.GetExtension(representative).TrimStart('.').ToUpperInvariant()
                });
            }

            return results.OrderBy(r => r.Author).ThenBy(r => r.Series).ThenBy(r => r.Title).ToList();
        }

        private List<string> CollectAudioFiles(string rootFolderPath)
        {
            var candidates = new List<string>();
            var dirs = new Stack<string>();
            dirs.Push(rootFolderPath);

            while (dirs.Count > 0)
            {
                var dir = dirs.Pop();
                try
                {
                    var normalizedDir = Path.GetFullPath(dir);
                    foreach (var file in Directory.EnumerateFiles(normalizedDir))
                    {
                        try
                        {
                            if (AudioExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                                candidates.Add(file);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                        {
                            _logger.LogDebug(ex, "Skipped file {File} during unmatched scan", file);
                        }
                    }
                    foreach (var sub in Directory.EnumerateDirectories(normalizedDir))
                        dirs.Push(sub);
                }
                catch (IOException ioEx) { _logger.LogWarning(ioEx, "IO error scanning {Dir}", dir); }
                catch (UnauthorizedAccessException uaEx) { _logger.LogWarning(uaEx, "Access denied scanning {Dir}", dir); }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogWarning(ex, "Unexpected error scanning {Dir}", dir);
                }
            }

            return candidates;
        }

        private static string NormalizePath(string path) =>
            Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
    }
}
