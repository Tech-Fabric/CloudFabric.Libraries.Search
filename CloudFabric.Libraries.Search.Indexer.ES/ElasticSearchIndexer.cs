﻿using Nest;
using CloudFabric.Libraries.Search.Attributes;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CloudFabric.Libraries.Search.Indexer.ES
{
    public class ElasticSearchIndexer : ISearchIndexer
    {
        private ElasticClient _client = null;

        public ElasticSearchIndexer(string uri, string username, string password, bool debug = false)
        {
            var _connectionSettings = new ConnectionSettings(new Uri(uri));
            _connectionSettings.BasicAuthentication(username, password);
            _connectionSettings.DefaultFieldNameInferrer(p => p);
            if (debug)
            {
                _connectionSettings.EnableDebugMode();
            }

            _client = new ElasticClient(_connectionSettings);
        }

        public async Task CreateSynonymMaps(Dictionary<string, List<string>> synonymMaps)
        {
            throw new NotImplementedException();
        }

        public async Task<string> ListIndices()
        {
            var response = await _client.Cat.IndicesAsync();

            var output = JsonConvert.SerializeObject(response.Records);

            return output;
        }

        public async Task DeleteIndex(string indexName)
        {
            await _client.Indices.DeleteAsync(indexName);
        }

        public async Task<string> CreateIndex<T>(string forcedNewIndexName) where T : class
        {
            try
            {
                if (!_client.ConnectionSettings.IdProperties.ContainsKey(typeof(T)))
                {
                    _client.ConnectionSettings.IdProperties.Add(typeof(T), SearchablePropertyAttribute.GetKeyPropertyName<T>());
                }

                if (string.IsNullOrWhiteSpace(forcedNewIndexName))
                {
                    throw new Exception($"Missing required parameter: {nameof(forcedNewIndexName)}");
                }

                var response = await _client.Indices.ExistsAsync(
                    new IndexExistsRequest(forcedNewIndexName)
                );

                if (!response.Exists)
                {
                    var descriptor = new CreateIndexDescriptor(forcedNewIndexName)
                        .Settings(s => s
                            .Analysis(analysis => analysis
                                .Analyzers(analyzers => analyzers
                                    .Custom("folding-analyzer", c => c
                                        .Tokenizer("standard")
                                        .Filters("lowercase", "asciifolding")
                                    )
                                    .Custom("phone-number", c => c
                                        .Tokenizer("keyword")
                                        .Filters("us-phone-number", "ten-digits-min")
                                        .CharFilters("digits-only")
                                    )
                                    .Custom("phone-number-search", c => c
                                        .Tokenizer("keyword")
                                        .Filters("not-empty")
                                        .CharFilters("alphanum-only")
                                    )
                                    .Custom("keyword-custom", c => c
                                        .Tokenizer("keyword")
                                        .Filters("lowercase")
                                    )
                                )
                                .CharFilters(charFilters => charFilters
                                    .PatternReplace("digits-only", p => p.Pattern("[^\\d]").Replacement(""))
                                    .PatternReplace("alphanum-only", p => p.Pattern("[^a-zA-Z\\d]").Replacement(""))
                                )
                                .TokenFilters(filters => filters
                                    .PatternCapture("us-phone-number", f => f.Patterns("1?(1)(\\d*)").PreserveOriginal())
                                    .Length("ten-digits-min", f => f.Min(10))
                                    .Length("not-empty", f => f.Min(1))
                                )
                            )
                            .Setting("max_result_window", 1000000)
                        );

                    var createIndexResponse = await _client.Indices.CreateAsync(descriptor);
                    if (createIndexResponse.Acknowledged)
                    {
                        Console.WriteLine($"Created index " + forcedNewIndexName);
                    }
                    else
                    {
                        var message = $"Index creation failed for index {forcedNewIndexName}. DebugInformation: ${createIndexResponse.DebugInformation}!";
                        Console.WriteLine(message);
                        throw new Exception(message);
                    }
                }

                var properties = GetPropertiesDescriptors<T>();

                var putMappingRequest = new PutMappingRequest(forcedNewIndexName)
                {
                    Properties = properties
                };
                await _client.MapAsync(putMappingRequest);
                Console.WriteLine($"Updated mapping for index " + forcedNewIndexName);
                return forcedNewIndexName;
            }
            catch (Exception ex)
            {                
                Console.WriteLine("Error creating index: {0}\r\n", ex.Message);
                throw;
            }
        }

        private IProperties GetPropertiesDescriptors<T>() where T : class
        {
            var properties = new PropertiesDescriptor<T>();

            PropertyInfo[] props = typeof(T).GetProperties();
            foreach (PropertyInfo prop in props)
            {
                object[] attrs = prop.GetCustomAttributes(true);
                foreach (object attr in attrs)
                {
                    SearchablePropertyAttribute propertyAttribute = attr as SearchablePropertyAttribute;
                    if (propertyAttribute == null)
                    {
                        continue;
                    }

                    Type propertyType = prop.PropertyType;

                    if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                    {
                        propertyType = Nullable.GetUnderlyingType(propertyType);
                    }
                    if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        propertyType = propertyType.GetMethod("get_Item").ReturnType;
                    }

                    switch (Type.GetTypeCode(propertyType))
                    {
                        case TypeCode.Int32:
                        case TypeCode.Int64:
                        case TypeCode.Double:
                            properties = properties.Number(p =>
                            {
                                return p.Name(prop.Name);
                            });
                            break;
                        case TypeCode.Boolean:
                            properties = properties.Boolean(p =>
                            {
                                return p.Name(prop.Name);
                            });
                            break;
                        case TypeCode.String:
                            if (propertyAttribute.IsSearchable)
                            {
                                var analyzer = string.IsNullOrEmpty(propertyAttribute.Analyzer) ? "standard" : propertyAttribute.Analyzer;
                                var searchAnalyzer = string.IsNullOrEmpty(propertyAttribute.SearchAnalyzer) ? analyzer : propertyAttribute.SearchAnalyzer;

                                properties = properties.Keyword(p =>
                                {
                                    return p
                                        .Name(prop.Name)
                                        .Fields(f => f
                                            .Text(ss => ss
                                                .Name("folded")
                                                .Analyzer("folding-analyzer")
                                                .Boost(propertyAttribute.SearchableBoost)
                                            )
                                            .Text(ss => ss
                                                .Name("text")
                                                .Analyzer(analyzer)
                                                .SearchAnalyzer(searchAnalyzer)
                                                .Boost(propertyAttribute.SearchableBoost)
                                            )
                                        )
                                        .Boost(propertyAttribute.SearchableBoost);
                                });
                            }
                            else
                            {
                                properties = properties.Keyword(p => p.Name(prop.Name));
                            }
                            break;
                        case TypeCode.DateTime:
                            properties = properties.Date(p =>
                            {
                                return p.Name(prop.Name);
                            });
                            break;
                        case TypeCode.Object:
                            //if (propertyAttribute.IsNested)
                            //{
                            properties = properties.Nested<object>(p => p.Name(prop.Name));
                            //}
                            break;
                        default:
                            throw new Exception(
                                $"Elastic Search doesn't support {prop.PropertyType.Name} type. TypeCode: {Type.GetTypeCode(prop.PropertyType)}"
                            );
                    }
                }
            }

            return ((IPromise<IProperties>)properties).Value;
        }
    }
}
