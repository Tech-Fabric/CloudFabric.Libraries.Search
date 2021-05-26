using Elasticsearch.Net;
using System;
using System.Collections.Generic;
using System.Text;

namespace CloudFabric.Libraries.Search.Services.ES.Exceprions
{
    public class ElasticSearchQueryException : Exception
    {
        public ServerError ServerError { get; }

        public Exception OriginalException { get; }
        

        public ElasticSearchQueryException(ServerError serverError, Exception originalException, string message) : base(message)
        {
            ServerError = serverError;
            OriginalException = originalException;            
        }
    }
}
