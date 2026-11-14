/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Infrastructure.Persistence;
using Listenarr.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Listenarr.Infrastructure.DependencyInjection.Persistence;

internal static class PersistenceRegistrationExtensions
{
    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDb)
    {
        if (configureDb != null)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<AutomaticSearchResetInterceptor>();
            // The factory and the scoped context share these options, so every context the
            // application resolves resets a book's search schedule when its metadata changes.
            services.AddDbContextFactory<ListenArrDbContext>(
                (provider, options) =>
                {
                    configureDb(options);
                    options.AddInterceptors(provider.GetRequiredService<AutomaticSearchResetInterceptor>());
                },
                ServiceLifetime.Singleton);
        }

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<LibraryFilesystemReadiness>();
        services.AddSingleton<ILibraryFilesystemReadiness>(provider =>
            provider.GetRequiredService<LibraryFilesystemReadiness>());
        services.AddSingleton<ILibraryFilesystemMutationGate>(provider =>
            provider.GetRequiredService<LibraryFilesystemReadiness>());
        services.AddSingleton<IMoveQueuePersistence, EfMoveQueuePersistence>();
        services.AddSingleton<IMoveExecutionStore, EfMoveExecutionStore>();
        services.AddSingleton<IMoveScanHandoffStore, EfMoveScanHandoffStore>();
        services.AddScoped<IHistoryRepository, EfHistoryRepository>();
        return services;
    }
}
