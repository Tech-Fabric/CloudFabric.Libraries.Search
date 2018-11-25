using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace CloudFabric.Libraries.Search.Api
{
    public class SearchApiClient<ResultRecordT>
    {
        protected static HttpClient _httpClient;
        protected string _baseAddress;

        public SearchApiClient(string baseAddress, HttpClient httpClient = null)
        {
            _baseAddress = baseAddress;

            if (httpClient != null)
            {
                _httpClient = httpClient;
            }
            else
            {
                _httpClient = new HttpClient();
            }

            _httpClient.BaseAddress = new Uri(_baseAddress);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private async Task<string>
            PerformGetRequest(string query)
        {
            var result = await _httpClient.GetAsync(query);

            var response = await result.Content.ReadAsStringAsync();

            return response;
        }

        public async Task<SearchApiResponse<ResultRecordT>>
            GetById(string id = "")
        {
            var response = await PerformGetRequest("get_by_id?id=" + id);

            return JsonConvert.DeserializeObject<SearchApiResponse<ResultRecordT>>(response);
        }

        public async Task<SearchApiResponse<ResultRecordT>>
            GetByIds(string[] ids)
        {
            if (ids == null || ids.Length < 1)
            {
                throw new ArgumentNullException();
            }

            var response = await PerformGetRequest("get_by_ids?ids=" + string.Join(",", ids));

            return JsonConvert.DeserializeObject<SearchApiResponse<ResultRecordT>>(response);
        }

        public async Task<SearchApiResponse<ResultRecordT>> Search(SearchApiRequest request)
        {
            var dataAsString = JsonConvert.SerializeObject(request);

            var content = new StringContent(dataAsString, Encoding.UTF8, "application/json");

            var result = await _httpClient.PostAsync("search", content);

            var response = await result.Content.ReadAsStringAsync();

            return JsonConvert.DeserializeObject<SearchApiResponse<ResultRecordT>>(response);
        }
    }
}
