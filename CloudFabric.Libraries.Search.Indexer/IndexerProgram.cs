using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CloudFabric.Libraries.Search.Indexer
{
    public abstract class IndexerProgram
    {
        protected IConfigurationRoot Configuration { get; set; }

        protected void LoadSettings()
        {
            var config = new ConfigurationBuilder()
                            .SetBasePath(Directory.GetCurrentDirectory())
                            .AddJsonFile("appsettings.json")
                            .Build();

            Configuration = config;
        }

        protected abstract ISearchIndexer GetSearchIndexer();

        protected async Task CreateIndex()
        {
            ISearchIndexer indexer = null;
            try
            {
                indexer = GetSearchIndexer();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to get indexer..." + ex.Message);
            }

            if (indexer != null)
            {
                await CreateIndex(indexer);
            }
        }

        protected async Task CreateIndex(ISearchIndexer indexer)
        {
            throw new NotImplementedException();
        }

        public IndexerProgram(string[] args)
        {
            LoadSettings();

            Console.WriteLine("Creating indexes...");

            CreateIndex().GetAwaiter().GetResult();

            Console.WriteLine("{0}", "Complete.  Press any key to exit\n");
            Console.ReadKey();
        }
    }
}
