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
// csharp
using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Listenarr.Api.Services;

namespace Listenarr.Api.Tests
{
    /// <summary>
    /// Shared test fixture that centralizes common test registrations.
    /// Use with xUnit IClassFixture/Test collection fixtures to reduce duplication.
    /// </summary>
    public class TestServicesFixture : IDisposable
    {
        public ServiceProvider Provider { get; }
        public IServiceScopeFactory ScopeFactory { get; }

        public TestServicesFixture()
        {
            var services = new ServiceCollection();

            // Basic infra commonly used across tests
            services.AddLogging();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            services.AddSingleton<IConfiguration>(configuration);

            // Common cross-cutting services useful for IO-heavy tests
            services.AddSingleton<IFileStorage, Listenarr.Api.Services.FileStorage>();
            services.AddMemoryCache();
            services.AddSingleton<MetadataExtractionLimiter>();

            // Allow tests to override / add more registrations as needed by calling CreateScope + registering within scope
            Provider = services.BuildServiceProvider(validateScopes: true);
            ScopeFactory = Provider.GetRequiredService<IServiceScopeFactory>();
        }

        public void Dispose()
        {
            (Provider as IDisposable)?.Dispose();
        }
    }
}
