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

using Listenarr.Api.Dtos.ManualImport;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Api.Features.Downloads;

/// <summary>
/// A manual import whose file registration would refuse on its extension must be refused before
/// anything is written into the library folder (Listenarr#998). Registration used to run after
/// the destination was published, so the refusal left a copy, or on a Move a second hardlink, in
/// the library folder with no catalog row behind it.
/// </summary>
/// <remarks>
/// These run the real controller, file mover and journal store against temp directories; only
/// metadata extraction is stubbed. Every assertion reads the filesystem and the database rather
/// than the reply alone, because the defect was a reply that said "refused" beside a folder that
/// said otherwise.
/// </remarks>
[Trait("Name", "ManualImportRefusedBeforePublicationTests")]
[Trait("Category", "Api")]
public sealed class ManualImportRefusedBeforePublicationTests : BaseTests
{
    private const string BookTitle = "The Refused Import";
    private const string SourceContent = "bytes of a file the registration gate refuses";

    public ManualImportRefusedBeforePublicationTests()
    {
        // Both overloads answer with audio-only content, so the audio controls pass on what the
        // probe reports rather than leaning on the always-audio extension short-circuit.
        var probed = new AudioMetadata
        {
            Title = BookTitle,
            Format = "mp3",
            BitRate = 128000,
            HasAudioStream = true,
            HasVideoStream = false
        };
        var metadata = new Mock<IMetadataService>();
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<string>()))
            .ReturnsAsync(probed);
        metadata.Setup(service => service.ExtractFileMetadataAsync(
                It.IsAny<MetadataFileSource>()))
            .ReturnsAsync(probed);
        Init(builder => builder.WithSingleton(metadata.Object));
    }

    public static TheoryData<FileAction, string> RefusedExtensions => new()
    {
        { FileAction.Move, "incoming.mkv" },
        { FileAction.Copy, "incoming.mkv" },
        { FileAction.Move, "incoming.MKV" },
        { FileAction.Copy, "incoming.txt" },
        { FileAction.HardlinkCopy, "incoming.mkv" },
        // No extension at all. The planner would name these .m4b and registration would then
        // accept them, so before the gate they imported; refusing them is intended.
        { FileAction.Move, "incoming" },
        { FileAction.Copy, "incoming" },
        { FileAction.Move, "incoming." },
        { FileAction.Copy, "incoming." },
    };

    [Theory]
    [MemberData(nameof(RefusedExtensions))]
    public async Task Start_ExtensionRegistrationRefuses_WritesNothingIntoLibraryFolder(
        FileAction action,
        string sourceName)
    {
        var arranged = await ArrangeAsync(sourceName);

        var result = Assert.Single(await StartAsync(
            action,
            arranged.SourceFile,
            arranged.Audiobook.Id));

        Assert.False(result.Success);
        Assert.Empty(Directory.EnumerateFileSystemEntries(
            arranged.OutputRoot,
            "*",
            SearchOption.AllDirectories));
        Assert.Equal(
            "The file is not an audio format Listenarr can import.",
            result.Error);
        Assert.Equal(SourceContent, await File.ReadAllTextAsync(arranged.SourceFile));
        Assert.Empty(await TrackedFilesAsync(arranged.Audiobook.Id));
        Assert.Empty(await JournalsAsync());
    }

    [Theory]
    [InlineData(FileAction.Move)]
    [InlineData(FileAction.Copy)]
    public async Task Start_ExtensionRefused_PreExistingFileAtPlannedDestinationSurvivesUntouched(
        FileAction action)
    {
        var arranged = await ArrangeAsync("incoming.mkv");
        // The name the planner would give the import, holding bytes nobody imported. Before the
        // fix the planner stepped around it to a new name and published there, so a refusal left
        // a second file beside it.
        var preExisting = Path.Join(arranged.OutputRoot, BookTitle + ".mkv");
        const string preExistingContent = "a file that was already in the library folder";
        await File.WriteAllTextAsync(preExisting, preExistingContent);

        var result = Assert.Single(await StartAsync(
            action,
            arranged.SourceFile,
            arranged.Audiobook.Id));

        Assert.False(result.Success);
        Assert.Equal(
            [preExisting],
            Directory.EnumerateFileSystemEntries(
                arranged.OutputRoot,
                "*",
                SearchOption.AllDirectories));
        Assert.Equal(preExistingContent, await File.ReadAllTextAsync(preExisting));
        Assert.Equal(SourceContent, await File.ReadAllTextAsync(arranged.SourceFile));
        Assert.Empty(await TrackedFilesAsync(arranged.Audiobook.Id));
    }

    [Theory]
    [InlineData(FileAction.Move, "incoming.mp3")]
    [InlineData(FileAction.Copy, "incoming.mp3")]
    [InlineData(FileAction.Move, "incoming.M4B")]
    public async Task Start_AudioExtension_StillImportsIntoLibraryFolder(
        FileAction action,
        string sourceName)
    {
        // The control: the same apparatus with an extension registration accepts must publish
        // and register, or an empty library folder above would prove nothing.
        var arranged = await ArrangeAsync(sourceName);

        var result = Assert.Single(await StartAsync(
            action,
            arranged.SourceFile,
            arranged.Audiobook.Id));

        Assert.True(result.Success, result.Error);
        var published = Assert.Single(Directory.EnumerateFiles(
            arranged.OutputRoot,
            "*",
            SearchOption.AllDirectories));
        Assert.Equal(SourceContent, await File.ReadAllTextAsync(published));
        Assert.Equal(action == FileAction.Copy, File.Exists(arranged.SourceFile));
        Assert.Single(await TrackedFilesAsync(arranged.Audiobook.Id));
    }

    private sealed record Arranged(string OutputRoot, string SourceFile, Audiobook Audiobook);

    private async Task<Arranged> ArrangeAsync(string sourceName)
    {
        var outputRoot = FileService.GetTempDirectory("manual-refused-out");
        var sourceRoot = FileService.GetTempDirectory("manual-refused-src");
        var sourceFile = await FileService.GetFileAsync(
            sourceRoot,
            sourceName,
            SourceContent);
        await AddAuthorizedRootAsync(outputRoot);

        var settings = await _applicationSettingsRepository.GetAsync()
            ?? new ApplicationSettings();
        settings.OutputPath = outputRoot;
        settings.FolderNamingPattern = "";
        settings.FileNamingPattern = "{Title}";
        settings.EnableMetadataProcessing = false;
        await _applicationSettingsRepository.SaveAsync(settings);

        var audiobook = await _audiobookRepository.AddAsync(new Audiobook
        {
            Title = BookTitle,
            Authors = ["Author"],
            BasePath = outputRoot
        });
        return new Arranged(outputRoot, sourceFile, audiobook);
    }

    private async Task<IReadOnlyList<ManualImportResultDto>> StartAsync(
        FileAction action,
        string sourceFile,
        int audiobookId)
    {
        var controller = ActivatorUtilities.CreateInstance<ManualImportController>(
            _provider);
        var response = await controller.Start(new ManualImportRequestDto
        {
            Path = Path.GetDirectoryName(sourceFile)!,
            Mode = "interactive",
            Action = action,
            Items =
            [
                new ManualImportItemDto
                {
                    FullPath = sourceFile,
                    MatchedAudiobookId = audiobookId
                }
            ]
        });
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(
            response.Result);
        return Assert.IsAssignableFrom<IEnumerable<ManualImportResultDto>>(
                ok.Value!.GetType().GetProperty("results")!.GetValue(ok.Value))
            .ToList();
    }

    private async Task<List<AudiobookFile>> TrackedFilesAsync(int audiobookId)
    {
        await using var db = await CreateDbContextAsync();
        return await db.AudiobookFiles
            .AsNoTracking()
            .Where(file => file.AudiobookId == audiobookId)
            .ToListAsync();
    }

    private async Task<List<FileMutationJournal>> JournalsAsync()
    {
        await using var db = await CreateDbContextAsync();
        return await db.FileMutationJournals.AsNoTracking().ToListAsync();
    }

    private Task<ListenArrDbContext> CreateDbContextAsync() =>
        _provider
            .GetRequiredService<IDbContextFactory<ListenArrDbContext>>()
            .CreateDbContextAsync();
}
