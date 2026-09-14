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
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Audiobooks.Catalog;

[Trait("Area", "Library")]
[Trait("Name", "LibraryAddServiceAuthorCanonicalizationTests")]
[Trait("Category", "Application")]
public sealed class LibraryAddServiceAuthorCanonicalizationTests : BaseTests
{
    private static readonly HttpClient SharedHttpClient = new();
    private readonly Mock<AudibleService> audibleMock =
        new(SharedHttpClient, Mock.Of<ILogger<AudibleService>>()) { CallBase = false };
    private readonly Mock<IImageCacheService> imageCacheMock = new();
    private readonly Mock<ILibraryDestinationMutationGuard> destinationGuardMock = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        destinationGuardMock
            .Setup(guard => guard.GetBlockingReasonAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        Init(services => services
            .WithSingleton(audibleMock.Object)
            .WithSingleton(imageCacheMock.Object)
            .WithSingleton(destinationGuardMock.Object));

        await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
            .WithFolderNamingPattern("{Author}")
            .WithFileNamingPattern("{Title}")
            .Build());
        await _rootFolderRepository.AddAsync(new RootFolderBuilder()
            .WithIsDefault()
            .WithPath(FileService.GetTempDirectory("listenarr-canonicalization"))
            .Build());
    }

    private async Task<Audiobook> AddAsync(string title, params string[] authors)
    {
        var result = await _provider.GetRequiredService<ILibraryAddService>().AddToLibraryAsync(
            new LibraryAddOperationRequest
            {
                Metadata = new AudibleBookMetadata
                {
                    Title = title,
                    Authors = [.. authors]
                },
                Monitored = true
            },
            CancellationToken.None);

        Assert.True(result.Added, result.Message);
        Assert.NotNull(result.Audiobook);
        return (await _audiobookRepository.GetByIdAsync(result.Audiobook.Id))!;
    }

    // The provider's own spelling came back with the ASIN and was discarded two lines before the
    // ASIN was stored. Adopt it where the two differ only in ways the normalizer already erases.
    [Theory]
    [InlineData("Andy  Weir", "Andy Weir")]
    [InlineData("andy weir", "Andy Weir")]
    [InlineData("J. N. Chaney", "J.N. Chaney")]
    [InlineData("Émile Zola", "Emile Zola")]
    public async Task AddToLibrary_SpellingVariant_AdoptsTheProviderName(
        string stored,
        string provider)
    {
        audibleMock
            .Setup(service => service.LookupAuthorAsync(stored, "us"))
            .ReturnsAsync(new AuthorLookupItem { Asin = "B000TESTAA", Name = provider });

        var saved = await AddAsync("A Spelling Variant", stored);

        Assert.Equal([provider], saved.Authors);
        Assert.Equal(["B000TESTAA"], saved.AuthorAsins);
    }

    // The control for the theory above. The remote matcher accepts a bare substring in either
    // direction, so it resolves this to the right contributor and hands back a shorter name. The
    // credit is not ours to rewrite: either test alone passes on an implementation that rewrites
    // everything or one that rewrites nothing.
    [Theory]
    [InlineData("Sir Arthur Conan Doyle", "Arthur Conan Doyle")]
    [InlineData("Andy Weir, PhD", "Andy Weir")]
    [InlineData("Jane Austen (Author)", "Jane Austen")]
    public async Task AddToLibrary_ContainmentOnlyMatch_KeepsTheStoredCreditAndStillStoresTheAsin(
        string stored,
        string provider)
    {
        audibleMock
            .Setup(service => service.LookupAuthorAsync(stored, "us"))
            .ReturnsAsync(new AuthorLookupItem { Asin = "B000TESTBB", Name = provider });

        var saved = await AddAsync("A Containment Match", stored);

        Assert.Equal([stored], saved.Authors);
        Assert.Equal(["B000TESTBB"], saved.AuthorAsins);
    }

    [Fact]
    public async Task AddToLibrary_FailedLookup_ChangesNothing()
    {
        audibleMock
            .Setup(service => service.LookupAuthorAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((AuthorLookupItem?)null);

        var saved = await AddAsync("An Unresolved Author", "Andy  Weir");

        Assert.Equal(["Andy  Weir"], saved.Authors);
        Assert.Empty(saved.AuthorAsins ?? []);
    }

    [Fact]
    public async Task AddToLibrary_ThrowingLookup_StillAddsAndLeavesTheNameAlone()
    {
        audibleMock
            .Setup(service => service.LookupAuthorAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("Audible is unreachable."));

        var saved = await AddAsync("A Failing Lookup", "Andy  Weir");

        Assert.Equal(["Andy  Weir"], saved.Authors);
        Assert.Empty(saved.AuthorAsins ?? []);
    }

    // AuthorAsins is a deduplicated set of successful lookups, not a positional partner to
    // Authors. This is the test that fails on any implementation that assumed the two lists line
    // up index for index.
    [Fact]
    public async Task AddToLibrary_PartialResolution_CorrectsOnlyTheAuthorThatResolved()
    {
        audibleMock
            .Setup(service => service.LookupAuthorAsync("Unknown Person", "us"))
            .ReturnsAsync((AuthorLookupItem?)null);
        audibleMock
            .Setup(service => service.LookupAuthorAsync("Andy  Weir", "us"))
            .ReturnsAsync(new AuthorLookupItem { Asin = "B000TESTCC", Name = "Andy Weir" });

        var saved = await AddAsync("A Partial Resolution", "Unknown Person", "Andy  Weir");

        Assert.Equal(["Unknown Person", "Andy Weir"], saved.Authors);
        Assert.Equal(["B000TESTCC"], saved.AuthorAsins);
    }

    // A lookup that resolves to somebody else entirely must not rename the credit, even though
    // this is the one case where the remote matcher genuinely got it wrong.
    [Fact]
    public async Task AddToLibrary_DifferentPersonSharingInitials_KeepsTheStoredCredit()
    {
        audibleMock
            .Setup(service => service.LookupAuthorAsync("J. N. Chaney", "us"))
            .ReturnsAsync(new AuthorLookupItem { Asin = "B000TESTDD", Name = "J. N. Smith" });

        var saved = await AddAsync("A Wrong Match", "J. N. Chaney");

        Assert.Equal(["J. N. Chaney"], saved.Authors);
        Assert.Equal(["B000TESTDD"], saved.AuthorAsins);
    }
}
