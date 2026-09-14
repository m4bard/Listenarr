using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

[Trait("Name", "StartupDbNormalizerTests")]
[Trait("Category", "Infrastructure")]
public sealed class StartupDbNormalizerTests : BaseTests
{
    // The canonicalization pass joins Audiobooks to AuthorCacheEntries on AuthorNameNormalized.
    // Rows written before that column's normalizer was unified hold keys the current reader never
    // produces, so running the join first would silently skip exactly the drifted rows it is
    // looking for. The order is load-bearing, so it is asserted rather than assumed.
    [Fact]
    public async Task AuthorKeyRederivation_RunsBeforeTheCanonicalizationThatJoinsOnIt()
    {
        var calls = new List<string>();
        var finished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Mock<IAudiobookRepository>(MockBehavior.Strict);
        repository.Setup(service => service.NormalizeJsonColumnsAsync(
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls.Add(nameof(IAudiobookRepository.NormalizeJsonColumnsAsync));
                return Task.CompletedTask;
            });
        repository.Setup(service => service.RederiveAuthorNameKeysAsync(
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls.Add(nameof(IAudiobookRepository.RederiveAuthorNameKeysAsync));
                return Task.FromResult(new AuthorNameKeyRederivationResult(0, 0, 0));
            });
        repository.Setup(service => service.CanonicalizeStoredAuthorNamesAsync(
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                calls.Add(nameof(IAudiobookRepository.CanonicalizeStoredAuthorNamesAsync));
                finished.TrySetResult();
                return Task.FromResult(0);
            });
        using var provider = new ServiceCollection()
            .AddScoped(_ => repository.Object)
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        var readiness = new LibraryFilesystemReadiness();
        readiness.MarkReady();
        var service = new StartupDbNormalizer(
            provider,
            readiness,
            NullLogger<StartupDbNormalizer>.Instance);

        await service.StartAsync(CancellationToken.None);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(
            [
                nameof(IAudiobookRepository.NormalizeJsonColumnsAsync),
                nameof(IAudiobookRepository.RederiveAuthorNameKeysAsync),
                nameof(IAudiobookRepository.CanonicalizeStoredAuthorNamesAsync)
            ],
            calls);
    }

    [Fact]
    public async Task FilesystemInitializationFailure_ReleasesUnrelatedJsonNormalization()
    {
        var normalized = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Mock<IAudiobookRepository>(MockBehavior.Strict);
        repository.Setup(service => service.NormalizeJsonColumnsAsync(
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                normalized.TrySetResult();
                return Task.CompletedTask;
            });
        repository.Setup(service => service.RederiveAuthorNameKeysAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthorNameKeyRederivationResult(0, 0, 0));
        repository.Setup(service => service.CanonicalizeStoredAuthorNamesAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        using var provider = new ServiceCollection()
            .AddScoped(_ => repository.Object)
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        var readiness = new LibraryFilesystemReadiness();
        readiness.MarkRunning("LibraryDirectoryOwnership");
        var service = new StartupDbNormalizer(
            provider,
            readiness,
            NullLogger<StartupDbNormalizer>.Instance);

        await service.StartAsync(CancellationToken.None);

        repository.Verify(
            candidate => candidate.NormalizeJsonColumnsAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        readiness.MarkFailed(
            "filesystem_initialization_failed",
            "Injected filesystem failure.",
            "LibraryDirectoryOwnership");
        await normalized.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        repository.Verify(
            candidate => candidate.NormalizeJsonColumnsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
