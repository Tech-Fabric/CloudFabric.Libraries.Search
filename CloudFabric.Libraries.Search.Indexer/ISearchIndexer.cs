using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloudFabric.Libraries.Search.Indexer
{
    public interface ISearchIndexer
    {
        Task CreateSynonymMaps(Dictionary<string, List<string>> synonymMaps);
        Task<bool> CreateIndex<T>() where T : class;
    }
}
