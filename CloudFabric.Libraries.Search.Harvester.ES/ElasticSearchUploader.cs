using Nest;
using CloudFabric.Libraries.Search.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloudFabric.Libraries.Search.Harvester.ES
{
    public class ElasticSearchUploader : ISearchUploader
    {
        private ElasticClient _client = null;

        /// <summary>
        /// Allows remapping indexes stored on SearchAttributes to new names.
        /// </summary>
        private Dictionary<Type, string> _indexMapping;

        public ElasticSearchUploader(string uri, string username, string password, Dictionary<Type, string> indexMapping = null)
        {
            var _connectionSettings = new ConnectionSettings(new Uri(uri));
            _connectionSettings.BasicAuthentication(username, password);
            _connectionSettings.DefaultFieldNameInferrer(p => p);

            _client = new ElasticClient(_connectionSettings);

            _indexMapping = indexMapping == null ? new Dictionary<Type, string>() : indexMapping;

            foreach (var mapping in _indexMapping)
            {
                _client.ConnectionSettings.IdProperties.TryAdd(mapping.Key, SearchablePropertyAttribute.GetKeyPropertyName(mapping.Key));
            }
        }

        public async Task<SearchUploadResult> Upload<T>(IEnumerable<T> records) where T : class
        {
            if (!_client.ConnectionSettings.IdProperties.ContainsKey(typeof(T)))
            {
                _client.ConnectionSettings.IdProperties.TryAdd(typeof(T), SearchablePropertyAttribute.GetKeyPropertyName<T>());
            }

            // We cannot just throw all 200k+ records to azure search, that causes out of memory and http payload too large exceptions.
            var recordsInOneStep = 1000;
            var totalRecords = records.Count();

            var indexName = _indexMapping[typeof(T)];

            var result = new SearchUploadResult()
            {
                Success = true,
                Uploaded = 0
            };

            while (result.Uploaded < totalRecords)
            {
                var recordsToUpload = records.Skip(result.Uploaded).Take(recordsInOneStep);

                try
                {
                    var response = await _client.IndexManyAsync(recordsToUpload, indexName);

                    result.DebugInformation = response.DebugInformation;
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to index some of the documents.");

                    result.DebugInformation = e.Message;
                    result.Success = false;
                    return result;
                }

                result.Uploaded += recordsToUpload.Count();

                Console.WriteLine($"{result.Uploaded}/{totalRecords} uploaded...");
            }

            return result;
        }
        public bool Delete<T>(IEnumerable<string> idList) where T : class
        {
            return true;
        }
    }
}
