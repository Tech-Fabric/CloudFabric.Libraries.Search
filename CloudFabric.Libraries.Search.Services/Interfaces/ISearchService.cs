using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloudFabric.Libraries.Search.Services.Interfaces
{
    public interface ISearchService
    {
        Task<List<SuggestionResultRecord<T>>> Suggest<T>(string searchText, string suggesterName, bool fuzzy = true);
        Task<SearchResult<ResultT>> Query<T, ResultT>(SearchRequest searchRequest, bool track = false) where T : class where ResultT : class;
    }
}
