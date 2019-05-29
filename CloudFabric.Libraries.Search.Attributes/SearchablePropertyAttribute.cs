using System;
using System.Collections.Generic;
using System.Text;

namespace CloudFabric.Libraries.Search.Attributes
{
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    public class SearchablePropertyAttribute : Attribute
    {
        public virtual bool IsKey { get; set; } = false;
        public virtual bool IsSearchable { get; set; } = false;
        public virtual string[] SynonymMaps { get; set; } = new string[] { };
        public virtual double SearchableBoost { get; set; } = 0;
        public virtual bool IsFilterable { get; set; } = false;
        public virtual bool IsSortable { get; set; } = false;
        public virtual bool IsFacetable { get; set; } = false;

        public virtual bool IsNested { get; set; } = false;

        public virtual string Analyzer { get; set; }
        public virtual string SearchAnalyzer { get; set; }
        public virtual string IndexAnalyzer { get; set; }

        public virtual bool UseForSuggestions { get; set; } = false;

        public virtual double[] FacetableRanges { get; set; } = new double[] { };
    }
}
