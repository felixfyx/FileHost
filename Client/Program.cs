using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace FileHostClient
{
    // Demo of CatalogLoader: starts the download, does other work meanwhile, then uses the catalog.
    // Settings are in App.config.
    internal static class Program
    {
        private static int Main()
        {
            string url = ConfigurationManager.AppSettings["Url"];
            string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigurationManager.AppSettings["OutputPath"]);
            int timeoutSeconds = int.Parse(ConfigurationManager.AppSettings["TimeoutSeconds"]);

            // Starts downloading in the background; the constructor returns immediately.
            CatalogLoader loader = new CatalogLoader(url, outputPath, timeoutSeconds);
            Console.WriteLine("Download started. Ready yet? " + loader.IsReady);

            // The program is free to do other work meanwhile. Here it just shows progress dots.
            Task<Catalog> catalogTask = loader.GetCatalogAsync();
            Console.Write("Downloading " + url);
            while (!catalogTask.IsCompleted)
            {
                Console.Write(".");
                Thread.Sleep(500);
            }
            Console.WriteLine();

            try
            {
                // Gives the catalog, or throws whatever went wrong in the background.
                // GetAwaiter().GetResult() throws the original exception (like await does), so the catch
                // blocks below match it. .Result would wrap it in an AggregateException instead.
                Catalog catalog = catalogTask.GetAwaiter().GetResult();
                Console.WriteLine("Ready? " + loader.IsReady + ". Saved " + new FileInfo(outputPath).Length + " bytes to " + outputPath);
                Print(catalog);
                return 0;
            }
            catch (WebException ex)
            {
                // Server not reachable, timed out, busy for too long, or answered with an error such as 404.
                Console.WriteLine("Download failed: " + ex.Message);
                return 1;
            }
            catch (IOException ex)
            {
                Console.WriteLine("Could not save the file: " + ex.Message);
                return 1;
            }
            catch (InvalidOperationException ex)
            {
                // XmlSerializer reports bad XML this way; the inner exception says what was wrong.
                Console.WriteLine("Could not read the XML: " + ex.GetBaseException().Message);
                return 1;
            }
        }

        private static void Print(Catalog catalog)
        {
            Console.WriteLine();
            Console.WriteLine(catalog.Name + " (updated " + catalog.Updated.ToString("u") + "), " + catalog.Products.Count + " product(s):");
            foreach (Product p in catalog.Products)
            {
                Console.WriteLine("  #" + p.Id + " " + p.Name + " [" + p.Category + "] "
                    + p.Price.Amount + " " + p.Price.Currency
                    + ", qty " + p.Quantity + (p.InStock ? ", in stock" : ", out of stock")
                    + ", tags: " + string.Join(", ", p.Tags));
            }
        }
    }
}
