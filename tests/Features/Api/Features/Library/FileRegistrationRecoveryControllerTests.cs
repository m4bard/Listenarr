using System.Security.Claims;
using Listenarr.Application.Common.Exceptions;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Tests.Features.Api.Features.Library;

[Trait("Area", "Library")]
[Trait("Name", "FileRegistrationRecoveryControllerTests")]
[Trait("Category", "Api")]
public sealed class FileRegistrationRecoveryControllerTests : BaseTests
{
    private static readonly string Token = new('a', 64);
    private static readonly FileRegistrationRecoveryController.ResolveRequest Request = new(Token, "note", true);

    [Fact]
    public async Task Resolve_RunsUnderTheExclusiveLock_PassesTheRequestAndReturnsTheReview()
    {
        var operationId = Guid.NewGuid();
        var review = Review(operationId);
        var service = new Mock<IFileRegistrationRecoveryService>(MockBehavior.Strict);
        service.Setup(s => s.ResolveAsync(operationId, Token, "note", true, "operator-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(review);
        var coordinator = Coordinator();
        var controller = Controller(service, coordinator, new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "operator-1")], "test")));

        var result = await controller.Resolve(operationId, Request, CancellationToken.None);

        Assert.Same(review, Assert.IsType<OkObjectResult>(result).Value);
        coordinator.Verify(c => c.ExecuteExclusiveAsync(
            It.IsAny<Func<CancellationToken, Task<FileRegistrationReview>>>(), It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(s => s.ResolveAsync(operationId, Token, "note", true, "operator-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolve_WithoutAnyIdentity_StillSuppliesANonEmptyActor()
    {
        var operationId = Guid.NewGuid();
        string? actor = null;
        var service = new Mock<IFileRegistrationRecoveryService>(MockBehavior.Strict);
        service.Setup(s => s.ResolveAsync(operationId, Token, "note", true, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, bool, string, CancellationToken>((_, _, _, _, caller, _) => actor = caller)
            .ReturnsAsync(Review(operationId));

        await Controller(service, Coordinator(), new ClaimsPrincipal(new ClaimsIdentity())).Resolve(operationId, Request, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(actor));
    }

    [Theory]
    [InlineData(typeof(KeyNotFoundException), typeof(NotFoundResult), 404)]
    [InlineData(typeof(ArgumentException), typeof(BadRequestObjectResult), 400)]
    [InlineData(typeof(InvalidOperationException), typeof(ConflictObjectResult), 409)]
    public async Task Resolve_MapsServiceRefusalsToStatusCodes(Type exception, Type resultType, int statusCode)
    {
        var operationId = Guid.NewGuid();
        var service = new Mock<IFileRegistrationRecoveryService>(MockBehavior.Strict);
        service.Setup(s => s.ResolveAsync(operationId, Token, "note", true, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync((Exception)Activator.CreateInstance(exception)!);

        var result = await Controller(service, Coordinator()).Resolve(operationId, Request, CancellationToken.None);

        Assert.IsType(resultType, result);
        Assert.Equal(statusCode, ((Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)result).StatusCode);
    }

    [Fact]
    public async Task Resolve_StaleReview_PropagatesTheConflictForTheExceptionHandler()
    {
        var operationId = Guid.NewGuid();
        var service = new Mock<IFileRegistrationRecoveryService>(MockBehavior.Strict);
        service.Setup(s => s.ResolveAsync(operationId, Token, "note", true, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationConflictException("registration_review_stale", "stale"));

        var stale = await Assert.ThrowsAsync<ApplicationConflictException>(
            () => Controller(service, Coordinator()).Resolve(operationId, Request, CancellationToken.None));

        Assert.Equal("registration_review_stale", stale.Code);
    }

    [Fact]
    public async Task Inspect_ReturnsTheReview_AndMapsRefusals()
    {
        var found = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var review = Review(found);
        var service = new Mock<IFileRegistrationRecoveryService>(MockBehavior.Strict);
        service.Setup(s => s.InspectAsync(found, It.IsAny<CancellationToken>())).ReturnsAsync(review);
        service.Setup(s => s.InspectAsync(missing, It.IsAny<CancellationToken>())).ThrowsAsync(new KeyNotFoundException());
        service.Setup(s => s.InspectAsync(foreign, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        var controller = Controller(service, Coordinator());

        Assert.Same(review, Assert.IsType<OkObjectResult>(await controller.Inspect(found, CancellationToken.None)).Value);
        Assert.IsType<NotFoundResult>(await controller.Inspect(missing, CancellationToken.None));
        Assert.IsType<ConflictObjectResult>(await controller.Inspect(foreign, CancellationToken.None));
    }

    [Fact]
    public async Task List_ReturnsTheServiceReviewsWithoutTakingTheExclusiveLock()
    {
        var reviews = new[] { Review(Guid.NewGuid()), Review(Guid.NewGuid()) };
        var service = new Mock<IFileRegistrationRecoveryService>(MockBehavior.Strict);
        service.Setup(s => s.ListReviewsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(reviews);
        var coordinator = new Mock<IFilesystemMutationCoordinator>(MockBehavior.Strict);

        var result = await Controller(service, coordinator).List(CancellationToken.None);

        Assert.Equal(reviews, result);
    }

    private static FileRegistrationReview Review(Guid operationId) => new(operationId, 7, "HardlinkCopy", 2,
        "NeedsAttention", "reason", "/source/book.m4b", "/destination/book.m4b",
        new("Present", 1, "AB"), new("Missing", null, null), [], "Completeness cannot be verified",
        true, Token, null, null, null);

    private static FileRegistrationRecoveryController Controller(
        Mock<IFileRegistrationRecoveryService> service, Mock<IFilesystemMutationCoordinator> coordinator,
        ClaimsPrincipal? user = null) => new(service.Object, coordinator.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal() } }
        };

    private static Mock<IFilesystemMutationCoordinator> Coordinator()
    {
        var coordinator = new Mock<IFilesystemMutationCoordinator>(MockBehavior.Strict);
        coordinator.Setup(c => c.ExecuteExclusiveAsync(
                It.IsAny<Func<CancellationToken, Task<FileRegistrationReview>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<FileRegistrationReview>> operation, CancellationToken token) => operation(token));
        return coordinator;
    }
}
