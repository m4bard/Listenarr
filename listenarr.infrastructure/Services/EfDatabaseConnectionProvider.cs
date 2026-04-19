using System.Data.Common;
using System.Threading.Tasks;
using Listenarr.Application.Services;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Services
{
    public class EfDatabaseConnectionProvider : IDatabaseConnectionProvider
    {
        private readonly ListenArrDbContext _dbContext;

        public EfDatabaseConnectionProvider(ListenArrDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<DbConnection> GetOpenConnectionAsync()
        {
            var conn = _dbContext.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();
            return conn;
        }
    }
}
