using System;
using System.Configuration;
using System.IO;
using System.Net;

namespace FileHostClient
{
    // Demo of CatalogLoader: starts the background downloads, then prints the current catalog whenever asked.
    // Settings are in App.config.
    internal static class Program
    {
        private static int Main()
        {
            string url = ConfigurationManager.AppSettings["Url"];
            string outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigurationManager.AppSettings["OutputPath"]);
            int timeoutSeconds = int.Parse(ConfigurationManager.AppSettings["TimeoutSeconds"]);
            int refreshMinutes = int.Parse(ConfigurationManager.AppSettings["RefreshMinutes"]);

            // Starts downloading on a background thread; the constructor returns immediately.
            using (CatalogLoader loader = new CatalogLoader(url, outputPath, timeoutSeconds, refreshMinutes))
            {
                Console.WriteLine("Downloading " + url + " now and every " + refreshMinutes + " minute(s). Ready yet? " + loader.IsReady);

                // The program is free to do other work meanwhile. Here it just prints the catalog when asked.
                // Only the first print can wait, and only until the first download is done.
                PrintCurrent(loader, outputPath);
                Console.WriteLine();
                Console.WriteLine("Press Enter to print the current catalog again, or type q and press Enter to quit.");
                string line;
                while ((line = Console.ReadLine()) != null && line.Trim() != "q")
                {
                    PrintCurrent(loader, outputPath);
                }
            }
            return 0;
        }

        private static void PrintCurrent(CatalogLoader loader, string outputPath)
        {
            try
            {
                // Gives the latest catalog, or throws what went wrong if no download has succeeded yet.
                Catalog catalog = loader.GetCatalog();
                Console.WriteLine("Saved " + new FileInfo(outputPath).Length + " bytes to " + outputPath);
                if (loader.LastRefreshError != null)
                {
                    Console.WriteLine("Latest refresh failed, showing the previous catalog: " + loader.LastRefreshError.GetBaseException().Message);
                }
                Print(catalog);
            }
            catch (WebException ex)
            {
                // Server not reachable, timed out, busy for too long, or answered with an error such as 404.
                Console.WriteLine("Download failed: " + ex.Message);
            }
            catch (IOException ex)
            {
                Console.WriteLine("Could not save the file: " + ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                // XmlSerializer reports bad XML this way; the inner exception says what was wrong.
                Console.WriteLine("Could not read the XML: " + ex.GetBaseException().Message);
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
