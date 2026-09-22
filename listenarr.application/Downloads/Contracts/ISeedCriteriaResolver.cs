/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Application.Downloads.Contracts;

/// <summary>
/// The per-indexer seed policy to apply to a torrent when it is added to a download client.
/// Either field may be null; a null field means "leave the download client's own configuration
/// alone" and must never be sent to the client as an explicit override.
/// </summary>
public sealed record TorrentSeedConfiguration(double? Ratio, TimeSpan? SeedTime)
{
    public bool HasAnyValue => Ratio.HasValue || SeedTime.HasValue;
}

/// <summary>
/// Resolves the seed policy for the indexer a torrent was grabbed from. Looks the indexer up
/// directly by id, the same shape as Readarr's SeedConfigProvider
/// (src/NzbDrone.Core/Indexers/SeedConfigProvider.cs:44-51 in a Readarr checkout), because
/// Listenarr's submission contract already carries the grabbing indexer's id at add time and
/// does not need Readarr's infohash-keyed history walk
/// (src/NzbDrone.Core/Download/DownloadSeedConfigProvider.cs).
/// </summary>
public interface ISeedCriteriaResolver
{
    Task<TorrentSeedConfiguration?> ResolveAsync(int? indexerId, CancellationToken ct = default);
}
