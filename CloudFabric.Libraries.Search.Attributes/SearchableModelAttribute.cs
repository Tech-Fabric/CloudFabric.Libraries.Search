using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CloudFabric.Libraries.Search.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class SearchableModelAttribute : Attribute
    {
        public virtual string IndexName { get; set; }

        public static string GetIndexName(Type type)
        {
            var modelClassAttributes = type.GetCustomAttributes(typeof(SearchableModelAttribute), true);

            if (modelClassAttributes.Length < 1)
            {
                return null;
            }

            var searchableModelAttribute = modelClassAttributes[0] as SearchableModelAttribute;

            return searchableModelAttribute.IndexName;
        }
    }
}
