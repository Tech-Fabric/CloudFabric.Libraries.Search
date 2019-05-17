using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloudFabric.Libraries.Search.Harvester
{
    public interface ISearchUploader
    {
        Task<bool> Upload<T>(IEnumerable<T> records) where T : class;

        bool Delete<T>(IEnumerable<string> idList) where T : class;
    }
}
