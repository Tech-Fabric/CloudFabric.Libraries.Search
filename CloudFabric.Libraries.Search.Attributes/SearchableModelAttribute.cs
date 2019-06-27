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

        public static TypeCode GetPropertyPathTypeCode<T>(string pathName)
        {
            return GetPropertyPathTypeCode(pathName, typeof(T));
        }

        public static TypeCode GetPropertyPathTypeCode(string pathName, Type type)
        {
            var pathParts = pathName.Split('.');
            if (pathParts.Count() <= 1)
            {
                return GetPropertyTypeCode(pathName, type);
            }

            MemberInfo[] members = type.GetMember(pathParts[0]);

            if(members.Length == 0)
            {
                throw new Exception($"Failed to get member {pathParts[0]} from type {type.Name}");
            }

            var p = members[0];

            Type propertyType = p.DeclaringType;

            if(p.MemberType == MemberTypes.Method || p.MemberType == MemberTypes.Constructor || p.MemberType == MemberTypes.Event)
            {
                throw new Exception($"It's not possible to search by ${pathParts[0]} member of type ${type.Name} since it's not field or property");
            }
            else if (p.MemberType == MemberTypes.Property)
            {
                propertyType = (p as PropertyInfo).PropertyType;
            }
            else if (p.MemberType == MemberTypes.Field)
            {
                propertyType = (p as FieldInfo).FieldType;
            }

            if(propertyType.GetTypeInfo().IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>))
            {
                propertyType = propertyType.GetGenericArguments()[0];
            }

            if (propertyType.GetType().IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                propertyType = Nullable.GetUnderlyingType(propertyType);
            }

            return GetPropertyPathTypeCode(string.Join(".", pathParts.Skip(1)), propertyType);
        }

        public static TypeCode GetPropertyTypeCode(string propertyName, Type type)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                throw new Exception($"GetPropertyTypeCode: propertyName can't be empty, type: {type.FullName}");
            }

            PropertyInfo prop = type.GetProperty(propertyName);

            if (prop == null)
            {
                throw new Exception($"GetPropertyTypeCode: can't find property {propertyName} on type {type.FullName}");
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
                throw new Exception($"Failed to get property type code for property {propertyName} on type {type.FullName}", ex);
            }
        }

        public static TypeCode GetPropertyTypeCode<T>(string propertyName)
        {
            return GetPropertyPathTypeCode(propertyName, typeof(T));
        }
    }
}
