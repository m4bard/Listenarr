using System.Data.Common;
using System.Threading.Tasks;

namespace Listenarr.Application.Services
{
    public interface IDatabaseConnectionProvider
    {
        Task<DbConnection> GetOpenConnectionAsync();
    }
}
