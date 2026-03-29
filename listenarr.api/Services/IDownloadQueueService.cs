using System.Collections.Generic;
using System.Threading.Tasks;

namespace Listenarr.Api.Services
{
    public interface IDownloadQueueService
    {
        Task<QueueSnapshot> GetQueueSnapshotAsync();
        Task<List<QueueItem>> GetQueueAsync();
    }
}
