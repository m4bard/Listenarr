using System.Reflection;
using Listenarr.Api.Attributes;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Tests.Features.Api.Features.Library;

[Trait("Area", "Library")]
[Trait("Name", "FileRegistrationRecoveryControllerTests")]
[Trait("Category", "Api")]
public sealed class FileRegistrationRecoveryControllerTests : BaseTests
{
    [Theory]
    [InlineData(FileMutationJournalState.CompletedSourceRetained, FileRegistrationRecoveryDisposition.Cleared, 200)]
    [InlineData(FileMutationJournalState.RegistrationCommitted, FileRegistrationRecoveryDisposition.WaitingForOwnerRetry, 409)]
    [InlineData(FileMutationJournalState.NeedsAttention, FileRegistrationRecoveryDisposition.RequiresOperatorAttention, 409)]
    public async Task Abandon_ReturnsOkOnlyWhenTheBoundaryIsClear(
        FileMutationJournalState state,
        FileRegistrationRecoveryDisposition disposition,
        int expectedStatusCode)
    {
        var operationId = Guid.NewGuid();
        var status = new FileRegistrationRecoveryStatus(
            operationId, state, 7, disposition, CanRetry: false, CanAbandon: false, "reason");
        var service = new Mock<IFileRegistrationRecoveryService>(MockBehavior.Strict);
        service.Setup(candidate => candidate.AbandonAsync(operationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
        var coordinator = CreateCoordinator();

        var result = await CreateController(service, coordinator).Abandon(operationId, CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(expectedStatusCode, objectResult.StatusCode);
        Assert.Same(status, objectResult.Value);
        service.Verify(candidate => candidate.AbandonAsync(operationId, It.IsAny<CancellationToken>()), Times.Once);
        coordinator.Verify(candidate => candidate.ExecuteExclusiveAsync(
            It.IsAny<Func<CancellationToken, Task<FileRegistrationRecoveryStatus>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Abandon_MapsServiceRefusalsToProblemCodes()
    {
        var notFound = Guid.NewGuid();
        var invalid = Guid.Empty;
        var foreign = Guid.NewGuid();
        var service = new Mock<IFileRegistrationRecoveryService>(MockBehavior.Strict);
        service.Setup(candidate => candidate.AbandonAsync(notFound, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        service.Setup(candidate => candidate.AbandonAsync(invalid, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("invalid", "operationId"));
        service.Setup(candidate => candidate.AbandonAsync(foreign, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());
        var controller = CreateController(service, CreateCoordinator());

        Assert.IsType<NotFoundObjectResult>(await controller.Abandon(notFound, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Abandon(invalid, CancellationToken.None));
        Assert.IsType<ConflictObjectResult>(await controller.Abandon(foreign, CancellationToken.None));
    }

    [Fact]
    public void Abandon_RequiresAdministratorOrApiKey()
    {
        var abandon = typeof(FileRegistrationRecoveryController)
            .GetMethod(nameof(FileRegistrationRecoveryController.Abandon))!;

        Assert.NotNull(abandon.GetCustomAttribute<RequireAdminOrApiKeyAttribute>());
        Assert.Equal("{operationId:guid}/abandon", abandon.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    private static FileRegistrationRecoveryController CreateController(
        Mock<IFileRegistrationRecoveryService> service,
        Mock<IFilesystemMutationCoordinator> coordinator) =>
        new(service.Object, coordinator.Object);

    private static Mock<IFilesystemMutationCoordinator> CreateCoordinator()
    {
        var coordinator = new Mock<IFilesystemMutationCoordinator>(MockBehavior.Strict);
        coordinator
            .Setup(candidate => candidate.ExecuteExclusiveAsync(
                It.IsAny<Func<CancellationToken, Task<FileRegistrationRecoveryStatus>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<FileRegistrationRecoveryStatus>> operation, CancellationToken token) =>
                operation(token));
        return coordinator;
    }
}
