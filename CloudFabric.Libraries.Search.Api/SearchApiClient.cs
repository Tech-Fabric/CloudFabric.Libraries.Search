﻿using Microsoft.ApplicationInsights;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CloudFabric.Libraries.Search.Api
{
    public class SearchApiClient<ResultRecordT>
    {
        protected static HttpClient _httpClient;
        protected string _baseAddress;
        protected TelemetryClient _telemetryClient;

        public SearchApiClient(string baseAddress, HttpClient httpClient = null, TelemetryClient telemetryClient = null)
        {
            _baseAddress = baseAddress;
            _telemetryClient = telemetryClient;

            _httpClient = httpClient ?? new HttpClient();

            _httpClient.BaseAddress = new Uri(_baseAddress);
            _httpClient.DefaultRequestHeaders.Accept.TryParseAdd("application/json");
        }

        private async Task<string>
            PerformGetRequest(string query)
        {
            try
            {
                var result = await _httpClient.GetAsync(query);

                var response = await result.Content.ReadAsStringAsync();

                return response;
            }
            catch (Exception ex)
            {
                var exception = new Exception("Failed to get data from search service", ex);
                if (_telemetryClient != null)
                {
                    _telemetryClient.TrackException(
                        ex,
                        new Dictionary<string, string>() {
                            { "searchQuery", query },
                            { "searchBaseAddress", _httpClient.BaseAddress.ToString() }
                        }
                    );
                }

                throw ex;
            }
        }

        public async Task<SearchApiResponse<ResultRecordT>>
            GetById(string id = "")
        {
            var response = await PerformGetRequest("get_by_id?id=" + id);

            try
            {
                return JsonConvert.DeserializeObject<SearchApiResponse<ResultRecordT>>(response);
            }
            catch (Exception ex)
            {
                var exception = new Exception("Failed to deserialize search response", ex);
                if (_telemetryClient != null)
                {
                    _telemetryClient.TrackException(
                        ex,
                        new Dictionary<string, string>() {
                            { "searchForId", id },
                            { "searchBaseAddress", _httpClient.BaseAddress.ToString() },
                            { "searchResponse", response }
                        }
                    );
                }

                throw ex;
            }
        }

        public async Task<SearchApiResponse<ResultRecordT>>
            GetByIds(string[] ids)
        {
            if (ids == null || ids.Length < 1)
            {
                throw new ArgumentNullException("ids");
            }

            var response = await PerformGetRequest("get_by_ids?ids=" + string.Join(",", ids));

            try
            {
                return JsonConvert.DeserializeObject<SearchApiResponse<ResultRecordT>>(response);
            }
            catch (Exception ex)
            {
                var exception = new Exception("Failed to deserialize search response", ex);
                if (_telemetryClient != null)
                {
                    _telemetryClient.TrackException(
                        ex,
                        new Dictionary<string, string>() {
                            { "searchForIds", string.Join(",", ids) },
                            { "searchBaseAddress", _httpClient.BaseAddress.ToString() },
                            { "searchResponse", response }
                        }
                    );
                }

                throw ex;
            }
        }

        public async Task<SearchApiResponse<ResultRecordT>> Search(SearchApiRequest request)
        {
            var dataAsString = JsonConvert.SerializeObject(request);

            var content = new StringContent(dataAsString, Encoding.UTF8, "application/json");

            var result = await _httpClient.PostAsync("search", content);

            var response = await result.Content.ReadAsStringAsync();

            if (result.StatusCode != HttpStatusCode.OK && result.StatusCode != HttpStatusCode.Accepted && result.StatusCode != HttpStatusCode.Created)
            {
                throw new Exception($"Search error. Status code: {result.StatusCode}. Response content: {response}");
            }

            try
            {
                return JsonConvert.DeserializeObject<SearchApiResponse<ResultRecordT>>(response);
            }
            catch (Exception ex)
            {
                var exception = new Exception("Failed to deserialize search response", ex);
                if (_telemetryClient != null)
                {
                    _telemetryClient.TrackException(
                        ex,
                        new Dictionary<string, string>() {
                            { "searchRequest", dataAsString },
                            { "searchBaseAddress", _httpClient.BaseAddress.ToString() },
                            { "searchResponse", response }
                        }
                    );
                }

                throw ex;
            }
        }
    }
}
