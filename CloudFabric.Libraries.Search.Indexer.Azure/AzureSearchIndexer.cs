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
                await _serviceClient.SynonymMaps.CreateOrUpdateAsync(new SynonymMap(mapName, SynonymMapFormat.Solr, string.Join("\n", synonymMaps[mapName])));
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
                            Type propertyType = prop.PropertyType;

                            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                            {
                                propertyType = Nullable.GetUnderlyingType(propertyType);
                            }

                            var field = new Field();

                            field.Name = prop.Name;

                            switch (Type.GetTypeCode(propertyType))
                            {
                                case TypeCode.Int32:
                                    field.Type = DataType.Int32;
                                    break;
                                case TypeCode.Int64:
                                    field.Type = DataType.Int64;
                                    break;
                                case TypeCode.Double:
                                    field.Type = DataType.Double;
                                    break;
                                case TypeCode.Boolean:
                                    field.Type = DataType.Boolean;
                                    break;
                                case TypeCode.String:
                                    field.Type = DataType.String;
                                    if (propertyAttribute.IsSearchable && !propertyAttribute.UseForSuggestions
                                        && string.IsNullOrWhiteSpace(propertyAttribute.SearchAnalyzer) && string.IsNullOrWhiteSpace(propertyAttribute.IndexAnalyzer)) 
                                        // Azure search doesn't support custom analyzer on fields enabled for suggestions
                                        // If Search & IndexAnalyzers are specified, we cannot set Analyzer
                                    {
                                        field.Analyzer = "standardasciifolding.lucene";
                                    }
                                    break;
                                case TypeCode.Object:
                                    var elementType = propertyType.GetElementType();
                                    if (Type.GetTypeCode(elementType) != TypeCode.String)
                                    {
                                        throw new Exception("Unsupported array element type!");
                                    }
                                    field.Type = DataType.Collection(DataType.String);
                                    if (propertyAttribute.IsSearchable && !propertyAttribute.UseForSuggestions) // Azure search doesn't support custom analyzer on fields enabled for suggestions
                                    {
                                        field.Analyzer = "standardasciifolding.lucene";
                                    }
                                    break;
                                case TypeCode.DateTime:
                                    field.Type = DataType.DateTimeOffset;
                                    break;
                                default:
                                    throw new Exception($"Azure Search doesn't support {propertyType.Name} type.");
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

                            field.IsKey = propertyAttribute.IsKey;
                            field.IsFilterable = propertyAttribute.IsFilterable;
                            field.IsSortable = propertyAttribute.IsSortable;
                            field.IsSearchable = propertyAttribute.IsSearchable;
                            field.IsFacetable = propertyAttribute.IsFacetable;

                            if (propertyAttribute.SynonymMaps.Length > 0)
                            {
                                field.SynonymMaps = propertyAttribute.SynonymMaps;
                            }

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
    }
}
