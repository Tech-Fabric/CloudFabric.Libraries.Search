using CloudFabric.Libraries.Search.Attributes;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloudFabric.Libraries.Search.Harvester.Azure
{
    public class AzureSearchUploader : ISearchUploader
    {
        private SearchServiceClient _serviceClient;

        private Dictionary<string, ISearchIndexClient> _indexClients = new Dictionary<string, ISearchIndexClient>();
        private TelemetryClient _telemetryClient;

        /// <summary>
        /// Allows remapping indexes stored on SearchAttributes to new names.
        /// </summary>
        private Dictionary<string, string> _indexMapping;

        public AzureSearchUploader(TelemetryClient telemetryClient, string searchServiceName, string apiKey, Dictionary<string, string> indexMapping = null)
        {
            _telemetryClient = telemetryClient;
            _serviceClient = new SearchServiceClient(searchServiceName, new SearchCredentials(apiKey));

            if (indexMapping == null)
            {
                _indexMapping = new Dictionary<string, string>();
            }
            else
            {
                _indexMapping = indexMapping;
            }
        }

        public async Task<SearchUploadResult> Upload<T>(IEnumerable<T> records) where T : class
        {
            using (var operation = _telemetryClient.StartOperation<RequestTelemetry>("uploadSearchRecords"))
            {
                var indexName = SearchableModelAttribute.GetIndexName(typeof(T));

                if(indexName == null)
                {
                    throw new Exception($"Type {typeof(T).Name} does not have SearchableModelAttribute");
                }

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
                    indexClient = _serviceClient.Indexes.GetClient(indexName);
                    _indexClients.Add(indexName, indexClient);
                }

                if (indexClient == null)
                {
                    throw new Exception("Failed to get indexClient. Make sure index exists.");
                }

                // We cannot just throw all 200k+ records to azure search, that causes out of memory and http payload too large exceptions.
                var recordsInOneStep = 200;
                var totalRecords = records.Count();

                var result = new SearchUploadResult()
                {
                    Success = true,
                    Uploaded = 0
                };

                while (result.Uploaded < totalRecords)
                {
                    var uploadSerializationStartTime = DateTime.UtcNow;

                    var recordsToUpload = records.Skip(result.Uploaded).Take(recordsInOneStep);

                    var actions = new List<IndexAction<T>>();

                    foreach (var r in recordsToUpload)
                    {
                        Dictionary<string, object> recordSerialized = r.GetType()
                            .GetProperties()
                            .ToDictionary(x => x.Name, x => x.GetValue(r));

                        var indexAction = IndexAction.MergeOrUpload(r);

                        actions.Add(indexAction);
                    }

                    var uploadSerializationTime = DateTime.UtcNow - uploadSerializationStartTime;
                    _telemetryClient.TrackMetric("azureSearchUploadChunkSerializationTime", uploadSerializationTime.TotalMilliseconds);

                    try
                    {
                        var uploadStartTime = DateTime.UtcNow;

                        indexClient.Documents.Index(IndexBatch.New(actions));

                        var uploadTime = DateTime.UtcNow - uploadStartTime;
                        _telemetryClient.TrackMetric("azureSearchChunkUploadTime", uploadTime.TotalMilliseconds);
                    }
                    catch (IndexBatchException e)
                    {
                        _telemetryClient.TrackException(e);

                        result.DebugInformation = e.Message;
                        result.Success = false;
                        return result;
                    }

                    actions.Clear();

                    result.Uploaded += recordsToUpload.Count();

                    //_logger.Info("Upload statistic:", new { UploadedRecords = uploadedRecords, TotalRecords = totalRecords, IndexName = indexName });
                }

                return result;
            }
        }

        public bool Delete<T>(IEnumerable<string> idList) where T : class
        {
            using (var operation = _telemetryClient.StartOperation<RequestTelemetry>("deleteSearchRecords"))
            {
                var indexName = SearchableModelAttribute.GetIndexName(typeof(T));
                string keyName = SearchableModelAttribute.GetKeyPropertyName<T>();

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
                    indexClient = _serviceClient.Indexes.GetClient(indexName);
                    _indexClients.Add(indexName, indexClient);
                }

                if (indexClient == null)
                {
                    throw new Exception("Failed to get indexClient. Make sure index exists.");
                }

                //Deleting 1000 records at a time using IndexBatch
                var recordsInOneStep = 1000;
                var totalRecords = idList.Count();
                var deletedRecords = 0;
                _telemetryClient.TrackMetric("azureSearchChunkDeleteItems", totalRecords);

                while (deletedRecords < totalRecords)
                {
                    var deleteStartTime = DateTime.UtcNow;

                    var recordsToDelete = idList.Skip(deletedRecords).Take(recordsInOneStep);

                    var indexBatchAction = IndexBatch.Delete(keyName, recordsToDelete);                                       

                    try
                    {
                        indexClient.Documents.Index(indexBatchAction);
                    }
                    catch (IndexBatchException e)
                    {
                        _telemetryClient.TrackException(e);
                        return false;
                    }
                    
                    var deleteTime = DateTime.UtcNow - deleteStartTime;

                    _telemetryClient.TrackMetric("azureSearchChunkDeleteTime", deleteTime.TotalMilliseconds);

                    deletedRecords += recordsToDelete.Count();

                }

                return true;
            }
        }

    }
}
