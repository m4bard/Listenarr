/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Listenarr.Infrastructure.Persistence;

/// <summary>WIP skeleton: the metadata-change reset is not implemented yet.</summary>
public sealed class AutomaticSearchResetInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public TimeProvider Clock { get; } = timeProvider;
}
