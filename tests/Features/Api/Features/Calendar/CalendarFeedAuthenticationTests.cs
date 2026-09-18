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

using System.Net;
using Asp.Versioning.ApiExplorer;
using Listenarr.Api.Features.Calendar;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Listenarr.Tests.Features.Api.Features.Calendar;

/// <summary>
/// The feed URL is long lived, ends up inside third-party calendar software, and often leaves the
/// local network, so how it authenticates is the part of this feature most worth pinning down.
/// </summary>
[Trait("Area", "Calendar")]
[Trait("Name", "CalendarFeedAuthenticationTests")]
[Trait("Category", "Integration")]
public sealed class CalendarFeedAuthenticationTests : BaseTests, IClassFixture<ListenarrWebApplicationFactory>
{
    private const string ApiKey = "test-api-key-0123456789";
    private const string FeedPath = "/feed/v1/calendar/" + CalendarFeedController.FeedFileName;

    private readonly ListenarrWebApplicationFactory _factory;

    public CalendarFeedAuthenticationTests(ListenarrWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Feed_WithApiKeyInTheQueryString_IsServed()
    {
        // This is the whole point. A calendar client subscribes to a URL and cannot attach a
        // header, so the key has to ride in the query, spelled "apikey" exactly as Sonarr, Radarr
        // and Readarr spell it, or an operator's pasted URL will not work.
        using var factory = WithAuthenticationEnabled();
        using var client = NewClient(factory);

        var response = await client.GetAsync($"{FeedPath}?apikey={ApiKey}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/calendar", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Feed_WithApiKeyInTheHeader_IsAlsoServed()
    {
        using var factory = WithAuthenticationEnabled();
        using var client = NewClient(factory);
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        var response = await client.GetAsync(FeedPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Feed_WithNoKey_IsRefusedByTheAuthenticationEnforcer()
    {
        // The feed sits outside /api on purpose, and the authentication enforcer used to wave
        // through everything that was not /api or /hubs. Without the enforcer knowing about
        // /feed, this request would be served and the whole library would be readable.
        //
        // The status code alone does not pin that guard: [RequireApiKey] returns a 401 of its
        // own, so deleting the /feed clause from the enforcer leaves a status-only assertion
        // green. The body is the discriminator, because the two layers write different ones. The
        // enforcer writes this JSON itself (AuthenticationEnforcerMiddleware.cs:110-112) and runs
        // before MVC; the attribute's UnauthorizedResult (RequireApiKeyAttribute.cs:65) comes back
        // as an RFC 9457 problem document instead. Measured, and see the case-variant test below,
        // which asserts the other shape.
        using var factory = WithAuthenticationEnabled();
        using var client = NewClient(factory);

        var response = await client.GetAsync(FeedPath);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"message\":\"Authentication required\"}", body);
    }

    [Fact]
    public async Task Feed_WithACaseVariantPathAndNoKey_IsRefusedByTheAttributeInstead()
    {
        // The enforcer's prefix checks pass no StringComparison, so they are case sensitive and
        // /FEED/... slips past the /feed clause. MVC routing is case insensitive, so the request
        // still reaches the controller, where [RequireApiKey] refuses it. This case is the one
        // place the two layers come apart, so it is the one worth asserting.
        //
        // The discriminator is the body, but not in the way it looks from the source.
        // UnauthorizedResult does not produce a bodyless 401 here: the MVC problem-details
        // handler renders it as an RFC 9457 document. Measured, not read. So the two layers are
        // still told apart by the body, and this assertion fails the moment the enforcer starts
        // catching this path too.
        using var factory = WithAuthenticationEnabled();
        using var client = NewClient(factory);

        var response = await client.GetAsync("/FEED/v1/calendar/" + CalendarFeedController.FeedFileName);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("Authentication required", body, StringComparison.Ordinal);
        Assert.Contains("\"status\":401", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feed_WithAnIgnorableCharacterInThePath_IsStillNotServed()
    {
        // The enforcer's three prefix checks pass no StringComparison, so they run under the
        // current culture, and a culture comparison treats U+200D and U+00AD as ignorable. So
        // "/\u200dfeed/v1/..." satisfies StartsWith("/feed") and the enforcer catches it, while
        // StringComparison.Ordinal would wave it through. Measured across en-US, de-DE, tr-TR and
        // the invariant culture; the difference is the same in all four.
        //
        // Measured both ways at this boundary, and the outcome is the same: routing does not
        // match the literal segment either, so the request is refused regardless of which
        // comparison the enforcer used. The clause is therefore left culture-sensitive, matching
        // the /api and /hubs clauses beside it, which pass no comparison either and are
        // pre-existing. Switching only /feed to Ordinal would make the enforcer catch strictly
        // fewer paths for no observable gain, which is the wrong direction to move a security
        // check on a hunch. This test pins the outcome rather than the mechanism.
        using var factory = WithAuthenticationEnabled();
        using var client = NewClient(factory);

        var response = await client.GetAsync(
            "/\u200dfeed/v1/calendar/" + CalendarFeedController.FeedFileName);
        var body = await response.Content.ReadAsStringAsync();

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("BEGIN:VCALENDAR", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feed_WithACaseVariantPathAndTheKey_IsStillServed()
    {
        // The other half of the same seam: ApiKeyMiddleware's query-key carve-out does compare
        // case insensitively, so a correct key still works on the odd casing.
        using var factory = WithAuthenticationEnabled();
        using var client = NewClient(factory);

        var response = await client.GetAsync(
            $"/FEED/v1/calendar/{CalendarFeedController.FeedFileName}?apikey={ApiKey}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Feed_WithTheWrongKey_IsRefused()
    {
        using var factory = WithAuthenticationEnabled();
        using var client = NewClient(factory);

        var response = await client.GetAsync($"{FeedPath}?apikey=not-the-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiRoutes_StillRefuseAKeyInTheQueryString()
    {
        // The query-string carve-out is scoped to the feed. Widening it to /api would undo the
        // reason ApiKeyMiddleware refuses query-string keys in the first place.
        using var factory = WithAuthenticationEnabled();
        using var client = NewClient(factory);
        var apiBasePath = ResolveApiBasePath(factory.Services);

        var response = await client.GetAsync($"{apiBasePath}/library?apikey={ApiKey}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Feed_IsNotCachedByAnythingInBetween()
    {
        using var factory = WithAuthenticationEnabled();
        using var client = NewClient(factory);

        var response = await client.GetAsync($"{FeedPath}?apikey={ApiKey}");

        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Feed_ServesAWellFormedDocumentWithAnEmptyLibrary()
    {
        using var factory = WithAuthenticationEnabled();
        using var client = NewClient(factory);

        var response = await client.GetAsync($"{FeedPath}?apikey={ApiKey}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.StartsWith("BEGIN:VCALENDAR", body, StringComparison.Ordinal);
        Assert.EndsWith("END:VCALENDAR\r\n", body, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> WithAuthenticationEnabled() =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStartupConfigService>();
                services.AddSingleton<IStartupConfigService>(_ =>
                    new StartupConfigServiceMock(new StartupConfig
                    {
                        AuthenticationRequired = "Enabled",
                        ApiKey = ApiKey
                    }));
            }));

    private static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string ResolveApiBasePath(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider.GetService<IApiVersionDescriptionProvider>();
        var groupName = provider?.ApiVersionDescriptions.FirstOrDefault(d => !d.IsDeprecated)?.GroupName
            ?? provider?.ApiVersionDescriptions.FirstOrDefault()?.GroupName;

        return string.IsNullOrWhiteSpace(groupName) ? "/api/v1" : $"/api/{groupName}";
    }
}
