using System.Collections.Generic;

namespace CloudFabric.Libraries.Search
{
    public class FacetStats
    {
        public object Value { get; set; }
        public long? Count { get; set; }
        public double? From { get; set; }
        public double? To { get; set; }
        public string SumByField { get; set; } = null;
        public double? SumByValue { get; set; }
    }

    public class SearchResultRecord<T>
    {
        public double Score { get; set; }
        public Dictionary<string, List<string>> Highlights { get; set; } = new Dictionary<string, List<string>>();
        public T Record { get; set; }

        public string GetHighlightedTextForField(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName) || !Highlights.ContainsKey(fieldName) || Highlights[fieldName].Count < 1)
            {
                return null;
            }

            return Highlights[fieldName][0];
        }
    }

    public class SearchResult<T>
    {
        public Dictionary<string, List<FacetStats>> FacetsStats = new Dictionary<string, List<FacetStats>>();
        public List<SearchResultRecord<T>> Records = new List<SearchResultRecord<T>>();
        public long? TotalRecordsFound = 0;
        public string SearchId;
        public string IndexName;
    }
}
