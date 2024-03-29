﻿using CloudFabric.Libraries.Search.Attributes;
using CloudFabric.Libraries.Search.Services.Interfaces;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
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
        private ConcurrentDictionary<Type, string> _indexMapping;

        private Dictionary<string, string> _propertyShortCutMapping = new Dictionary<string, string>();

        public AzureSearchService(string searchServiceName, string apiKey, ConcurrentDictionary<Type, string> indexMapping = null, TelemetryClient telemetryClient = null)
        {
            _telemetryClient = telemetryClient;
            _searchClient = new SearchServiceClient(searchServiceName, new SearchCredentials(apiKey));

            if (indexMapping == null)
            {
                _indexMapping = new ConcurrentDictionary<Type, string>();
            }
            else
            {
                _indexMapping = indexMapping;
            }
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

            var facetProperties = SearchablePropertyAttribute.GetFacetableProperties<T>();

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
            List<string> propertiesToSelect = GetPropertiesToSelect(resultType);

            SearchParameters sp = new SearchParameters()
            {
                Select = propertiesToSelect,
                SearchMode = searchRequest.SearchMode == "All" ? SearchMode.All : SearchMode.Any,
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
                //foreach (var facetProp in facetProperties)
                //{
                //if (azureSearchResults.Body.Facets.ContainsKey(facetProp.Key.Name))
                //{
                foreach (var kv in azureSearchResults.Body.Facets)
                {
                    var facetValues = new List<FacetStats>();

                    foreach (var facetValue in kv.Value)
                    {
                        facetValues.Add(new FacetStats()
                        {
                            Value = facetValue.Value,
                            Count = facetValue.Count,
                            From = (double?)facetValue.From,
                            To = (double?)facetValue.To
                        });
                    }

                    results.FacetsStats.Add(kv.Key.Replace("/", "."), facetValues);
                }
                //  }
                //}
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
            var indexName = _indexMapping[typeof(T)];
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

        /// <summary>
        /// Returns list of properties to select from search service by reading model's members
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        private List<string> GetPropertiesToSelect(Type t)
        {
            MemberInfo[] props = t.GetMembers();
            List<string> propertiesToSelect = new List<string>();

            foreach (var p in props)
            {
                if (p.GetCustomAttributes(typeof(IgnorePropertyAttribute), true).Length > 0)
                {
                    continue;
                }

                if (p.MemberType == MemberTypes.Method ||
                    p.MemberType == MemberTypes.Constructor ||
                    p.MemberType == MemberTypes.Event)
                {
                    continue;
                }

                Type propertyType = null;

                if (p.MemberType == MemberTypes.Property)
                {
                    propertyType = (p as PropertyInfo).PropertyType;
                }
                else if (p.MemberType == MemberTypes.Field)
                {
                    propertyType = (p as FieldInfo).FieldType;
                }

                var isList = propertyType.GetTypeInfo().IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>);

                if (propertyType.GetType().IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    propertyType = Nullable.GetUnderlyingType(propertyType);
                }

                if (isList)
                {
                    propertyType = propertyType.GetGenericArguments()[0];
                }

                if (propertyType.Namespace == "System" || propertyType.BaseType.Name == "Enum")
                {
                    propertiesToSelect.Add(p.Name);
                }
                else
                {
                    var childProperties = GetPropertiesToSelect(propertyType);
                    propertiesToSelect.AddRange(childProperties.Select(cp => p.Name + "/" + cp));
                }
            }

            return propertiesToSelect;
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
            switch (SearchablePropertyAttribute.GetPropertyTypeCode<T>(propertyName))
            {
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Byte:
                    if (filter.Value == null)
                    {
                        filterValue += "null";
                    }
                    else
                    {
                        filterValue += $"{filter.Value}";
                    }
                    break;
                case TypeCode.Char:
                case TypeCode.String:
                    if (filter.Value == null)
                    {
                        filterValue += "null";
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

            return $"{propertyName.Replace(".", "/")} {filterOperator} {filterValue}";
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
