/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Application.Downloads.Submission;

public sealed class SeedCriteriaResolver(IIndexerRepository indexerRepository) : ISeedCriteriaResolver
{
    public async Task<TorrentSeedConfiguration?> ResolveAsync(int? indexerId, CancellationToken ct = default)
    {
        if (indexerId is not int id)
        {
            return null;
        }

        var indexer = await indexerRepository.GetByIdAsync(id, ct);
        if (indexer == null)
        {
            return null;
        }

        if (indexer.SeedRatio is null && indexer.SeedTime is null)
        {
            return null;
        }

        return new TorrentSeedConfiguration(
            indexer.SeedRatio,
            indexer.SeedTime.HasValue ? TimeSpan.FromMinutes(indexer.SeedTime.Value) : null);
    }
}
