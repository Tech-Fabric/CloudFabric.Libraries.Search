using System.Collections.Generic;

namespace CloudFabric.Libraries.Search.Api
{
    public class SearchApiResponse<ResponseRecordT>
    {
        public string SearchId { get; set; }

        public string IndexName { get; set; }

        public List<SearchResultRecord<ResponseRecordT>> Records { get; set; }

        public long? TotalRecordsFound { get; set; }

        public Dictionary<string, List<FacetStats>> FacetsStats = new Dictionary<string, List<FacetStats>>();
    }
}
