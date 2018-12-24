using CloudFabric.Libraries.Search.Attributes;
using CloudFabric.Libraries.Search.Services.Interfaces;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace CloudFabric.Libraries.Search.Services.Azure.Implementations
{
    public class AzureSearchService : ISearchService
    {
        private TelemetryClient _telemetryClient;
        private SearchServiceClient _searchClient;

        private static string HighlightTagStart = "<span class=\"highlight\">";
        private static string HighlightTagEnd = "</span>";

        /// <summary>
        /// Allows remapping indexes stored on SearchAttributes to new names.
        /// </summary>
        private Dictionary<string, string> _indexMapping;

        private Dictionary<string, string> _propertyShortCutMapping = new Dictionary<string, string>();

        public AzureSearchService(string searchServiceName, string apiKey, Dictionary<string, string> indexMapping = null, TelemetryClient telemetryClient = null)
        {
            _telemetryClient = telemetryClient;
            _searchClient = new SearchServiceClient(searchServiceName, new SearchCredentials(apiKey));

            if (indexMapping == null)
            {
                _indexMapping = new Dictionary<string, string>();
            }
            else
            {
                _indexMapping = indexMapping;
            }

            //var filterPropertiesShortcuts = typeof(Search.FilterProperties).GetFields();
            //foreach (var f in filterPropertiesShortcuts)
            //{
            //    var shortcut = (string)f.GetValue(f);
            //    if (_propertyShortCutMapping.ContainsKey(shortcut))
            //    {
            //        Console.Error.WriteLine("FilterProperties object contains duplicate property value.");
            //    }
            //    else
            //    {
            //        _propertyShortCutMapping.Add(shortcut, f.Name);
            //    }
            //}
        }

        private string GetPropertyName(string propertyShortcut)
        {
            if (string.IsNullOrEmpty(propertyShortcut))
            {
                return propertyShortcut;
            }

            if (_propertyShortCutMapping.ContainsKey(propertyShortcut))
            {
                return _propertyShortCutMapping[propertyShortcut];
            }

            return propertyShortcut;
        }

        public async Task<List<SuggestionResultRecord<T>>> Suggest<T>(string searchText, string suggesterName, bool fuzzy = true)
        {
            var indexClient = GetIndexForModel<T>();

            if (indexClient == null)
            {
                throw new Exception("Failed to get indexClient. Make sure index exists.");
            }

            // Execute search based on query string
            try
            {
                SuggestParameters sp = new SuggestParameters()
                {
                    UseFuzzyMatching = fuzzy,
                    Top = 8,
                    HighlightPreTag = HighlightTagStart,
                    HighlightPostTag = HighlightTagEnd
                };

                var azureSuggestionResult = await indexClient.Documents.SuggestAsync(searchText, suggesterName, sp);

                var results = new List<SuggestionResultRecord<T>>();

                foreach (var record in azureSuggestionResult.Results)
                {
                    JObject obj = JObject.FromObject(record.Document);

                    var resultRecord = new SuggestionResultRecord<T>();
                    resultRecord.Record = obj.ToObject<T>();
                    resultRecord.TextWithHighlights = record.Text;

                    results.Add(resultRecord);
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error querying index: {0}\r\n", ex.Message.ToString());
            }
            return null;
        }

        public async Task<SearchResult<ResultT>> Query<T, ResultT>(
            SearchRequest searchRequest,
            bool track = false
        )
            where T : class
            where ResultT : class
        {
            var indexClient = GetIndexForModel<T>();

            if (indexClient == null)
            {
                throw new Exception("Failed to get indexClient. Make sure index exists.");
            }

            var facetProperties = SearchableModelAttribute.GetFacetableProperties<T>();

            List<string> facets = new List<string>();

            if (searchRequest.FacetInfoToReturn != null && searchRequest.FacetInfoToReturn.Count > 0)
            {
                foreach (FacetInfoRequest f in searchRequest.FacetInfoToReturn)
                {
                    var facetDefinition = GetPropertyName(f.FacetName);

                    if (f.Values != null && f.Values.Count() > 0)
                    {
                        facetDefinition += $",values:{string.Join("|", f.Values)}";
                    }
                    else
                    {
                        facetDefinition += $",count:{f.Count},sort:{f.Sort}";
                    }

                    facets.Add(facetDefinition);
                }
            }

            Type resultType = typeof(ResultT);
            MemberInfo[] props = resultType.GetMembers();
            List<string> propertiesToSelect = props
                .Where(p => 
                    (p.MemberType == MemberTypes.Field || p.MemberType == MemberTypes.Property) && 
                    p.GetCustomAttributes(typeof(IgnorePropertyAttribute), true).Length == 0
                )
                .Select(p => p.Name)
                .ToList();

            SearchParameters sp = new SearchParameters()
            {
                Select = propertiesToSelect,
                SearchMode = SearchMode.Any,
                QueryType = QueryType.Full,
                Top = searchRequest.Limit,
                Skip = searchRequest.Offset,
                // Add count
                IncludeTotalResultCount = true,
                // Add search highlights
                HighlightFields = searchRequest.FieldsToHighlight.Select(f => GetPropertyName(f)).ToList(),
                HighlightPreTag = HighlightTagStart,
                HighlightPostTag = HighlightTagEnd,
                // Add facets
                Facets = facets,
            };

            if (!string.IsNullOrEmpty(searchRequest.ScoringProfile))
            {
                sp.ScoringProfile = searchRequest.ScoringProfile;
            }

            if (searchRequest.OrderBy.Count > 0)
            {
                var orderByFields = new List<string>();

                foreach (var orderBy in searchRequest.OrderBy)
                {
                    var orderByPropertyName = GetPropertyName(orderBy.Key);
                    orderByFields.Add($"{orderByPropertyName} {orderBy.Value}");
                }

                sp.OrderBy = orderByFields;
            }

            if (searchRequest.Filters != null && searchRequest.Filters.Count > 0)
            {
                List<string> filterStrings = new List<string>();
                foreach (var f in searchRequest.Filters)
                {
                    filterStrings.Add($"({ConstructConditionFilter<T>(f)})");
                }

                sp.Filter = string.Join(" and ", filterStrings);
            }

            string searchText = searchRequest.SearchText;
            var headers = new Dictionary<string, List<string>>() { { "x-ms-azs-return-searchid", new List<string>() { "true" } } };

            var azureSearchResults = await indexClient.Documents.SearchWithHttpMessagesAsync(searchText, sp, customHeaders: headers);

            IEnumerable<string> headerValues;
            string searchId = string.Empty;
            if (azureSearchResults.Response.Headers.TryGetValues("x-ms-azs-searchid", out headerValues))
            {
                searchId = headerValues.FirstOrDefault();
            }

            if (track)
            {
                var properties = new Dictionary<string, string> {
                    {"SearchServiceName", _searchClient.SearchServiceName},
                    {"SearchId", searchId},
                    {"IndexName", indexClient.IndexName},
                    {"QueryTerms", searchText},
                    {"ResultCount", azureSearchResults.Body.Count.ToString()},
                    {"ScoringProfile", sp.ScoringProfile}
                };
                _telemetryClient.TrackEvent("Search", properties);
            }

            SearchResult<ResultT> results = new SearchResult<ResultT>();

            results.SearchId = searchId;
            results.IndexName = indexClient.IndexName;
            results.TotalRecordsFound = azureSearchResults.Body.Count;

            if (azureSearchResults.Body.Facets != null)
            {
                foreach (var facetProp in facetProperties)
                {
                    if (azureSearchResults.Body.Facets.ContainsKey(facetProp.Key.Name))
                    {
                        var facetValues = new List<FacetStats>();

                        foreach (var facetStat in azureSearchResults.Body.Facets[facetProp.Key.Name])
                        {
                            facetValues.Add(new FacetStats()
                            {
                                Value = facetStat.Value,
                                Count = facetStat.Count,
                                From = (double?)facetStat.From,
                                To = (double?)facetStat.To
                            });
                        }

                        results.FacetsStats.Add(facetProp.Key.Name, facetValues);
                    }
                }
            }

            foreach (var record in azureSearchResults.Body.Results)
            {
                try
                {
                    JObject obj = JObject.FromObject(record.Document);

                    var resultRecord = new SearchResultRecord<ResultT>();
                    resultRecord.Record = obj.ToObject<ResultT>();
                    resultRecord.Score = record.Score;

                    if (record.Highlights != null)
                    {
                        foreach (var highlight in record.Highlights)
                        {
                            var sourceFieldValue = obj.GetValue(highlight.Key).ToString();

                            foreach (var h in highlight.Value.ToList())
                            {
                                sourceFieldValue = sourceFieldValue.Replace(h.Replace(HighlightTagStart, String.Empty).Replace(HighlightTagEnd, String.Empty), h);
                            }

                            resultRecord.Highlights.Add(highlight.Key, new List<string>() { sourceFieldValue });
                        }
                    }

                    results.Records.Add(resultRecord);
                }
                catch (Exception ex)
                {
                    var a = ex;
                }
            }

            return results;
        }

        private Dictionary<string, ISearchIndexClient> _indexClients = new Dictionary<string, ISearchIndexClient>();

        private ISearchIndexClient GetIndexForModel<T>()
        {
            var indexName = SearchableModelAttribute.GetIndexName(typeof(T));

            if (_indexMapping.ContainsKey(indexName))
            {
                indexName = _indexMapping[indexName];
            }

            ISearchIndexClient indexClient = null;

            if (_indexClients.ContainsKey(indexName))
            {
                indexClient = _indexClients[indexName];
            }
            else
            {
                indexClient = _searchClient.Indexes.GetClient(indexName);
                _indexClients.Add(indexName, indexClient);
            }

            return indexClient;
        }

        private string ConstructOneConditionFilter<T>(Filter filter)
        {
            var filterOperator = "";
            var propertyName = GetPropertyName(filter.PropertyName);

            if (string.IsNullOrEmpty(propertyName))
            {
                return filterOperator;
            }

            switch (filter.Operator)
            {
                case FilterOperator.Equal:
                    filterOperator = "eq";
                    break;
                case FilterOperator.Greater:
                    filterOperator = "gt";
                    break;
                case FilterOperator.GreaterOrEqual:
                    filterOperator = "ge";
                    break;
                case FilterOperator.Lower:
                    filterOperator = "lt";
                    break;
                case FilterOperator.LowerOrEqual:
                    filterOperator = "le";
                    break;
                case FilterOperator.NotEqual:
                    filterOperator = "ne";
                    break;
            }

            var filterValue = "";
            switch (SearchableModelAttribute.GetPropertyTypeCode<T>(propertyName))
            {
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Byte:
                    filterValue += $"{filter.Value}";
                    break;
                case TypeCode.Char:
                case TypeCode.String:
                    if (filter.Value == null)
                    {
                        filterValue += "''";
                    }
                    else
                    {
                        var v = filter.Value.ToString();
                        v = v.Replace("'", "''");
                        filterValue += $"'{v}'";
                    }
                    break;
                case TypeCode.Boolean:
                    filterValue += $"{filter.Value.ToString().ToLower()}";
                    break;
                case TypeCode.DateTime:
                    DateTime dt = (DateTime)filter.Value;
                    filterValue += $"{dt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")}";
                    break;
            }

            return $"{propertyName} {filterOperator} {filterValue}";
        }

        private string ConstructConditionFilter<T>(Filter filter)
        {
            var q = ConstructOneConditionFilter<T>(filter);

            foreach (FilterConnector f in filter.Filters)
            {
                if (!string.IsNullOrEmpty(q) && f.Logic != null)
                {
                    q += $" {f.Logic} ";
                }

                var wrapWithParentheses = f.Logic != null && f.Filter.Filters.Count > 0;

                if (wrapWithParentheses)
                {
                    q += "(";
                }

                q += ConstructConditionFilter<T>(f.Filter);

                if (wrapWithParentheses)
                {
                    q += ")";
                }
            }

            return q;
        }
    }
}
