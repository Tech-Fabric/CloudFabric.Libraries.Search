namespace CloudFabric.Libraries.Search
{
    public class SuggestionResultRecord<T>
    {
        public T Record { get; set; }
        public string TextWithHighlights { get; set; }
    }
}
