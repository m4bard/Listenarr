/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories
{
    public class EfIndexerRepository : IIndexerRepository
    {
        private readonly ListenArrDbContext _db;
        private readonly TimeProvider _timeProvider;

        public EfIndexerRepository(ListenArrDbContext db, TimeProvider? timeProvider = null)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<Indexer?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            return await _db.Indexers.FindAsync(new object[] { id }, ct);
        }

        public async Task<Indexer?> GetByNameAsync(string name, CancellationToken ct = default)
        {
            return await _db.Indexers.AsNoTracking().FirstOrDefaultAsync(i => i.Name == name, ct);
        }

        public async Task<List<Indexer>> GetAllAsync(CancellationToken ct = default)
        {
            return await _db.Indexers.AsNoTracking().ToListAsync(ct);
        }

        public async Task<List<Indexer>> GetEnabledAsync(bool isAutomaticSearch, CancellationToken ct = default)
        {
            return await _db.Indexers
                .AsNoTracking()
                .Where(i => i.IsEnabled && (isAutomaticSearch ? i.EnableAutomaticSearch : i.EnableInteractiveSearch))
                .OrderBy(i => i.Priority)
                .ToListAsync(ct);
        }

        public async Task<Indexer> AddAsync(Indexer indexer, CancellationToken ct = default)
        {
            _db.Indexers.Add(indexer);
            await SaveAsync(becameEligible: IsEligibleForAutomaticSearch(indexer), ct);
            return indexer;
        }

        public async Task UpdateAsync(Indexer indexer, CancellationToken ct = default)
        {
            // Read from the database, not from the tracked entity: a caller that loaded the indexer
            // through this context has already changed the instance FindAsync returns.
            var wasEligible = await _db.Indexers
                .AsNoTracking()
                .Where(stored => stored.Id == indexer.Id)
                .Select(stored => stored.IsEnabled && stored.EnableAutomaticSearch)
                .FirstOrDefaultAsync(ct);
            var existing = await _db.Indexers.FindAsync(new object[] { indexer.Id }, ct);
            if (existing == null) throw new InvalidOperationException($"Indexer {indexer.Id} not found.");
            _db.Entry(existing).CurrentValues.SetValues(indexer);
            await SaveAsync(becameEligible: !wasEligible && IsEligibleForAutomaticSearch(indexer), ct);
        }

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            var indexer = await _db.Indexers.FindAsync(new object[] { id }, ct);
            if (indexer == null) return;
            _db.Indexers.Remove(indexer);
            await _db.SaveChangesAsync(ct);
        }

        private static bool IsEligibleForAutomaticSearch(Indexer indexer) =>
            indexer.IsEnabled && indexer.EnableAutomaticSearch;

        // An indexer that becomes eligible for automatic search has searched no book yet, so every
        // monitored book is made due now instead of waiting out the backoff its other indexers set;
        // their pair rows keep their own times, so only the new indexer is asked. The indexer write
        // and the due-time pull commit together or not at all. The due time is ignored after save,
        // so on a relational provider the pull is set-based; EF InMemory has neither set-based
        // updates nor transactions, and one tracked save carries both there.
        private async Task SaveAsync(bool becameEligible, CancellationToken ct)
        {
            if (!becameEligible)
            {
                await _db.SaveChangesAsync(ct);
                return;
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            if (!_db.Database.IsRelational())
            {
                foreach (var book in await BooksDueAfter(nowUtc).ToListAsync(ct))
                {
                    book.AutomaticSearchDueUtc = nowUtc;
                }

                await _db.SaveChangesAsync(ct);
                return;
            }

            await using var transaction = _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(ct)
                : null;
            // Changes are accepted only once both statements have run, so a failed pull leaves the
            // indexer pending in this context as well as absent from the database.
            await _db.SaveChangesAsync(acceptAllChangesOnSuccess: false, ct);
            await BooksDueAfter(nowUtc)
                .ExecuteUpdateAsync(setters => setters.SetProperty(book => book.AutomaticSearchDueUtc, nowUtc), ct);
            if (transaction != null)
            {
                await transaction.CommitAsync(ct);
            }

            _db.ChangeTracker.AcceptAllChanges();
        }

        private IQueryable<Audiobook> BooksDueAfter(DateTime nowUtc) =>
            _db.Audiobooks.Where(book =>
                book.Monitored && book.QualityProfileId != null && book.AutomaticSearchDueUtc > nowUtc);
    }
}
