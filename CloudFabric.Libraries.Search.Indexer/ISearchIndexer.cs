using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloudFabric.Libraries.Search.Indexer
{
    public interface ISearchIndexer
    {
        Task CreateSynonymMaps(Dictionary<string, List<string>> synonymMaps);
        Task<string> CreateIndex<T>(string newIndexName = null) where T : class;
    }
}
