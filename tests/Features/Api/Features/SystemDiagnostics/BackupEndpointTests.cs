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
    public async Task BackupEndpoints_RequireCredentials_WhenAuthenticationIsEnabled()
    {
        // Given an instance configured to require authentication
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupConfigService>(_ =>
                    new StartupConfigServiceMock(new StartupConfig
                    {
                        AuthenticationRequired = "true"
                    }))));

        var apiBase = TestUtils.ResolveApiBasePath(factory.Services);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        // When an anonymous caller asks to list or to create
        using var listed = await client.GetAsync($"{apiBase}/system/backup");
        using var created = await client.PostAsync($"{apiBase}/system/backup", null);

        // Then both are refused. This is the assertion that fails if [RequireAdminOrApiKey] is
        // removed from the controller, on endpoints that describe and produce a file holding the
        // API key, the admin password hash and every download client credential.
        Assert.NotEqual(HttpStatusCode.OK, listed.StatusCode);
        Assert.NotEqual(HttpStatusCode.Created, created.StatusCode);
        Assert.True(
            listed.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Listing returned {(int)listed.StatusCode} rather than 401 or 403.");
        Assert.True(
            created.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Create returned {(int)created.StatusCode} rather than 401 or 403.");
    }
}
