/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Metadata.Core;

/// <summary>
/// The lookup service is where the ISBN endpoint's three answers are decided. Its catch-all
/// turns anything that goes wrong into a false with "Lookup failed", and a false is read by
/// callers as a verdict on the ISBN, so the fault has to leave by the other door.
/// </summary>
/// <remarks>
/// Tested here rather than only at the endpoint. The controller tests mock the lookup service
/// away, so they pin what the controller does with a raised fault and say nothing about whether
/// anything still raises one: delete the rethrow and every one of them still passes.
/// </remarks>
[Trait("Area", "Metadata")]
[Trait("Name", "AsinLookupServiceFaultTests")]
[Trait("Category", "Application")]
public sealed class AsinLookupServiceFaultTests : BaseTests
{
    private const string Isbn = "9780000000001";

    private static Mock<AudibleService> Audible(out HttpClient client)
    {
        client = new HttpClient();
        return new Mock<AudibleService>(client, Mock.Of<ILogger<AudibleService>>());
    }

    private static AsinLookupService Service(Mock<AudibleService> audible) =>
        new(audible.Object, Mock.Of<ILogger<AsinLookupService>>());

    [Theory]
    [Trait("Scenario", "ProviderFaultLeavesByTheOtherDoor")]
    [InlineData("throttled")]
    [InlineData("transport")]
    public async Task GetAsinFromIsbnAsync_Raises_WhenTheProviderDidNotAnswer(string fault)
    {
        Exception raised = fault == "throttled"
            ? new MetadataProviderThrottledException("the provider asked for less traffic", TimeSpan.FromSeconds(30))
            : new HttpRequestException("the request timed out");

        var audible = Audible(out var client);
        using var _ = client;
        audible
            .Setup(service => service.SearchByIsbnAsync(
                Isbn, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(raised);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(
            () => Service(audible).GetAsinFromIsbnAsync(Isbn));

        Assert.Same(raised, thrown);
    }

    [Fact]
    [Trait("Scenario", "AProviderThatAnsweredAndHadNothingIsStillAMiss")]
    public async Task GetAsinFromIsbnAsync_ReportsNotFound_WhenTheProviderAnsweredWithNoResults()
    {
        var audible = Audible(out var client);
        using var _ = client;
        audible
            .Setup(service => service.SearchByIsbnAsync(
                Isbn, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(new AudibleSearchResponse { Results = [] });

        var (success, asin, error) = await Service(audible).GetAsinFromIsbnAsync(Isbn);

        // The half that must not change. Without this the rethrow above could be satisfied by
        // raising on everything, which would turn every unresolvable ISBN into an outage.
        Assert.False(success);
        Assert.Null(asin);
        Assert.Equal("ASIN not found for ISBN", error);
    }

    [Fact]
    [Trait("Scenario", "SomethingElseGoingWrongIsStillALookupFailure")]
    public async Task GetAsinFromIsbnAsync_StillReportsLookupFailed_WhenTheFaultSaysNothingAboutTheProvider()
    {
        var audible = Audible(out var client);
        using var _ = client;
        audible
            .Setup(service => service.SearchByIsbnAsync(
                Isbn, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("the search response could not be read"));

        var (success, _, error) = await Service(audible).GetAsinFromIsbnAsync(Isbn);

        // The catch-all keeps everything it used to keep. Only the fault shapes the predicate
        // names propagate, which is what stops the rethrow being a blanket one.
        Assert.False(success);
        Assert.Equal("Lookup failed", error);
    }
}
