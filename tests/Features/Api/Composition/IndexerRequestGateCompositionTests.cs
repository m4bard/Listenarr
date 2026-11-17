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

using System.Reflection;
using Listenarr.Infrastructure.Search.RequestGate;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Tests.Features.Api.Composition;

/// <summary>
/// The application's own container must hand every request-gate consumer the one real gate. A
/// consumer that falls back to the pass-through gate (several take the gate as an optional
/// parameter) or gets a second instance would send without the shared cooldowns, caps and pacing,
/// and every unit test that builds its gate by hand would still pass.
/// </summary>
[Trait("Area", "Composition")]
[Trait("Name", "IndexerRequestGateCompositionTests")]
[Trait("Category", "Composition")]
public sealed class IndexerRequestGateCompositionTests : BaseTests
{
    [Fact]
    public void ApplicationContainer_GivesEveryGateConsumerTheOneRealGate()
    {
        IServiceCollection? registrations = null;
        using var factory = new ListenarrWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => registrations = services));
        var root = factory.Services;
        using var scope = root.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IIndexerRequestGate>();

        var consumers = GateConsumerTypes();
        var instances = consumers.ToDictionary(type => type, type => Resolve(type, registrations!, scope.ServiceProvider));
        // A consumer built with `new` inside another one (the Torznab parser) is reached through it.
        foreach (var type in consumers.Where(type => instances[type] is null))
        {
            instances[type] = instances.Values
                .Where(owner => owner is not null)
                .SelectMany(owner => Reachable(owner!))
                .FirstOrDefault(type.IsInstanceOfType);
        }

        Assert.IsType<IndexerRequestGate>(gate);
        Assert.Same(gate, root.GetRequiredService<IIndexerRequestGate>());
        Assert.Contains(typeof(SystemService), consumers);
        Assert.Contains(typeof(IndexersController), consumers);
        Assert.Empty(instances.Where(pair => pair.Value is null).Select(pair => pair.Key.Name));
        var gatesHeld = instances.ToDictionary(
            pair => pair.Key.Name,
            pair => Reachable(pair.Value!).OfType<IIndexerRequestGate>().Distinct().ToList());
        Assert.Empty(gatesHeld.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key));
        Assert.Empty(gatesHeld.Where(pair => pair.Value.Any(held => !ReferenceEquals(held, gate))).Select(pair => pair.Key));
    }

    // Every production type that takes the gate in a constructor, other than the gates themselves.
    private static List<Type> GateConsumerTypes() =>
        new[] { typeof(IIndexerRequestGate).Assembly, typeof(IndexerRequestGate).Assembly, typeof(IndexersController).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && !typeof(IIndexerRequestGate).IsAssignableFrom(type)
                && type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Any(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IIndexerRequestGate))))
            .ToList();

    private static object? Resolve(Type type, IServiceCollection registrations, IServiceProvider services)
    {
        if (typeof(ControllerBase).IsAssignableFrom(type))
        {
            return ActivatorUtilities.CreateInstance(services, type);
        }

        var direct = registrations.FirstOrDefault(descriptor => descriptor.ServiceType == type || descriptor.ImplementationType == type);
        if (direct is not null)
        {
            return services.GetServices(direct.ServiceType).FirstOrDefault(type.IsInstanceOfType);
        }

        // Registered through a factory under an interface, as the search providers are.
        return type.GetInterfaces()
            .Where(contract => registrations.Any(descriptor => descriptor.ServiceType == contract))
            .SelectMany(contract => services.GetServices(contract))
            .FirstOrDefault(type.IsInstanceOfType);
    }

    // The object and what its fields hold, two levels down, within the application's own types.
    private static IEnumerable<object> Reachable(object root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance) { root };
        var frontier = new List<object> { root };
        for (var depth = 0; depth < 3 && frontier.Count > 0; depth++)
        {
            frontier = frontier
                .SelectMany(owner => owner.GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(field => field.GetValue(owner)))
                .Where(value => value is not null
                    && (value is IIndexerRequestGate || value.GetType().Namespace?.StartsWith("Listenarr", StringComparison.Ordinal) == true)
                    && seen.Add(value))
                .Select(value => value!)
                .ToList();
        }

        return seen;
    }
}
