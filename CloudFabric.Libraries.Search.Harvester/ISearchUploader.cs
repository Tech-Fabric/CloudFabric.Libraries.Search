using System;
using System.Collections.Generic;

namespace CloudFabric.Libraries.Search.Harvester
{
    public interface ISearchUploader
    {
        bool Upload<T>(IEnumerable<T> records) where T : class;
    }
}
