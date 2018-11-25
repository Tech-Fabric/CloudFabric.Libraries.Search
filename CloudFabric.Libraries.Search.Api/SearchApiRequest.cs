using System.Collections.Generic;
using System.ComponentModel;

namespace CloudFabric.Libraries.Search.Api
{
    public class SearchApiRequest
    {
        [DefaultValue(0)]
        public int Offset { get; set; } = 0;

        [DefaultValue(50)]
        public int Limit { get; set; } = 50;

        [DefaultValue("*")]
        public string SearchText { get; set; } = "*";

        [DefaultValue("")]
        public string OrderBy { get; set; }

        public List<Filter> Filters { get; set; } = new List<Filter>();
        public List<FacetInfoRequest> FacetInfoToReturn = new List<FacetInfoRequest>();
    }
}
