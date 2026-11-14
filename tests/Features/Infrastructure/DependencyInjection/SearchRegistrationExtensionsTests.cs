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

using Listenarr.Infrastructure.DependencyInjection.Search;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.DependencyInjection;

[Trait("Name", "SearchRegistrationExtensionsTests")]
[Trait("Category", "Infrastructure")]
public sealed class SearchRegistrationExtensionsTests : BaseTests
{
    [Fact]
    public void AddSearchServices_RegistersPassThroughGateAsSingleton()
    {
        // Given
        var services = new ServiceCollection();

        // When
        services.AddSearchServices();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        var first = provider.GetRequiredService<IIndexerRequestGate>();
        var second = provider.GetRequiredService<IIndexerRequestGate>();

        // Then
        var descriptor = Assert.Single(services, service => service.ServiceType == typeof(IIndexerRequestGate));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.IsType<PassThroughIndexerRequestGate>(first);
        Assert.Same(first, second);
    }
}
