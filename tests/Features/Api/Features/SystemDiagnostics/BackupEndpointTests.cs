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
using System.Text.Json;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Listenarr.Tests.Features.Api.Features.SystemDiagnostics;

/// <summary>
/// The HTTP surface of the backup feature. The archive it describes carries every credential the
/// instance holds, so the shape of these responses and the attribute guarding them are worth
/// pinning rather than trusting to review.
/// </summary>
[Trait("Name", "BackupEndpointTests")]
[Trait("Category", "Backup")]
public sealed class BackupEndpointTests : IClassFixture<ListenarrWebApplicationFactory>
{
    private readonly ListenarrWebApplicationFactory _factory;

    public BackupEndpointTests(ListenarrWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Trait("Scenario", "ListingShape")]
    public async Task GetBackups_ReturnsNameTriggerSizeAndTime_AndNothingPathShaped()
    {
        // Given an instance that has taken a backup
        var apiBase = TestUtils.ResolveApiBasePath(_factory.Services);
        using var client = _factory.CreateClient();

        using var created = await client.PostAsync($"{apiBase}/system/backup", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // When the listing is fetched
        using var response = await client.GetAsync($"{apiBase}/system/backup");

        // Then it carries exactly the four fields the operator screen needs
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entry = Assert.Single(document.RootElement.EnumerateArray().Take(1).ToList());

        Assert.Equal(
            ["createdAtUtc", "name", "sizeBytes", "trigger"],
            entry.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        // And nothing in it says where the archive lives. A path would tell an unauthenticated
        // caller on a default install exactly where to go looking for the API key.
        var name = entry.GetProperty("name").GetString();
        Assert.NotNull(name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
    }

    [Fact]
    [Trait("Scenario", "ManualCreateIsRecordedAsManual")]
    public async Task CreateBackup_Returns201_AndRecordsTheBackupAsManual()
    {
        // Given a running instance
        var apiBase = TestUtils.ResolveApiBasePath(_factory.Services);
        using var client = _factory.CreateClient();

        // When a backup is requested
        using var response = await client.PostAsync($"{apiBase}/system/backup", null);

        // Then it is created, and marked manual so the age sweep will never remove it
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Manual", document.RootElement.GetProperty("trigger").GetString());
        Assert.True(document.RootElement.GetProperty("sizeBytes").GetInt64() > 0);
    }

    [Fact]
    [Trait("Scenario", "GuardedWhenAuthIsOn")]
    public async Task BackupEndpoints_RefuseANonAdminSession_WhenAuthenticationIsEnabled()
    {
        // Given an instance requiring authentication, and a caller who is logged in but is not an
        // administrator. That combination is the one [RequireAdminOrApiKey] decides on its own:
        // the authentication middleware only distinguishes a signed-in caller from an anonymous
        // one, so an anonymous request would be refused with or without the attribute and proves
        // nothing about it.
        using var factory = CreateAuthEnabledFactory();
        var apiBase = TestUtils.ResolveApiBasePath(factory.Services);

        string adminToken;
        string userToken;
        using (var scope = factory.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
            adminToken = await sessions.CreateSessionAsync("admin", true, false);
            userToken = await sessions.CreateSessionAsync("someone", false, false);
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        // When the non-admin asks to list and to create
        using var listed = await Send(client, HttpMethod.Get, $"{apiBase}/system/backup", userToken);
        using var created = await Send(client, HttpMethod.Post, $"{apiBase}/system/backup", userToken);

        // Then both are refused
        Assert.Equal(HttpStatusCode.Forbidden, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);

        // Control, on the same instance with the same middleware: an administrator gets through.
        // Without that, a test asserting 403 would pass just as happily against an endpoint that
        // was broken for everybody.
        using var adminListed = await Send(client, HttpMethod.Get, $"{apiBase}/system/backup", adminToken);
        Assert.Equal(HttpStatusCode.OK, adminListed.StatusCode);
    }

    [Fact]
    [Trait("Scenario", "AnonymousIsRefusedWhenAuthIsOn")]
    public async Task BackupEndpoints_RefuseAnAnonymousCaller_WhenAuthenticationIsEnabled()
    {
        // Given an instance requiring authentication
        using var factory = CreateAuthEnabledFactory();
        var apiBase = TestUtils.ResolveApiBasePath(factory.Services);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        // When an anonymous caller asks to list and to create
        using var listed = await client.GetAsync($"{apiBase}/system/backup");
        using var created = await client.PostAsync($"{apiBase}/system/backup", null);

        // Then both are refused. The middleware is what does this rather than the attribute, so
        // this pins the route into the authenticated area, not the attribute itself.
        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, created.StatusCode);
    }

    private static Task<HttpResponseMessage> Send(
        HttpClient client,
        HttpMethod method,
        string url,
        string sessionToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Cookie", $"listenarr_session={sessionToken}");
        return client.SendAsync(request);
    }

    private WebApplicationFactory<Program> CreateAuthEnabledFactory()
    {
        return _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupConfigService>(_ =>
                    new StartupConfigServiceMock(new StartupConfig
                    {
                        AuthenticationRequired = "true"
                    }))));
    }
}
