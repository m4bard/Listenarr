/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Domain.Common;

namespace Listenarr.Api.Features.Downloads;

public sealed class ManualImportDestinationTracker(
    IFileSystem fileSystem,
    IFileSystemSemanticsResolver semanticsResolver)
{
    private readonly Dictionary<string, HashSet<string>> _usedDestinationsByBoundary = new(StringComparer.Ordinal);

    public int Count => _usedDestinationsByBoundary.Values.Sum(set => set.Count);

    public async Task<string> ReserveUniqueAsync(
        string desiredDestination,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(desiredDestination))
        {
            throw new ArgumentException("Destination path is required.", nameof(desiredDestination));
        }

        var resolution = await semanticsResolver.ResolveAsync(
            Path.GetDirectoryName(desiredDestination) ?? desiredDestination,
            cancellationToken: cancellationToken);
        if (resolution.State != PathIdentityState.Valid)
        {
            throw new InvalidOperationException(
                resolution.Reason ?? "Destination filesystem identity is unavailable.");
        }

        var boundaryKey = FileSystemPathIdentity.CreateKey(
            "manual-import-boundary",
            resolution.BoundaryPath,
            resolution.Semantics);
        if (!_usedDestinationsByBoundary.TryGetValue(boundaryKey, out var usedDestinations))
        {
            usedDestinations = new HashSet<string>(resolution.Semantics.Comparer);
            _usedDestinationsByBoundary[boundaryKey] = usedDestinations;
        }

        // Use the destination volume's case rules for both in-memory batch collisions
        // and pre-existing path checks so macOS/Linux mounted case-insensitive volumes
        // do not accept two case-only variants in the same import batch.
        var uniqueDestination = FileUtils.GetUniqueDestinationPath(
            desiredDestination,
            fileSystem.FileExists,
            usedDestinations);
        usedDestinations.Add(uniqueDestination);
        return uniqueDestination;
    }
}
