using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloudFabric.Libraries.Search.Harvester
{
    public class SearchUploadResult
    {
        public bool Success { get; set; }
        public string DebugInformation { get; set; }
        public int Uploaded { get; set; }
    }

    public interface ISearchUploader
    {
        Task<SearchUploadResult> Upload<T>(IEnumerable<T> records) where T : class;

        bool Delete<T>(IEnumerable<string> idList) where T : class;
    }
}
