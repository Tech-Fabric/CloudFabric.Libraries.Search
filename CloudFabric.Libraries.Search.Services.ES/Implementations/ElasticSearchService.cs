using Nest;
using CloudFabric.Libraries.Search.Attributes;
using CloudFabric.Libraries.Search.Services.Interfaces;
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloudFabric.Libraries.Search.Services.ES.Implementations
{
    public class ElasticSearchService : ISearchService
    {
        private ElasticClient _client = null;
        /// <summary>
        /// Allows remapping indexes stored on SearchAttributes to new names.
        /// </summary>
        private Dictionary<string, string> _indexMapping;

        public ElasticSearchService(string uri, string username, string password, Dictionary<string, string> indexMapping = null)
        {
            var _connectionSettings = new ConnectionSettings(new Uri(uri));
            _connectionSettings.BasicAuthentication(username, password);
            _connectionSettings.DefaultFieldNameInferrer(p => p);
            _connectionSettings.EnableDebugMode();

            _client = new ElasticClient(_connectionSettings);

            _indexMapping = indexMapping == null ? new Dictionary<string, string>() : indexMapping;
        }

        public async Task<SearchResult<ResultT>> Query<T, ResultT>(SearchRequest searchRequest, bool track = false)
            where T : class
            where ResultT: class
        {
            if (!_client.ConnectionSettings.IdProperties.ContainsKey(typeof(T)))
            {
                _client.ConnectionSettings.IdProperties.Add(typeof(T), SearchableModelAttribute.GetKeyPropertyName<T>());
            }

            var indexType = SearchableModelAttribute.GetIndexName(typeof(T));
            var indexName = _indexMapping[indexType];

            var facetProperties = SearchableModelAttribute.GetFacetableProperties<T>();

            var searchResponse = await _client.SearchAsync<ResultT>(s =>
            {
                s = s.Index(indexName);
                s = s.TrackTotalHits();
                s = s.Query(q => ConstructSearchQuery(q, searchRequest));
                s = s.Sort(d => ConstructSort(d, searchRequest));
                s = s.Aggregations(a => ConstructAggregations(a, searchRequest, facetProperties));
                s = s.Take(searchRequest.Limit);
                s = s.Skip(searchRequest.Offset);
                s = s.Highlight(ConstructHighlight);
                return s;
            });

            SearchResult<ResultT> results = new SearchResult<ResultT>();

            results.DebugInformation = searchResponse.DebugInformation;
            results.TotalRecordsFound = searchResponse.Total == -1 ? 0 : searchResponse.Total;

            foreach (var hit in searchResponse.Hits)
            {
                var resultRecord = new SearchResultRecord<ResultT>();
                resultRecord.Record = hit.Source;
                resultRecord.Score = hit.Score.GetValueOrDefault();

                var highlights = hit.Highlight;

                foreach (var h in hit.Highlight)
                {
                    resultRecord.Highlights.Add(h.Key, h.Value.ToList());
                }

                results.Records.Add(resultRecord);
            }

            foreach (var agg in searchResponse.Aggregations)
            {
                var facetValues = new List<FacetStats>();

                if (agg.Value is BucketAggregate)
                {
                    foreach (var item in (agg.Value as BucketAggregate).Items)
                    {
                        if (item is RangeBucket)
                        {
                            facetValues.Add(new FacetStats()
                            {
                                Count = (item as RangeBucket).DocCount,
                                From = (item as RangeBucket).From,
                                To = (item as RangeBucket).To
                            });
                        }
                        else if (item is KeyedBucket<object>)
                        {
                            var facetStats = new FacetStats()
                            {
                                Value = (item as KeyedBucket<object>).Key,
                                Count = (item as KeyedBucket<object>).DocCount
                            };
                            string sumByField = (item as Nest.KeyedBucket<object>)?.Keys?.FirstOrDefault();
                            if (sumByField != null && sumByField != "")
                            {
                                facetStats.SumByField = sumByField;
                                var valueAgg = (ValueAggregate)(item as KeyedBucket<object>).Values.FirstOrDefault();
                                facetStats.SumByValue = valueAgg.Value;
                            }
                            facetValues.Add(facetStats);
                        }
                    }
                }
                else if (agg.Value is SingleBucketAggregate)
                {
                    foreach (var item in ((agg.Value as Nest.SingleBucketAggregate).FirstOrDefault().Value as Nest.BucketAggregate).Items)
                    {
                        var data = ((item as Nest.KeyedBucket<object>).Values.FirstOrDefault() as Nest.SingleBucketAggregate).Values.FirstOrDefault();
                        var facetStats = new FacetStats()
                        {
                            Value = (item as KeyedBucket<object>).Key,
                            Count = (data as SingleBucketAggregate).DocCount
                        };

                        string sumByField = (data as Nest.SingleBucketAggregate).Keys?.FirstOrDefault();
                        if (sumByField != null && sumByField != "")
                        {
                            facetStats.SumByField = sumByField;
                            var valueAgg = (ValueAggregate)((data as SingleBucketAggregate).Values.FirstOrDefault());
                            facetStats.SumByValue = valueAgg.Value;
                        }
                        facetValues.Add(facetStats);
                    }
                }

                results.FacetsStats.Add(agg.Key, facetValues);
            }

            return results;
        }

        private SortDescriptor<T> ConstructSort<T>(SortDescriptor<T> sortDescriptor, SearchRequest searchRequest) where T : class
        {
            foreach (var orderBy in searchRequest.OrderBy)
            {
                sortDescriptor = sortDescriptor.Field(
                    new Nest.Field(orderBy.Key),
                    orderBy.Value.ToLower() == CloudFabric.Libraries.Search.SortOrder.Asc ? Nest.SortOrder.Ascending : Nest.SortOrder.Descending
                );
            }


            return sortDescriptor;
        }

        private QueryContainer ConstructSearchQuery<T>(QueryContainerDescriptor<T> searchDescriptor, SearchRequest searchRequest) where T : class
        {
            QueryContainer result = null;

            var properties = SearchableModelAttribute.GetSearchablePropertyNames<T>();
            var foldedProperties = new List<string>(properties.Select(p => p + ".folded"));
            properties.AddRange(foldedProperties);

            QueryBase textQuery = new MatchAllQuery();

            if (searchRequest.SearchText != "*")
            {
                //textQuery = new MultiMatchQuery()
                //{
                //    //Fields = properties.Select(p => "folding-" + p).ToArray(),
                //    Fields = properties.ToArray(),
                //    Query = searchRequest.SearchText,
                //    Analyzer = "folding-analyzer",
                //    Boost = 1.0,
                //    CutoffFrequency = 0.001,
                //    Fuzziness = Fuzziness.Auto,
                //    Lenient = true,
                //    MaxExpansions = 2,
                //    MinimumShouldMatch = 2,
                //    PrefixLength = 2,
                //    Operator = Operator.Or,
                //    Name = "query"
                //};
                textQuery = new QueryStringQuery() { Query = searchRequest.SearchText };
            }

            if (searchRequest.Filters != null || searchRequest.Filters.Count > 0)
            {
                var filterStrings = new List<string>();

                foreach (var f in searchRequest.Filters)
                {
                    var conditionFilter = $"({ConstructConditionFilter<T>(f)})";
                    var propName = f.PropertyName == null ? f.Filters[0].Filter.PropertyName : f.PropertyName;

                    if (propName.IndexOf(".") == -1)
                    {
                        filterStrings.Add(conditionFilter);
                    }
                }

                var queryStringQuery = new QueryStringQuery() { Query = string.Join(" AND ", filterStrings) };

                var filter = new List<QueryContainer>() { queryStringQuery };

                var nestedQueryStrings = ConstructNestedQueryStrings<T>(searchRequest.Filters);

                foreach (var entry in nestedQueryStrings)
                {
                    var nestedFilter = new NestedQuery()
                    {
                        Path = entry.Key,
                        Query = new BoolQuery()
                        {
                            Filter = new List<QueryContainer>()
                            {
                                new QueryStringQuery() { Query = entry.Value }
                            }
                        }
                    };

                    filter.Add(nestedFilter);
                }

                result = searchDescriptor.Bool(q =>
                    new BoolQuery()
                    {
                        Must = new List<QueryContainer>() { textQuery },
                        Filter = filter
                    }
                );
            }
            else
            {
                result = textQuery;
            }

            return result;
        }

        private Dictionary<string, string> ConstructNestedQueryStrings<T>(List<Filter> filters)
        {
            var result = new Dictionary<string, string>();

            if (filters == null || filters.Count == 0)
            {
                return result;
            }

            var nestedFiltersStrings = new Dictionary<string, List<string>>();

            foreach (var f in filters)
            {
                var propName = f.PropertyName == null ? f.Filters[0].Filter.PropertyName : f.PropertyName;
                var pathParts = propName.Split('.');

                if (pathParts.Count() <= 1)
                {
                    continue;
                }

                var conditionFilter = $"({ConstructConditionFilter<T>(f)})";
                var nestedPath = string.Join(".", pathParts.Take(pathParts.Length - 1));
                if (!nestedFiltersStrings.ContainsKey(nestedPath))
                {
                    nestedFiltersStrings[nestedPath] = new List<string>();
                }
                nestedFiltersStrings[nestedPath].Add(conditionFilter);
            }

            foreach (var entry in nestedFiltersStrings)
            {
                result[entry.Key] = string.Join(" AND ", entry.Value);
            }

            return result;
        }

        private IAggregationContainer ConstructAggregations<T>(
            AggregationContainerDescriptor<T> aggregationContainerDescriptor,
            SearchRequest searchRequest,
            Dictionary<System.Reflection.PropertyInfo, SearchablePropertyAttribute> facetProperties
        ) where T : class
        {
            var aggs = new AggregationDictionary();

            if (searchRequest.FacetInfoToReturn != null)
            {
                foreach (var facetInfo in searchRequest.FacetInfoToReturn)
                {
                    var facetProp = facetProperties.FirstOrDefault(f => f.Key.Name == facetInfo.FacetName);

                    if (facetProp.Key != null && facetProp.Value.FacetableRanges.Length > 0)
                    {
                        List<IAggregationRange> ranges = new List<IAggregationRange>();

                        for (var i = 0; i < facetProp.Value.FacetableRanges.Length; i++)
                        {
                            var from = i == 0 ? -1 : facetProp.Value.FacetableRanges[i - 1];
                            var to = facetProp.Value.FacetableRanges[i];

                            if (from == -1)
                            {
                                ranges.Add(new AggregationRange() { To = to });
                            }
                            else
                            {
                                ranges.Add(new AggregationRange() { From = from, To = to });
                            }
                        }

                        aggs.Add(
                            facetProp.Key.Name,
                            new RangeAggregation(facetProp.Key.Name)
                            {
                                Field = facetProp.Key.Name,
                                Ranges = ranges
                            }
                        );
                    }
                    else
                    {
                        var pathParts = facetInfo.FacetName.Split('.');
                        var termsAgg = new TermsAggregation(facetInfo.FacetName)
                        {
                            Field = facetInfo.FacetName,
                            Size = facetInfo.Count,
                            Order = new List<TermsOrder>() {
                            new TermsOrder() { Key = "_count", Order = Nest.SortOrder.Descending }
                            //new TermsOrder() { Key = "_term", Order = Nest.SortOrder.Descending }
                            }
                        };
                        AggregationContainer agg;
                        if (pathParts.Count() <= 1)
                        {
                            if (facetInfo.SumByField != null && facetInfo.SumByField != "")
                            {
                                termsAgg.Aggregations = new SumAggregation(facetInfo.SumByField, facetInfo.SumByField);
                            }
                            agg = termsAgg;
                        }
                        else
                        {
                            var nestedPath = string.Join(".", pathParts.Take(pathParts.Length - 1));

                            var reverseNestedAgg = new ReverseNestedAggregation(facetInfo.FacetName);

                            if (facetInfo.SumByField != null && facetInfo.SumByField != "")
                            {
                                reverseNestedAgg.Aggregations = new SumAggregation(facetInfo.SumByField, facetInfo.SumByField);
                            }

                            var nestedQueryStrings = ConstructNestedQueryStrings<T>(searchRequest.Filters);
                            var query = nestedQueryStrings.ContainsKey(nestedPath) ? nestedQueryStrings[nestedPath] : "*";

                            termsAgg.Aggregations = new FilterAggregation(facetInfo.FacetName)
                            {
                                Filter = new QueryStringQuery()
                                {
                                    Query = query
                                },
                                Aggregations = reverseNestedAgg
                            };

                            agg = new NestedAggregation(facetInfo.FacetName)
                            {
                                Path = nestedPath,
                                Aggregations = termsAgg
                            };
                        }

                        aggs.Add(
                            facetInfo.FacetName,
                            agg
                        );
                    }
                }
            }

            return new AggregationContainer() { Aggregations = aggs };
        }

        private IHighlight ConstructHighlight<T>(HighlightDescriptor<T> highlightDescriptor) where T : class
        {
            var searchableProperties = SearchableModelAttribute.GetSearchablePropertyNames<T>();

            Dictionary<Field, IHighlightField> fields = new Dictionary<Field, IHighlightField>();

            foreach (var p in searchableProperties)
            {
                fields.Add(p, new HighlightField
                {
                    //Type = HighlighterType.Plain,
                    //ForceSource = true,
                    //FragmentSize = 150,
                    //Fragmenter = HighlighterFragmenter.Span,
                    //NumberOfFragments = 0,
                    //NoMatchSize = 150
                });
            }

            Highlight highlight = new Highlight
            {
                PreTags = new[] { "<span class=\"highlight\">" },
                PostTags = new[] { "</span>" },
                NumberOfFragments = 0,
                Encoder = HighlighterEncoder.Html,
                Fields = fields
            };

            return highlight;
        }

        private string ConstructOneConditionFilter<T>(Filter filter)
        {
            if (string.IsNullOrEmpty(filter.PropertyName))
            {
                return "";
            }

            var filterOperator = "";
            switch (filter.Operator)
            {
                case FilterOperator.NotEqual:
                case FilterOperator.Equal:
                    filterOperator = ":";
                    break;
                case FilterOperator.Greater:
                    filterOperator = ":>";
                    break;
                case FilterOperator.GreaterOrEqual:
                    filterOperator = ":>=";
                    break;
                case FilterOperator.Lower:
                    filterOperator = ":<";
                    break;
                case FilterOperator.LowerOrEqual:
                    filterOperator = ":<=";
                    break;
            }

            var filterValue = "";
            switch (SearchableModelAttribute.GetPropertyPathTypeCode<T>(filter.PropertyName))
            {
                case TypeCode.DateTime:
                    filterOperator = ":";
                    var dateFilterValue = filter.Value == null ? "null" : ((DateTime)filter.Value).ToString("o");
                    switch (filter.Operator)
                    {
                        case FilterOperator.NotEqual:
                        case FilterOperator.Equal:
                            filterValue = $"\"{dateFilterValue}\"";
                            break;
                        case FilterOperator.Greater:
                            filterValue = $"{{{dateFilterValue} TO *}}";
                            break;
                        case FilterOperator.GreaterOrEqual:
                            filterValue = $"[{dateFilterValue} TO *]";
                            break;
                        case FilterOperator.Lower:
                            filterValue = $"{{* TO {dateFilterValue}}}";
                            break;
                        case FilterOperator.LowerOrEqual:
                            filterValue = $"[* TO {dateFilterValue}]";
                            break;
                    }
                    break;
                case TypeCode.Char:
                case TypeCode.String:
                    filterValue = $"\"{filter.Value}\"";
                    break;
                //case TypeCode.Decimal:
                //case TypeCode.Double:
                //case TypeCode.Int16:
                //case TypeCode.Int32:
                //case TypeCode.Int64:
                //case TypeCode.Byte:
                //case TypeCode.Boolean:
                default:
                    filterValue = filter.Value == null ? "null" : filter.Value.ToString().ToLower();
                    break;
            }

            var condition = $"{filter.PropertyName}{filterOperator}{filterValue}";
            if (filter.Value == null)
            {
                condition = $"({condition} OR (!(_exists_:{filter.PropertyName})))";
            }
            if (filter.Operator == FilterOperator.NotEqual)
            {
                return $"!({condition})";
            }
            return condition;
        }

        private string ConstructConditionFilter<T>(Filter filter)
        {
            var q = ConstructOneConditionFilter<T>(filter);

            foreach (FilterConnector f in filter.Filters)
            {
                if (!string.IsNullOrEmpty(q) && f.Logic != null)
                {
                    q += $" {f.Logic.ToUpper()} ";
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

        public Task<List<SuggestionResultRecord<T>>> Suggest<T>(string searchText, string suggesterName, bool fuzzy = true)
        {
            throw new NotImplementedException();
        }
    }
}
