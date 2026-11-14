/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Application.Search.Scheduling;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Infrastructure.Search.RequestGate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Listenarr.Infrastructure.DependencyInjection.Search;

internal static class SearchRegistrationExtensions
{
    public static IServiceCollection AddSearchServices(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IndexerRequestStateCache>();
        services.AddSingleton<IIndexerRequestGate, IndexerRequestGate>();
        services.AddScoped<IIndexerSearchProvider>(provider =>
            ActivatorUtilities.CreateInstance<InternetArchiveSearchProvider>(provider, CreateIndexerClient(provider)));
        services.AddScoped<IIndexerSearchProvider>(provider =>
            ActivatorUtilities.CreateInstance<TorznabNewznabSearchProvider>(provider, CreateIndexerClient(provider)));
        services.AddScoped<IIndexerSearchProvider>(provider =>
            ActivatorUtilities.CreateInstance<MyAnonamouseSearchProvider>(provider, CreateIndexerClient(provider)));
        services.AddScoped<IMyAnonamouseConnectionTester, MyAnonamouseConnectionTester>();
        services.AddScoped<IndexerAdditionalSettingsParser>();
        services.AddScoped<IndexerSearchWorkflow>();
        services.AddScoped<MetadataSourceCatalog>();
        services.AddScoped<SearchFinalDispositionLogger>();
        services.AddScoped<ISearchService, SearchService>();
        return services;
    }

    // Search providers send on the indexer client, which leaves every redirect hop to the request gate.
    private static HttpClient CreateIndexerClient(IServiceProvider provider) =>
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(IndexerHttpDispatch.ClientName);

    public static IServiceCollection AddSearchInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IIndexerRepository, EfIndexerRepository>();
        services.AddScoped<IAutomaticSearchScheduleStore, EfAutomaticSearchScheduleStore>();
        services.AddScoped<IAutomaticSearchAcquisition, AutomaticSearchAcquisition>();
        services.AddSingleton<IIndexerRequestStateStore, EfIndexerRequestStateStore>();
        return services;
    }
}
