/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Application.Calendar;
using Listenarr.Application.Calendar.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Listenarr.Infrastructure.DependencyInjection.Calendar;

internal static class CalendarRegistrationExtensions
{
    public static IServiceCollection AddCalendarServices(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Scoped: the calendar query runs on the shared scoped ListenArrDbContext.
        services.AddScoped<ICalendarService, CalendarService>();

        // Singleton: the writer holds no request state.
        services.AddSingleton<ICalendarDocumentWriter, CalendarDocumentWriter>();
        return services;
    }
}
