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
        private readonly IFfmpegService _ffmpegService;

        public UnmatchedScanBackgroundService(
            IUnmatchedScanQueueService queue,
            IServiceScopeFactory scopeFactory,
            ILogger<UnmatchedScanBackgroundService> logger,
            IHubContext<SettingsHub> hubContext,
            IFfmpegService ffmpegService)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _hubContext = hubContext;
            _ffmpegService = ffmpegService;
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
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var appSettings = await configService.GetApplicationSettingsAsync();
            var concurrency = Math.Clamp(appSettings?.UnmatchedScanConcurrency ?? 2, 1, 8);

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

            // Resolve ffprobe path once for the whole scan (null = not available)
            var ffprobePath = await _ffmpegService.GetFfprobePathAsync();

            var groupList = grouped.ToList();
            var results = new System.Collections.Concurrent.ConcurrentBag<UnmatchedFileResult>();

            // Parallel.ForEachAsync only allocates active slots — avoids creating all tasks upfront
            await Parallel.ForEachAsync(groupList,
                new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = ct },
                async (group, token) =>
                {
                    var files = group.ToList();
                    var representative = files.OrderBy(f => f).First();
                    var parsed = PathMetadataParser.Parse(representative, rootFolderPath);

                    if (!string.IsNullOrEmpty(ffprobePath))
                    {
                        var tags = await PathMetadataParser.ReadEmbeddedTagsAsync(representative, ffprobePath, token);
                        if (!string.IsNullOrEmpty(tags.Title))        parsed.Title = tags.Title;
                        if (!string.IsNullOrEmpty(tags.Author))       parsed.Author = tags.Author;
                        if (!string.IsNullOrEmpty(tags.Narrator))     parsed.Narrator = tags.Narrator;
                        if (!string.IsNullOrEmpty(tags.Series))       parsed.Series = tags.Series;
                        if (!string.IsNullOrEmpty(tags.SeriesNumber)) parsed.SeriesNumber = tags.SeriesNumber;
                        if (!string.IsNullOrEmpty(tags.Year))         parsed.Year = tags.Year;
                        if (!string.IsNullOrEmpty(tags.Description))  parsed.Description = tags.Description;
                        if (!string.IsNullOrEmpty(tags.Asin))         parsed.Asin = tags.Asin;
                    }

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
                        Asin = parsed.Asin,
                        Format = Path.GetExtension(representative).TrimStart('.').ToUpperInvariant()
                    });
                });

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
