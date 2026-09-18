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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Downloads.Blocklist
{
    public class BlocklistService : IBlocklistService
    {
        private readonly ListenArrDbContext _context;
        private readonly ILogger<BlocklistService> _logger;

        public BlocklistService(ListenArrDbContext context, ILogger<BlocklistService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// The blocklist table, reached through Set&lt;T&gt; rather than through a DbSet property
        /// on ListenArrDbContext.
        ///
        /// EF finds the entity either way: BlockedReleaseConfiguration implements
        /// IEntityTypeConfiguration&lt;BlockedRelease&gt; and OnModelCreating already calls
        /// ApplyConfigurationsFromAssembly, which puts the type in the model without a property
        /// declaring it. What the property would add is one more line to the single list that
        /// every pull request adding a table has to edit, and that list is where those pull
        /// requests collide with each other.
        /// </summary>
        private DbSet<BlockedRelease> BlockedReleases => _context.Set<BlockedRelease>();

        public async Task BlockAsync(
            int audiobookId,
            string releaseIdentifier,
            string title,
            long? size,
            string reason)
        {
            if (string.IsNullOrWhiteSpace(releaseIdentifier))
            {
                return;
            }

            var already = await BlockedReleases.AnyAsync(
                entry => entry.AudiobookId == audiobookId
                    && entry.ReleaseIdentifier == releaseIdentifier);
            if (already)
            {
                return;
            }

            var entry = BlockedReleases.Add(new BlockedRelease
            {
                AudiobookId = audiobookId,
                ReleaseIdentifier = releaseIdentifier,
                Title = title,
                Size = size,
                Reason = reason
            });

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (UniqueConstraintViolationException)
            {
                // The AnyAsync above and this insert are not one operation, and
                // (AudiobookId, ReleaseIdentifier) is uniquely indexed. Two failures observed for
                // the same release in one poll cycle, or a failure racing a retry, both get past
                // the read and the loser's insert violates the index. The row the winner wrote
                // says exactly what this one would have said, so losing the race is the same
                // outcome as finding the entry already there.
                //
                // It has to be caught rather than left to propagate. The only caller is
                // DownloadMonitorService.OnDownloadFailed, which has more failure handling to do
                // after this line, so an exception escaping here abandons that work partway
                // through for what is not an error. DownloadProcessingJobService.EnqueueAsync
                // treats its own unique index the same way.
                entry.State = EntityState.Detached;
                _logger.LogDebug(
                    "Release was already blocked for audiobook {AudiobookId} by a concurrent failure",
                    audiobookId);
                return;
            }

            _logger.LogInformation(
                "Blocked release for audiobook {AudiobookId} so it is not grabbed again: {Reason}",
                audiobookId,
                reason);
        }

        public async Task<IReadOnlyList<BlockedRelease>> GetForAudiobookAsync(int audiobookId)
        {
            return await BlockedReleases
                .Where(entry => entry.AudiobookId == audiobookId)
                .OrderByDescending(entry => entry.BlockedAt)
                .ToListAsync();
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var entry = await BlockedReleases
                .FirstOrDefaultAsync(candidate => candidate.Id == id);
            if (entry is null)
            {
                return false;
            }

            BlockedReleases.Remove(entry);
            await _context.SaveChangesAsync();
            _logger.LogInformation(
                "Removed blocklist entry {BlocklistEntryId} for audiobook {AudiobookId}",
                id,
                entry.AudiobookId);
            return true;
        }

        public async Task<int> ClearForAudiobookAsync(int audiobookId)
        {
            var entries = await BlockedReleases
                .Where(entry => entry.AudiobookId == audiobookId)
                .ToListAsync();
            if (entries.Count == 0)
            {
                return 0;
            }

            BlockedReleases.RemoveRange(entries);
            await _context.SaveChangesAsync();
            _logger.LogInformation(
                "Cleared {RemovedCount} blocklist entries for audiobook {AudiobookId}",
                entries.Count,
                audiobookId);
            return entries.Count;
        }
    }
}
