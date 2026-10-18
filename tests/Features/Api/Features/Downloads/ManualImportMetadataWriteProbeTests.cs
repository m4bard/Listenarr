using Listenarr.Api.Dtos.ManualImport;
using Listenarr.Infrastructure.Library.Files;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Listenarr.Tests.Features.Api.Features.Downloads;

// PROBE ONLY (not for upstream). Drives the real manual-import controller, the real FileMover
// and lease, and the real TagLib writer behind the IMetadataService seam, then records what
// landed in the file and what an idempotent retry does. Results go to PROBE_OUT as JSON lines.
[Trait("Name", "ManualImportMetadataWriteProbeTests")]
[Trait("Category", "Api")]
public sealed class ManualImportMetadataWriteProbeTests : BaseTests
{
    private readonly Mock<IMetadataService> _metadata = new();
    private readonly List<string> _writerLog = new();

    public ManualImportMetadataWriteProbeTests()
    {
        _metadata.Setup(service => service.ExtractFileMetadataAsync(It.IsAny<string>()))
            .ReturnsAsync(new AudioMetadata { Title = "Probe", Format = "m4b", BitRate = 64000 });
        _metadata.Setup(service => service.ExtractFileMetadataAsync(It.IsAny<MetadataFileSource>()))
            .ReturnsAsync(new AudioMetadata { Format = "m4b", Duration = TimeSpan.FromSeconds(2) });
        var writerLogger = new Mock<ILogger<TagLibAudioTagWriter>>();
        writerLogger.Setup(logger => logger.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        writerLogger.Setup(logger => logger.Log(
                It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()))
            .Callback(new InvocationAction(invocation =>
                _writerLog.Add($"{invocation.Arguments[0]}: {invocation.Arguments[2]}"
                    + (invocation.Arguments[3] is Exception exception
                        ? $" | {exception.GetType().Name}: {exception.Message}" : ""))));
        var writer = new TagLibAudioTagWriter(writerLogger.Object);
        _metadata.Setup(service => service.WriteAsinTagAsync(
                It.IsAny<IAudiobookFileRegistrationLease>(), It.IsAny<string>()))
            .Returns((IAudiobookFileRegistrationLease lease, string asin) =>
                writer.WriteAsinTagAsync(lease, asin));
        Init(builder => builder.WithSingleton(_metadata.Object));
    }

    [Theory]
    [InlineData(FileAction.Move)]
    [InlineData(FileAction.Copy)]
    [InlineData(FileAction.HardlinkCopy)]
    public async Task Probe_ManualImportAsinEmbedAndRetry(FileAction action)
    {
        var fixture = Environment.GetEnvironmentVariable("PROBE_M4B");
        var output = Environment.GetEnvironmentVariable("PROBE_OUT");
        var variant = Environment.GetEnvironmentVariable("PROBE_VARIANT") ?? "unset";
        if (string.IsNullOrWhiteSpace(fixture) || string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        var outputRoot = FileService.GetTempDirectory("probe-asin-output");
        var sourceRoot = FileService.GetTempDirectory("probe-asin-source");
        var source = Path.Join(sourceRoot, "incoming.m4b");
        File.Copy(fixture, source);
        var sourceSha = Sha(source);
        await AddAuthorizedRootAsync(outputRoot);
        var settings = await _applicationSettingsRepository.GetAsync() ?? new ApplicationSettings();
        settings.OutputPath = outputRoot;
        settings.FolderNamingPattern = "";
        settings.FileNamingPattern = "{Title}";
        settings.EnableMetadataProcessing = false;
        await _applicationSettingsRepository.SaveAsync(settings);
        var book = await _audiobookRepository.AddAsync(new Listenarr.Tests.Builders.AudiobookBuilder()
            .WithTitle("Probe Book").WithBasePath(outputRoot).WithAsin("B0PROBE001").Build());
        var request = new ManualImportRequestDto
        {
            Path = sourceRoot,
            Mode = "interactive",
            Action = action,
            Items = [new ManualImportItemDto { FullPath = source, MatchedAudiobookId = book.Id }]
        };

        var first = await StartAsync(request);
        var tracked = await _audiobookFileRepository.GetByAudiobookIdAsync(book.Id);
        var destination = tracked.FirstOrDefault()?.Path;
        var record = new Dictionary<string, object?>
        {
            ["variant"] = variant,
            ["action"] = action.ToString(),
            ["first"] = first,
            ["writerCalls"] = _metadata.Invocations.Count(invocation =>
                invocation.Method.Name == nameof(IMetadataService.WriteAsinTagAsync)),
            ["writerLog"] = _writerLog.ToList(),
            ["destinationAsin"] = destination == null ? null : ReadAsin(destination),
            ["destinationChanged"] = destination == null ? null : Sha(destination) != sourceSha,
            ["sourceExists"] = File.Exists(source),
            ["sourceAsin"] = File.Exists(source) ? ReadAsin(source) : null,
            ["journalsAfterFirst"] = await JournalsAsync(destination),
            ["trackedAfterFirst"] = tracked.Count
        };

        var second = await StartAsync(request);
        var trackedAfterRetry = await _audiobookFileRepository.GetByAudiobookIdAsync(book.Id);
        record["retry"] = second;
        record["trackedAfterRetry"] = trackedAfterRetry.Count;
        record["m4bFilesInOutputAfterRetry"] = Directory
            .GetFiles(outputRoot, "*.m4b", SearchOption.AllDirectories)
            .Select(Path.GetFileName).ToList();
        record["journalsAfterRetry"] = await JournalsAsync(destination);
        record["destinationAsinAfterRetry"] = destination == null || !File.Exists(destination)
            ? null : ReadAsin(destination);

        await File.AppendAllTextAsync(output,
            System.Text.Json.JsonSerializer.Serialize(record) + Environment.NewLine);
    }

    // Reader control: a hand-stamped copy must read as tagged and the fixture as untagged,
    // otherwise "no ASIN" in the import probe proves nothing. Also records what the canary
    // path-based writer (no lease) does to the same file.
    [Fact]
    public async Task Probe_ReaderAndPathWriterControls()
    {
        var fixture = Environment.GetEnvironmentVariable("PROBE_M4B");
        var output = Environment.GetEnvironmentVariable("PROBE_OUT");
        if (string.IsNullOrWhiteSpace(fixture) || string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        var root = FileService.GetTempDirectory("probe-asin-controls");
        var stamped = Path.Join(root, "stamped.m4b");
        var pathWritten = Path.Join(root, "path-written.m4b");
        File.Copy(fixture, stamped);
        File.Copy(fixture, pathWritten);
        using (var file = TagLib.File.Create(stamped))
        {
            ((TagLib.Mpeg4.AppleTag)file.GetTag(TagLib.TagTypes.Apple, true))
                .SetDashBox("com.apple.iTunes", "ASIN", "B0STAMP001");
            file.Save();
        }
        var pathWrittenSha = Sha(pathWritten);
        await new TagLibAudioTagWriter(Mock.Of<ILogger<TagLibAudioTagWriter>>())
            .WriteAsinTagAsync(pathWritten, "B0PATH0001");
        await File.AppendAllTextAsync(output, System.Text.Json.JsonSerializer.Serialize(new
        {
            variant = Environment.GetEnvironmentVariable("PROBE_VARIANT") ?? "unset",
            control = "reader",
            fixtureAsin = ReadAsin(fixture),
            stampedAsin = ReadAsin(stamped),
            pathWriterAsin = ReadAsin(pathWritten),
            pathWriterChangedBytes = Sha(pathWritten) != pathWrittenSha
        }) + Environment.NewLine);
    }

    private async Task<object> StartAsync(ManualImportRequestDto request)
    {
        var result = (await ActivatorUtilities.CreateInstance<ManualImportController>(_provider)
            .Start(request)).Result;
        if (result is not Microsoft.AspNetCore.Mvc.ObjectResult objectResult)
        {
            return result?.GetType().Name ?? "null";
        }
        var payload = System.Text.Json.JsonSerializer.SerializeToElement(objectResult.Value);
        var first = payload.TryGetProperty("results", out var results) && results.GetArrayLength() > 0
            ? results[0] : default;
        string? Property(string name) => first.ValueKind == System.Text.Json.JsonValueKind.Object
            && first.TryGetProperty(name, out var value) && value.ValueKind != System.Text.Json.JsonValueKind.Null
                ? value.ToString() : null;
        return new
        {
            status = objectResult.StatusCode,
            importedCount = payload.TryGetProperty("importedCount", out var count) ? count.GetInt32() : -1,
            success = Property("Success"),
            error = Property("Error"),
            warningCode = Property("WarningCode"),
            warning = Property("Warning"),
            destination = Property("DestinationPath") is { } path ? Path.GetFileName(path) : null,
            effectiveAction = Property("EffectiveAction")
        };
    }

    private async Task<List<string>> JournalsAsync(string? destination)
    {
        var factory = _provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var journals = await db.FileMutationJournals.AsNoTracking().ToListAsync();
        var destinationSha = destination != null && File.Exists(destination) ? Sha(destination) : null;
        return journals.Select(journal =>
            $"{journal.Action} {journal.State} len={journal.SourceLength} "
            + $"proofMatchesDestination={string.Equals(journal.SourceSha256, destinationSha, StringComparison.OrdinalIgnoreCase)} "
            + $"error={journal.Error}").ToList();
    }

    private static string Sha(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string? ReadAsin(string path)
    {
        using var file = TagLib.File.Create(path);
        return file.GetTag(TagLib.TagTypes.Apple, false) is TagLib.Mpeg4.AppleTag apple
            ? apple.GetDashBox("com.apple.iTunes", "ASIN")
            : null;
    }
}
