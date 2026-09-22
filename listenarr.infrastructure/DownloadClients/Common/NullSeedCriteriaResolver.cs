/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Infrastructure.DownloadClients.Common;

/// <summary>
/// A resolver that never applies seed criteria. Used only by the legacy manual-wiring
/// constructors on the torrent adapters (kept for callers that build an adapter directly
/// instead of through DI, without access to a repository-backed resolver); the DI-registered
/// adapters always use <c>SeedCriteriaResolver</c> instead.
/// </summary>
internal sealed class NullSeedCriteriaResolver : ISeedCriteriaResolver
{
    public Task<TorrentSeedConfiguration?> ResolveAsync(int? indexerId, CancellationToken ct = default)
        => Task.FromResult<TorrentSeedConfiguration?>(null);
}
