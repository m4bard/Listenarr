using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Repositories
{
    public class EfRootFolderRepository : IRootFolderRepository
    {
        private readonly IDbContextFactory<ListenArrDbContext> _dbFactory;
        private readonly ILogger<EfRootFolderRepository> _logger;

        public EfRootFolderRepository(IDbContextFactory<ListenArrDbContext> dbFactory, ILogger<EfRootFolderRepository> logger)
        {
            _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task AddAsync(RootFolder root)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            ctx.RootFolders.Add(root);
            await ctx.SaveChangesAsync();
        }

        public async Task<List<RootFolder>> GetAllAsync()
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.RootFolders.OrderBy(r => r.Name).ToListAsync();
        }

        public async Task<RootFolder?> GetByIdAsync(int id)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.RootFolders.FindAsync(id);
        }

        public async Task<RootFolder?> GetByPathAsync(string path)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.RootFolders.FirstOrDefaultAsync(r => r.Path == path);
        }

        public async Task RemoveAsync(int id)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var r = await ctx.RootFolders.FindAsync(id);
            if (r == null) return;
            ctx.RootFolders.Remove(r);
            await ctx.SaveChangesAsync();
        }

        public async Task UpdateAsync(RootFolder root)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            ctx.RootFolders.Update(root);
            await ctx.SaveChangesAsync();
        }

        public async Task<RootFolder?> GetDefaultAsync()
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.RootFolders.FirstOrDefaultAsync(r => r.IsDefault);
        }
    }
}
