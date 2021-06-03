using Elasticsearch.Net;
using System;
using System.Collections.Generic;
using System.Text;

namespace CloudFabric.Libraries.Search.Services.ES.Exceptions
{
    public class ElasticSearchQueryException : Exception
    {
        public ServerError ServerError { get; }              

        public ElasticSearchQueryException(ServerError serverError, string message) : base(message)
        {
            ServerError = serverError;                        
        }
    }
}
