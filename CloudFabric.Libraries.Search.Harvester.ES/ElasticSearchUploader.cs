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
        private Dictionary<string, string> _indexMapping;

        public ElasticSearchUploader(string uri, string username, string password, Dictionary<string, string> indexMapping = null)
        {
            var _connectionSettings = new ConnectionSettings(new Uri(uri));
            _connectionSettings.BasicAuthentication(username, password);
            _connectionSettings.DefaultFieldNameInferrer(p => p);

            _client = new ElasticClient(_connectionSettings);

            _indexMapping = indexMapping == null ? new Dictionary<string, string>() : indexMapping;
        }

        public async Task<bool> Upload<T>(IEnumerable<T> records) where T : class
        {
            if (!_client.ConnectionSettings.IdProperties.ContainsKey(typeof(T)))
            {
                _client.ConnectionSettings.IdProperties.Add(typeof(T), SearchableModelAttribute.GetKeyPropertyName<T>());
            }

            // We cannot just throw all 200k+ records to azure search, that causes out of memory and http payload too large exceptions.
            var recordsInOneStep = 1000;
            var totalRecords = records.Count();
            var uploadedRecords = 0;

            var indexType = SearchableModelAttribute.GetIndexName(typeof(T));
            var indexName = _indexMapping[indexType];

            while (uploadedRecords < totalRecords)
            {
                var recordsToUpload = records.Skip(uploadedRecords).Take(recordsInOneStep);

                try
                {
                    await _client.IndexManyAsync(recordsToUpload, indexName);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to index some of the documents.");

                    return false;
                }

                uploadedRecords += recordsToUpload.Count();

                Console.WriteLine($"{uploadedRecords}/{totalRecords} uploaded...");
            }

            return true;
        }
        public bool Delete<T>(IEnumerable<string> idList) where T : class
        {
            return true;
        }
    }
}
