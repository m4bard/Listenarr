/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Security.Claims;
using Listenarr.Api.Attributes;
using Listenarr.Api.Features.ActivityHistory;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.ActivityHistory
{
    [Trait("Area", "ActivityHistory")]
    [Trait("Name", "HistoryControllerQueryTests")]
    [Trait("Category", "Api")]
    public class HistoryControllerQueryTests : BaseTests
    {
        private readonly Mock<IHistoryRepository> _history = new();
        private readonly HistoryController _controller;
        private HistoryQuery? _received;

        public HistoryControllerQueryTests()
        {
            _history
                .Setup(repository => repository.QueryAsync(It.IsAny<HistoryQuery>(), It.IsAny<CancellationToken>()))
                .Callback<HistoryQuery, CancellationToken>((query, _) => _received = query)
                .ReturnsAsync(new HistoryPage([], 0, 50, 0));

            _controller = new HistoryController(
                _history.Object,
                new Mock<IConfigurationService>().Object,
                new Mock<ILogger<HistoryController>>().Object);
        }

        [Fact]
        public async Task GetAll_NoEventTypeFilter_LeavesBothEventFieldsUnset()
        {
            await _controller.GetAll();

            Assert.NotNull(_received);
            Assert.Null(_received!.EventType);
            Assert.Null(_received.EventTypes);
        }

        // The single-value form is the one every existing caller sends, and it has to keep
        // producing exactly the query it did before, or widening the parameter is a behaviour
        // change dressed up as an addition.
        [Fact]
        public async Task GetAll_SingleEventType_StaysASingleExactMatch()
        {
            await _controller.GetAll(eventType: HistoryEvents.Grabbed);

            Assert.Equal(HistoryEvents.Grabbed, _received!.EventType);
            Assert.Null(_received.EventTypes);
        }

        [Fact]
        public async Task GetAll_CommaSeparatedEventTypes_BecomeOneSetOnOneQuery()
        {
            await _controller.GetAll(eventType: "DownloadFailed,Removed");

            Assert.Null(_received!.EventType);
            Assert.Equal(new[] { "DownloadFailed", "Removed" }, _received.EventTypes);
            _history.Verify(
                repository => repository.QueryAsync(It.IsAny<HistoryQuery>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // Several real event types contain spaces, so the split trims around the commas and
        // nowhere else.
        [Fact]
        public async Task GetAll_EventTypesWithSpacesSurviveTheSplit()
        {
            await _controller.GetAll(eventType: "File Added, File Removed ,Scan Incomplete");

            Assert.Equal(
                new[] { "File Added", "File Removed", "Scan Incomplete" },
                _received!.EventTypes);
        }

        [Fact]
        public async Task GetAll_EmptySegmentsAreDroppedRatherThanMatchingAnEmptyEventType()
        {
            await _controller.GetAll(eventType: "Grabbed,,");

            Assert.Equal(HistoryEvents.Grabbed, _received!.EventType);
            Assert.Null(_received.EventTypes);
        }

        [Fact]
        public async Task GetAll_ThreadsEveryOtherFilterOntoTheQuery()
        {
            var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);

            await _controller.GetAll(
                limit: 25,
                offset: 50,
                sortBy: "outcome",
                sortDirection: "asc",
                eventType: null,
                outcome: HistoryOutcome.Failed,
                from: from,
                to: to,
                audiobookId: 42,
                downloadId: "ABC123",
                downloadClientId: "client-1",
                correlationId: "corr-1");

            Assert.Equal(25, _received!.Limit);
            Assert.Equal(50, _received.Offset);
            Assert.Equal("outcome", _received.SortBy);
            Assert.Equal("asc", _received.SortDirection);
            Assert.Equal(HistoryOutcome.Failed, _received.Outcome);
            Assert.Equal(from, _received.From);
            Assert.Equal(to, _received.To);
            Assert.Equal(42, _received.AudiobookId);
            Assert.Equal("ABC123", _received.DownloadId);
            Assert.Equal("client-1", _received.DownloadClientId);
            Assert.Equal("corr-1", _received.CorrelationId);
        }
    }

    /// <summary>
    /// HistoryController is the only controller in the codebase carrying
    /// <see cref="RequireAdministratorSessionAttribute"/>, and the frontend has no role in its
    /// user model, so it cannot pre-hide the controls this gate protects; it has to attempt and
    /// handle. An install running with authentication off takes the first branch below for every
    /// request, which means neither of the refusing branches can be exercised by clicking. They
    /// exist only here.
    /// </summary>
    [Trait("Area", "ActivityHistory")]
    [Trait("Name", "RequireAdministratorSessionFilterTests")]
    [Trait("Category", "Api")]
    public class RequireAdministratorSessionFilterTests : BaseTests
    {
        private readonly Mock<IStartupConfigService> _startupConfig = new();

        private static ActionExecutingContext ContextFor(ClaimsPrincipal user)
        {
            var httpContext = new DefaultHttpContext { User = user };
            var actionContext = new ActionContext(
                httpContext,
                new RouteData(),
                new ActionDescriptor());

            return new ActionExecutingContext(
                actionContext,
                [],
                new Dictionary<string, object?>(),
                controller: null!);
        }

        private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

        private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
            new(new ClaimsIdentity(claims, authenticationType: "Cookies"));

        private async Task<ActionExecutingContext> RunAsync(ClaimsPrincipal user)
        {
            var filter = new RequireAdministratorSessionFilter(_startupConfig.Object);
            var context = ContextFor(user);
            var called = false;

            await filter.OnActionExecutionAsync(context, () =>
            {
                called = true;
                return Task.FromResult(new ActionExecutedContext(context, [], controller: null!));
            });

            // A filter that both refused and ran the action would be a gate in name only.
            Assert.Equal(context.Result == null, called);
            return context;
        }

        [Fact]
        public async Task AuthenticationDisabled_AllowsEveryCaller()
        {
            _startupConfig.Setup(config => config.IsAuthenticationRequired()).Returns(false);

            Assert.Null((await RunAsync(Anonymous())).Result);
        }

        [Fact]
        public async Task AuthenticationEnabled_AnonymousCallerIsUnauthorized()
        {
            _startupConfig.Setup(config => config.IsAuthenticationRequired()).Returns(true);

            Assert.IsType<UnauthorizedResult>((await RunAsync(Anonymous())).Result);
        }

        [Fact]
        public async Task AuthenticationEnabled_AuthenticatedNonAdminIsForbidden()
        {
            _startupConfig.Setup(config => config.IsAuthenticationRequired()).Returns(true);

            var result = Assert.IsType<StatusCodeResult>(
                (await RunAsync(Authenticated(new Claim(ClaimTypes.Name, "reader")))).Result);

            Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        }

        // An API key is not an interactive administrator session, which is the whole point of
        // this attribute existing separately from RequireApiKey.
        [Fact]
        public async Task AuthenticationEnabled_ApiKeyAdministratorIsStillForbidden()
        {
            _startupConfig.Setup(config => config.IsAuthenticationRequired()).Returns(true);

            var result = Assert.IsType<StatusCodeResult>(
                (await RunAsync(Authenticated(
                    new Claim(ClaimTypes.Role, "Administrator"),
                    new Claim("AuthMethod", "ApiKey")))).Result);

            Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        }

        [Fact]
        public async Task AuthenticationEnabled_AdministratorSessionIsAllowed()
        {
            _startupConfig.Setup(config => config.IsAuthenticationRequired()).Returns(true);

            var context = await RunAsync(Authenticated(new Claim(ClaimTypes.Role, "Administrator")));

            Assert.Null(context.Result);
        }
    }
}
