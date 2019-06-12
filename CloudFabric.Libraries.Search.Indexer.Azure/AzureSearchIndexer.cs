using CloudFabric.Libraries.Search.Attributes;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CloudFabric.Libraries.Search.Indexer.Azure
{
    public class AzureSearchIndexer : ISearchIndexer
    {
        private SearchServiceClient _serviceClient;

        public AzureSearchIndexer(string searchServiceName, string apiKey)
        {
            _serviceClient = new SearchServiceClient(searchServiceName, new SearchCredentials(apiKey));
        }

        public async Task CreateSynonymMaps(Dictionary<string, List<string>> synonymMaps)
        {
            foreach (var mapName in synonymMaps.Keys)
            {
                await _serviceClient.SynonymMaps.CreateOrUpdateAsync(new SynonymMap(mapName, string.Join("\n", synonymMaps[mapName])));
            }
        }

        public async Task<bool> CreateIndex<T>(string forcedNewIndexName = null) where T : class
        {
            try
            {
                var indexName = SearchableModelAttribute.GetIndexName(typeof(T));

                if (string.IsNullOrEmpty(indexName))
                {
                    throw new Exception("Model class should have SearchableModelAttribute with indexName specified.");
                }

                List<ScoringProfile> scoringProfiles = null;
                string defaultScoringProfile = null;

                try
                {
                    var existingIndexNames = await _serviceClient.Indexes.ListNamesAsync();

                    var foundIndexName = "";
                    long foundIndexVersion = 0;
                    var indexMatcher = new Regex(indexName + "-(?<timestamp>\\d{12})+");
                    // find last index 
                    foreach (var existingIndexName in existingIndexNames)
                    {
                        var match = indexMatcher.Match(existingIndexName);

                        if (match.Success)
                        {
                            var timestampGroup = new List<Group>(match.Groups).FirstOrDefault(g => g.Name == "timestamp");

                            if (timestampGroup != null && timestampGroup.Success && timestampGroup.Value != null && timestampGroup.Value.Length > 0)
                            {
                                var version = long.Parse(timestampGroup.Value);

                                if (version > foundIndexVersion)
                                {
                                    foundIndexName = existingIndexName;
                                    foundIndexVersion = version;
                                }
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(foundIndexName))
                    {
                        Console.WriteLine("Unable to find last index version for index: " + indexName + ". New index will be created.");
                        foundIndexName = indexName;
                    }
                    else
                    {
                        Console.WriteLine("Found last index version: " + foundIndexName);
                    }

                    var existingIndex = await _serviceClient.Indexes.GetAsync(foundIndexName);
                    scoringProfiles = (List<ScoringProfile>)existingIndex.ScoringProfiles;

                    Console.WriteLine("ScoringProfiles:");
                    Console.Write(JsonConvert.SerializeObject(scoringProfiles, Formatting.Indented));
                    string mydocpath =
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                    var path = mydocpath + @"\AzureSearch_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss") + "_scoringProfilesBackup.json";
                    using (StreamWriter outputFile = new StreamWriter(path))
                    {
                        outputFile.Write(JsonConvert.SerializeObject(scoringProfiles, Formatting.Indented));
                    }

                    defaultScoringProfile = existingIndex.DefaultScoringProfile;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                List<Field> fields = new List<Field>();
                List<Suggester> suggesters = new List<Suggester>();

                PropertyInfo[] props = typeof(T).GetProperties();
                foreach (PropertyInfo prop in props)
                {
                    object[] attrs = prop.GetCustomAttributes(true);
                    foreach (object attr in attrs)
                    {
                        SearchablePropertyAttribute propertyAttribute = attr as SearchablePropertyAttribute;

                        if (propertyAttribute != null)
                        {
                            var field = ConstructFieldForProperty(prop, propertyAttribute);
                            
                            fields.Add(field);

                            if (propertyAttribute.UseForSuggestions)
                            {
                                var suggester = new Suggester { Name = field.Name, SourceFields = new[] { field.Name } };

                                suggesters.Add(suggester);
                            }
                        }
                    }
                }

                string newIndexVersionSuffix = DateTime.Now.ToString("yyyyMMddHHmmss");
                string newIndexName = indexName + "-" + newIndexVersionSuffix;

                var definition = new Index()
                {
                    Name = newIndexName,
                    Fields = fields,
                    Suggesters = suggesters,
                    ScoringProfiles = scoringProfiles,
                    DefaultScoringProfile = defaultScoringProfile
                };

                await _serviceClient.Indexes.CreateOrUpdateAsync(definition);

                Console.WriteLine($"Created index " + definition.Name);

                //await _serviceClient.Indexes.DeleteAsync(indexName);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error creating index: {0}\r\n", ex.Message);
            }

            return true;
        }

        public Field ConstructFieldForProperty(PropertyInfo prop, SearchablePropertyAttribute propertyAttribute)
        {
            Type propertyType = prop.PropertyType;

            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                propertyType = Nullable.GetUnderlyingType(propertyType);
            }

            Field field = null;
            var fieldName = prop.Name;
            

            if (propertyAttribute.IsNested)
            {
                var nestedFields = new List<Field>();

                var isList = propertyType.GetTypeInfo().IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>);

                if (isList)
                {
                    propertyType = propertyType.GetGenericArguments()[0];
                }

                PropertyInfo[] nestedProps = propertyType.GetProperties();
                foreach (PropertyInfo nestedProp in nestedProps)
                {
                    object[] attrs = nestedProp.GetCustomAttributes(true);
                    foreach (object attr in attrs)
                    {
                        SearchablePropertyAttribute nestedPropertyAttribute = attr as SearchablePropertyAttribute;

                        if (propertyAttribute != null)
                        {
                            var nestedField = ConstructFieldForProperty(nestedProp, nestedPropertyAttribute);

                            nestedFields.Add(nestedField);
                        }
                    }
                }

                if (isList)
                {
                    field = new Field(fieldName, DataType.Collection(DataType.Complex), nestedFields);
                }
                else
                {
                    field = new Field(fieldName, DataType.Complex, nestedFields);
                }
            }
            else
            {
                DataType? fieldType = null;

                switch (Type.GetTypeCode(propertyType))
                {
                    case TypeCode.Int32:
                        fieldType = DataType.Int32;
                        break;
                    case TypeCode.Int64:
                        fieldType = DataType.Int64;
                        break;
                    case TypeCode.Single:
                    case TypeCode.Decimal:
                    case TypeCode.Double:
                        fieldType = DataType.Double;
                        break;
                    case TypeCode.Boolean:
                        fieldType = DataType.Boolean;
                        break;
                    case TypeCode.String:
                        fieldType = DataType.String;
                        break;
                    case TypeCode.Object:
                        var elementType = propertyType.GetElementType();
                        if (Type.GetTypeCode(elementType) != TypeCode.String)
                        {
                            throw new Exception("Unsupported array element type!");
                        }
                        fieldType = DataType.Collection(DataType.String);

                        break;
                    case TypeCode.DateTime:
                        fieldType = DataType.DateTimeOffset;
                        break;
                    default:
                        throw new Exception($"Azure Search doesn't support {propertyType.Name} type.");
                }

                field = new Field(fieldName, fieldType.GetValueOrDefault());
            }

            if (propertyAttribute.Analyzer != null && propertyAttribute.Analyzer != "")
            {
                field.Analyzer = propertyAttribute.Analyzer;
            }
            //SearchAnalyzer & IndexAnalyzer should be specified together
            if (!string.IsNullOrWhiteSpace(propertyAttribute.SearchAnalyzer) && !string.IsNullOrWhiteSpace(propertyAttribute.IndexAnalyzer))
            {
                field.SearchAnalyzer = propertyAttribute.SearchAnalyzer;
                field.IndexAnalyzer = propertyAttribute.IndexAnalyzer;
            }
            else if ((string.IsNullOrWhiteSpace(propertyAttribute.SearchAnalyzer) && !string.IsNullOrWhiteSpace(propertyAttribute.IndexAnalyzer))
                    || (!string.IsNullOrWhiteSpace(propertyAttribute.SearchAnalyzer) && string.IsNullOrWhiteSpace(propertyAttribute.IndexAnalyzer))
                    )
            {
                throw new Exception($"Both SearchAnalyzer & IndexAnalyzer are should be specified together.");
            }

            if(propertyAttribute.IsNested == false)
            {
                field.IsKey = propertyAttribute.IsKey;
                field.IsRetrievable = propertyAttribute.IsRetrievable;
                field.IsFilterable = propertyAttribute.IsFilterable;
                field.IsSortable = propertyAttribute.IsSortable;
                field.IsSearchable = propertyAttribute.IsSearchable;
                field.IsFacetable = propertyAttribute.IsFacetable;
            }

            if (propertyAttribute.SynonymMaps.Length > 0)
            {
                field.SynonymMaps = propertyAttribute.SynonymMaps;
            }

            return field;
        }
    }
}
