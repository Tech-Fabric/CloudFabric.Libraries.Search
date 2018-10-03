using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace CloudFabric.Libraries.Search
{
    public static class SortOrder
    {
        public const string Asc = "asc";
        public const string Desc = "desc";
    }

    public static class FilterOperator
    {
        public const string Equal = "eq";
        public const string NotEqual = "ne";
        public const string Greater = "gt";
        public const string GreaterOrEqual = "ge";
        public const string Lower = "lt";
        public const string LowerOrEqual = "le";
    }

    public static class FilterLogic
    {
        public const string And = "and";
        public const string Or = "or";
    }

    public class Filter
    {
        public string PropertyName { get; set; }
        public string Operator { get; set; }
        public object Value { get; set; }

        /// <summary>
        /// Optional tag used for referencing this particular filter. Something like a filter name.
        /// </summary>
        public string Tag { get; set; }

        /// <summary>
        /// Whether this filter should be visible on UI (and could be removed).
        /// </summary>
        public bool Visible { get; set; } = true;

        public List<FilterConnector> Filters = new List<FilterConnector>();

        public Filter() { }

        /// <summary>
        /// Creates empty filter with specified tag. 
        /// </summary>
        /// <param name="tag"></param>
        public Filter(string tag)
        {
            this.Tag = tag;
        }

        public Filter(string propertyName, string oper, object value, string tag = "")
        {
            this.PropertyName = propertyName;
            this.Operator = oper;
            this.Value = value;
            this.Tag = tag;
        }

        public Filter(Filter filterToClone)
        {
            PropertyName = filterToClone.PropertyName;
            Operator = filterToClone.Operator;
            Value = filterToClone.Value;
            Tag = filterToClone.Tag;

            Filters = filterToClone.Filters.Select(f => new FilterConnector(f)).ToList();
        }

        public Filter Or(string propertyName, string oper, object value)
        {
            var filter = new Filter(propertyName, oper, value);
            return Or(filter);
        }

        public Filter Or(Filter f)
        {
            var connector = new FilterConnector() { Logic = FilterLogic.Or, Filter = f };
            Filters.Add(connector);
            return this;
        }

        public Filter And(string propertyName, string oper, object value)
        {
            var filter = new Filter(propertyName, oper, value);
            return And(filter);
        }

        public Filter And(Filter f)
        {
            var connector = new FilterConnector() { Logic = FilterLogic.And, Filter = f };
            Filters.Add(connector);
            return this;
        }

        public object Serialize()
        {
            var obj = new
            {
                p = this.PropertyName,
                o = this.Operator,
                v = this.Value,
                vi = this.Visible,
                t = this.Tag,
                f = this.Filters.Select(f => f.Serialize()).ToList()
            };

            return obj;
        }

        public static Filter Deserialize(dynamic f)
        {
            Filter filter = null;

            filter = new Filter(f.p.ToString(), f.o.ToString(), f.v, f.t.ToString());
            if (f.vi != null)
            {
                filter.Visible = f.vi;
            }

            if (f.f != null && f.f.Count > 0)
            {
                filter.Filters = new List<FilterConnector>();

                foreach (var ff in f.f)
                {
                    filter.Filters.Add(FilterConnector.Deserialize(ff));
                }
            }

            return filter;
        }
    }

    public class FilterConnector
    {
        /// <summary>
        /// Logical operator which connects this filter to another filter;
        /// </summary>
        public string Logic { get; set; }
        public Filter Filter { get; set; }

        public FilterConnector() { }

        public FilterConnector(FilterConnector connectorToClone)
        {
            Logic = connectorToClone.Logic;
            Filter = new Filter(connectorToClone.Filter);
        }

        public object Serialize()
        {
            var obj = new
            {
                l = Logic,
                f = Filter.Serialize()
            };

            return obj;
        }

        public static FilterConnector Deserialize(dynamic obj)
        {
            var fc = new FilterConnector();
            fc.Logic = obj.l;
            fc.Filter = Filter.Deserialize(obj.f);
            return fc;
        }
    }

    public class FacetInfoRequest
    {
        public FacetInfoRequest(string facetName, string sort = "count", int count = 1000)
        {
            FacetName = facetName;
            Sort = sort;
            Count = count;
        }

        /// <summary>
        /// Facetable property name.
        /// </summary>
        public string FacetName { get; set; }

        /// <summary>
        /// How to sort facet results. Count for sorting based on records number.
        /// </summary>
        public string Sort = "count";

        /// <summary>
        /// How many facet values to return;
        /// </summary>
        public int Count { get; set; } = 1000;

        public double[] Values { get; set; } = new double[] { };
    }

    public class SearchRequest
    {
        public int Limit { get; set; } = 50;
        public int Offset { get; set; } = 0;
        public string SearchText { get; set; } = "*";
        public Dictionary<string, string> OrderBy = new Dictionary<string, string>();
        public List<string> FieldsToHighlight = new List<string>();
        public string ScoringProfile;
        public List<FacetInfoRequest> FacetInfoToReturn = new List<FacetInfoRequest>();

        /// <summary>
        /// List of filters. All filters will be joined by AND.
        /// It's handy to have a list because different links may want to remove one filter and add another one.
        /// </summary>
        public List<Filter> Filters { get; set; } = new List<Filter>();

        public string SerializeToQueryString(
            string searchText = null,
            int? limit = null,
            int? offset = null,
            Dictionary<string, string> orderBy = null,
            Filter filterToAdd = null,
            string filterTagToRemove = null)
        {
            return $"" +
                $"&filters={SerializeFiltersToQueryString(filterToAdd, filterTagToRemove)}" +
                $"&limit={(limit ?? Limit)}" +
                $"&offset={(offset ?? Offset)}" +
                $"&orderBy={SerializeOrderByToQueryString(orderBy)}" +
                $"&searchText={(searchText == null ? SearchText : searchText)}";
        }

        public string SerializeFiltersToQueryString(Filter filterToAdd = null, string filterTagToRemove = null)
        {
            // clone original list since we don't want our modifications to be saved
            var clonedList = Filters.Select(f => new Filter(f)).ToList();

            if (filterTagToRemove != null)
            {
                clonedList.RemoveAll(f => f.Tag == filterTagToRemove);
            }

            if (filterToAdd != null)
            {
                clonedList.Add(filterToAdd);
            }

            var serialized = JsonConvert.SerializeObject(clonedList.Select(f => f.Serialize()));

            serialized = serialized
                .Replace("{", "-_v")
                .Replace("}", "v_-")
                .Replace("[", "-_x")
                .Replace("]", "x_-")
                .Replace(":", "-_i")
                .Replace(",", "-_q");

            return System.Net.WebUtility.UrlEncode(serialized);
        }

        public void DeserializeFiltersQueryString(string filters)
        {
            if (string.IsNullOrEmpty(filters))
            {
                return;
            }

            filters = filters
                .Replace("-_v", "{")
                .Replace("v_-", "}")
                .Replace("-_x", "[")
                .Replace("x_-", "]")
                .Replace("-_i", ":")
                .Replace("-_q", ",");

            List<object> filtersJson = JsonConvert.DeserializeObject<List<object>>(System.Net.WebUtility.UrlDecode(filters));

            this.Filters = filtersJson.Select(s => Filter.Deserialize(s))
                .ToList();
        }

        public string SerializeOrderByToQueryString(Dictionary<string, string> orderBy = null)
        {
            var ordersToWorkWith = orderBy ?? OrderBy;

            List<string> orders = new List<string>();

            foreach (KeyValuePair<string, string> order in ordersToWorkWith)
            {
                orders.Add($"{order.Key} {order.Value}");
            }

            return string.Join(",", orders);
        }

        public void DeserializeOrderByQueryString(string orderByQueryString)
        {
            if (string.IsNullOrEmpty(orderByQueryString))
            {
                return;
            }

            var orders = orderByQueryString.Split(',');

            foreach (var orderBy in orders)
            {
                var orderByParts = orderBy.Split(' ');

                if (orderByParts.Length == 2)
                {
                    OrderBy.Add(orderByParts[0], orderByParts[1]);
                }
            }
        }
    }
}
