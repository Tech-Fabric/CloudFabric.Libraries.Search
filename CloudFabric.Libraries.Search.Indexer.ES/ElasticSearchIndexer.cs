using Nest;
using CloudFabric.Libraries.Search.Attributes;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace CloudFabric.Libraries.Search.Indexer.ES
{
    public class ElasticSearchIndexer : ISearchIndexer
    {
        private ElasticClient _client = null;

        public ElasticSearchIndexer(string uri, string username, string password)
        {
            var _connectionSettings = new ConnectionSettings(new Uri(uri));
            _connectionSettings.BasicAuthentication(username, password);
            _connectionSettings.DefaultFieldNameInferrer(p => p);

            _client = new ElasticClient(_connectionSettings);
        }

        public async Task CreateSynonymMaps(Dictionary<string, List<string>> synonymMaps)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> CreateIndex<T>(string newIndexName = null) where T : class
        {
            try
            {
                if (!_client.ConnectionSettings.IdProperties.ContainsKey(typeof(T)))
                {
                    _client.ConnectionSettings.IdProperties.Add(typeof(T), SearchableModelAttribute.GetKeyPropertyName<T>());
                }

                if (newIndexName == null)
                {
                    var indexName = SearchableModelAttribute.GetIndexName(typeof(T));

                    if (string.IsNullOrEmpty(indexName))
                    {
                        throw new Exception("Model class should have SearchableModelAttribute with indexName specified.");
                    }

                    string newIndexVersionSuffix = DateTime.Now.ToString("yyyyMMddHHmmss");

                    newIndexName = indexName + "-" + newIndexVersionSuffix;

                    var response = await _client.IndexExistsAsync(
                        new IndexExistsRequest(newIndexName)
                    );
                    if (response.Exists)
                    {
                        await _client.DeleteIndexAsync(
                            new DeleteIndexRequest(newIndexName)
                        );
                        Console.WriteLine($"Deleted index " + newIndexName);
                    }

                    var descriptor = new CreateIndexDescriptor(newIndexName)
                        .Settings(s => s
                            .Analysis(analysis => analysis
                            .Analyzers(analyzers => analyzers
                                .Custom("folding-analyzer", c => c
                                    .Tokenizer("standard")
                                    .Filters("lowercase", "asciifolding")
                                )
                            )
                        ));

                    await _client.CreateIndexAsync(descriptor);
                    Console.WriteLine($"Created index " + newIndexName);
                }

                var properties = GetPropertiesDescriptors<T>();

                var putMappingRequest = new PutMappingRequest(newIndexName)
                {
                    Properties = properties
                };
                await _client.MapAsync(putMappingRequest);
                Console.WriteLine($"Updated mapping for index " + newIndexName);
            }
            catch (Exception ex)
            {
                return false;
            }

            return true;
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
                    if (propertyAttribute != null)
                    {
                        Type propertyType = prop.PropertyType;

                        if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                        {
                            propertyType = Nullable.GetUnderlyingType(propertyType);
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
                                                    .Analyzer("standard")
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
                            default:
                                throw new Exception(
                                    $"Elastic Search doesn't support {prop.PropertyType.Name} type. TypeCode: {Type.GetTypeCode(prop.PropertyType)}"
                                );
                        }
                    }
                }
            }

            return ((IPromise<IProperties>)properties).Value;
        }
    }
}
