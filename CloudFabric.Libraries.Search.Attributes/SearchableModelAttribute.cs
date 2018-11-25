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

        public static string GetKeyPropertyName<T>()
        {
            PropertyInfo[] props = typeof(T).GetProperties();
            foreach (PropertyInfo prop in props)
            {
                object[] attrs = prop.GetCustomAttributes(true);
                foreach (object attr in attrs)
                {
                    SearchablePropertyAttribute propertyAttribute = attr as SearchablePropertyAttribute;
                    if (propertyAttribute != null)
                    {
                        if (propertyAttribute.IsKey)
                        {
                            return prop.Name;
                        }
                    }
                }
            }

            return null;
        }

        public static Dictionary<PropertyInfo, SearchablePropertyAttribute> GetFacetableProperties<T>()
        {
            Dictionary<PropertyInfo, SearchablePropertyAttribute> facetableProperties = new Dictionary<PropertyInfo, SearchablePropertyAttribute>();

            PropertyInfo[] props = typeof(T).GetProperties();
            foreach (PropertyInfo prop in props)
            {
                object[] attrs = prop.GetCustomAttributes(true);
                foreach (object attr in attrs)
                {
                    SearchablePropertyAttribute propertyAttribute = attr as SearchablePropertyAttribute;
                    if (propertyAttribute != null)
                    {
                        if (propertyAttribute.IsFacetable)
                        {
                            facetableProperties.Add(prop, propertyAttribute);
                        }
                    }
                }
            }

            return facetableProperties;
        }

        public static List<string> GetFacetablePropertyNames<T>()
        {
            return GetFacetableProperties<T>().Keys.Select(p => p.Name).ToList();
        }

        public static Dictionary<PropertyInfo, SearchablePropertyAttribute> GetSearchableProperties<T>()
        {
            Dictionary<PropertyInfo, SearchablePropertyAttribute> searchableProperties = new Dictionary<PropertyInfo, SearchablePropertyAttribute>();

            PropertyInfo[] props = typeof(T).GetProperties();
            foreach (PropertyInfo prop in props)
            {
                object[] attrs = prop.GetCustomAttributes(true);
                foreach (object attr in attrs)
                {
                    SearchablePropertyAttribute propertyAttribute = attr as SearchablePropertyAttribute;
                    if (propertyAttribute != null)
                    {
                        if (propertyAttribute.IsSearchable)
                        {
                            searchableProperties.Add(prop, propertyAttribute);
                        }
                    }
                }
            }

            return searchableProperties;
        }

        public static List<string> GetSearchablePropertyNames<T>()
        {
            return GetSearchableProperties<T>().Keys.Select(p => p.Name).ToList();
        }

        public static TypeCode GetPropertyTypeCode<T>(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                throw new Exception($"GetPropertyTypeCode: propertyName can't be empty, type: {typeof(T).FullName}");
            }

            PropertyInfo prop = typeof(T).GetProperty(propertyName);

            if (prop == null)
            {
                throw new Exception($"GetPropertyTypeCode: can't find property {propertyName} on type {typeof(T).FullName}");
            }

            try
            {
                if (prop.PropertyType.IsGenericType)
                {
                    if (prop.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                    {
                        return Type.GetTypeCode(prop.PropertyType.GetGenericArguments()[0]);
                    }
                }

                return Type.GetTypeCode(prop.PropertyType);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get property type code for property {propertyName} on type {typeof(T).FullName}", ex);
            }
        }
    }
}
