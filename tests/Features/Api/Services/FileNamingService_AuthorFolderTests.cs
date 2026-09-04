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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Services;

/// <summary>
/// The author string used for naming is per-release: it is whatever the metadata source
/// returned for that one book. Sources punctuate initials inconsistently, so one author can
/// end up owning several top-level folders. These cover which spellings the naming layer now
/// reconciles and, just as importantly, which it still cannot.
/// </summary>
[Trait("Area", "FileNaming")]
[Trait("Name", "FileNamingService_AuthorFolderTests")]
[Trait("Category", "FileNaming")]
public sealed class FileNamingService_AuthorFolderTests : BaseTests
{
    private const string FolderPattern = "{Author}/{Series}/{Title}";

    private static FileNamingService CreateService(ApplicationSettings? settings = null)
    {
        var configService = new Mock<IConfigurationService>();
        configService
            .Setup(service => service.GetApplicationSettingsAsync())
            .ReturnsAsync(settings ?? new ApplicationSettings());
        return new FileNamingService(configService.Object, new Mock<ILogger<FileNamingService>>().Object);
    }

    private static string AuthorFolderOf(string renderedPath)
    {
        return renderedPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)[0];
    }

    [Theory]
    [InlineData("J.M. Barrie")]
    [InlineData("J. M. Barrie")]
    [InlineData("J M Barrie")]
    [InlineData("J.M.Barrie")]
    public void ApplyNamingPattern_SpellsInitialsOneWay(string authorFromRelease)
    {
        var variables = new Dictionary<string, object>
        {
            { "Author", authorFromRelease },
            { "Series", string.Empty },
            { "Title", "Peter and Wendy" }
        };

        var rendered = CreateService().ApplyNamingPattern(FolderPattern, variables);

        Assert.Equal("J. M. Barrie", AuthorFolderOf(rendered));
    }

    [Fact]
    public void ApplyNamingPattern_TwoSpacingsOfOneAuthorShareAFolder()
    {
        var service = CreateService();

        var fromFirstRelease = service.ApplyNamingPattern(
            FolderPattern,
            new Dictionary<string, object> { { "Author", "J.M. Barrie" }, { "Title", "Peter and Wendy" } });
        var fromSecondRelease = service.ApplyNamingPattern(
            FolderPattern,
            new Dictionary<string, object> { { "Author", "J. M. Barrie" }, { "Title", "Peter Pan in Kensington Gardens" } });

        Assert.Equal(AuthorFolderOf(fromFirstRelease), AuthorFolderOf(fromSecondRelease));
    }

    [Theory]
    [InlineData("Homer")]
    [InlineData("Fyodor Dostoevsky")]
    [InlineData("St. John Rivers")]
    [InlineData("Malcolm X")]
    public void ApplyNamingPattern_LeavesNamesWithoutInitialsAlone(string author)
    {
        var variables = new Dictionary<string, object>
        {
            { "Author", author },
            { "Title", "A Book" }
        };

        var rendered = CreateService().ApplyNamingPattern(FolderPattern, variables);

        Assert.Equal(author, AuthorFolderOf(rendered));
    }

    [Fact]
    public void ApplyNamingPattern_AudibleMetadataUsesTheSameSpelling()
    {
        var metadata = new AudibleBookMetadata
        {
            Title = "Peter and Wendy",
            Authors = ["J.M. Barrie"]
        };

        var rendered = CreateService().ApplyNamingPattern(FolderPattern, metadata);

        Assert.Equal("J. M. Barrie", AuthorFolderOf(rendered));
    }

    [Fact]
    public async Task GenerateFilePathAsync_UsesTheSameSpellingEndToEnd()
    {
        var outputPath = Path.Join(Path.GetTempPath(), "ListenarrAuthorFolder");
        var settings = new ApplicationSettings
        {
            OutputPath = outputPath,
            FolderNamingPattern = FolderPattern,
            FileNamingPattern = "{Title}"
        };
        var metadata = new AudioMetadata
        {
            Title = "Peter and Wendy",
            Artist = "J.M. Barrie"
        };

        var rendered = await CreateService(settings).GenerateFilePathAsync(metadata, outputPath, ".m4b");

        Assert.Equal(
            Path.Join(outputPath, "J. M. Barrie", "Peter and Wendy", "Peter and Wendy.m4b"),
            rendered);
    }

    [Fact]
    public void ApplyNamingPattern_KeepsATranslatorOutOfThePath()
    {
        // A second name on a book is often a translator rather than a co-author, so nothing here
        // may join the list together. Only the byline's first name reaches the folder.
        var metadata = new AudibleBookMetadata
        {
            Title = "The Odyssey",
            Authors = ["Homer", "Samuel Butler - translator"]
        };

        var rendered = CreateService().ApplyNamingPattern(FolderPattern, metadata);

        Assert.Equal("Homer", AuthorFolderOf(rendered));
        Assert.DoesNotContain("translator", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Samuel Butler", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyNamingPattern_StillCannotReconcileAnInitialWithASpelledOutName()
    {
        // Documents a limit rather than an intention. Reconciling these two means either
        // shortening "James" or inventing it, and the naming layer has no author record to
        // decide from. Delete this once one exists.
        var service = CreateService();

        var spelledOut = service.ApplyNamingPattern(
            FolderPattern,
            new Dictionary<string, object> { { "Author", "James M. Barrie" }, { "Title", "Peter Pan in Kensington Gardens" } });
        var initialled = service.ApplyNamingPattern(
            FolderPattern,
            new Dictionary<string, object> { { "Author", "J.M. Barrie" }, { "Title", "Peter and Wendy" } });

        Assert.NotEqual(AuthorFolderOf(spelledOut), AuthorFolderOf(initialled));
    }

    [Fact]
    public void ApplyNamingPattern_StillSplitsOnBylineOrder()
    {
        // The other half of the defect, also unfixed. Choosing between two names on a byline
        // needs to know which of them the book belongs to, and nothing available here does.
        var service = CreateService();

        var oneOrder = service.ApplyNamingPattern(
            FolderPattern,
            new AudibleBookMetadata { Title = "The Odyssey", Authors = ["Homer", "Samuel Butler - translator"] });
        var otherOrder = service.ApplyNamingPattern(
            FolderPattern,
            new AudibleBookMetadata { Title = "The Odyssey", Authors = ["Samuel Butler - translator", "Homer"] });

        Assert.NotEqual(AuthorFolderOf(oneOrder), AuthorFolderOf(otherOrder));
    }
}
