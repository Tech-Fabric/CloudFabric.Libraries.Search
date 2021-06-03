using Elasticsearch.Net;
using System;
using System.Collections.Generic;
using System.Text;

namespace CloudFabric.Libraries.Search.Services.ES.Exceptions
{
    public class ElasticSearchQueryException : Exception
    {
        public ServerError ServerError { get; }              

        public ElasticSearchQueryException(ServerError serverError, Exception originalException, string message) : base(message,originalException)
        {
            ServerError = serverError;                        
        }
    }
}
