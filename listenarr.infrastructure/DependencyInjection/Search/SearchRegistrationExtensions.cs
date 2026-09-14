/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Listenarr.Infrastructure.DependencyInjection.Search;

internal static class SearchRegistrationExtensions
{
    public static IServiceCollection AddSearchServices(this IServiceCollection services)
    {
        services.AddScoped<IIndexerSearchProvider, InternetArchiveSearchProvider>();
        services.AddScoped<IIndexerSearchProvider, TorznabNewznabSearchProvider>();
        services.AddScoped<IIndexerSearchProvider, MyAnonamouseSearchProvider>();
        services.AddScoped<IMyAnonamouseConnectionTester, MyAnonamouseConnectionTester>();
        services.AddScoped<IndexerAdditionalSettingsParser>();

        // The startup window has to time from process start and the status service is scoped, so
        // the window is a singleton of its own rather than a field on the service.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IndexerBackoffStartupWindow>();
        services.AddScoped<IIndexerStatusService, IndexerStatusService>();
        services.AddScoped<IndexerSearchWorkflow>();
        services.AddScoped<MetadataSourceCatalog>();
        services.AddScoped<SearchFinalDispositionLogger>();
        services.AddScoped<ISearchService, SearchService>();
        return services;
    }

    public static IServiceCollection AddSearchInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IIndexerRepository, EfIndexerRepository>();
        return services;
    }
}
