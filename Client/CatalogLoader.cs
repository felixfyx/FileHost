using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace FileHostClient
{
    // Downloads the catalog XML in the background as soon as it's created, saves a copy, and deserializes it.
    //
    //   CatalogLoader loader = new CatalogLoader(url, outputPath, 30);  // returns immediately
    //   ...
    //   if (loader.IsReady) { ... }                                     // check without waiting
    //   Catalog catalog = await loader.GetCatalogAsync();               // wait only if not ready yet
    //
    // If the download fails, GetCatalogAsync throws: WebException (download), IOException (saving the file)
    // or InvalidOperationException (bad XML). Make sure something awaits it, or the error goes unnoticed.
    public class CatalogLoader
    {
        // If the server says the file is being updated (503), wait this long and try again, up to this many times.
        private const int RetryDelaySeconds = 2;
        private const int MaxAttempts = 5;

        private readonly Task<Catalog> _download;

        public CatalogLoader(string url, string outputPath, int timeoutSeconds)
        {
            // Start in the background and return immediately (a constructor can't await).
            //
            // Task.Run around the normal (blocking) download is used instead of HttpWebRequest's async
            // methods because Timeout and ReadWriteTimeout only work for the blocking calls.
            _download = Task.Run(() => DownloadAndRead(url, outputPath, timeoutSeconds));
        }

        // True once the catalog has downloaded and loaded successfully.
        public bool IsReady
        {
            get { return _download.Status == TaskStatus.RanToCompletion; }
        }

        // Await this wherever the data is needed. It waits only if the catalog isn't ready yet,
        // and throws if the download failed. Awaiting it again later returns the same catalog.
        public Task<Catalog> GetCatalogAsync()
        {
            return _download;
        }

        // Runs in the background: download, then deserialize.
        private static Catalog DownloadAndRead(string url, string outputPath, int timeoutSeconds)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    Download(url, outputPath, timeoutSeconds);
                    return Deserialize(outputPath);
                }
                catch (WebException ex) when (IsServerBusy(ex) && attempt < MaxAttempts)
                {
                    Thread.Sleep(RetryDelaySeconds * 1000);
                }
            }
        }

        private static bool IsServerBusy(WebException ex)
        {
            HttpWebResponse response = ex.Response as HttpWebResponse;
            return response != null && response.StatusCode == HttpStatusCode.ServiceUnavailable;
        }

        // Streams the file straight to disk in small chunks, so memory use stays low however big the file is.
        private static void Download(string url, string outputPath, int timeoutSeconds)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = timeoutSeconds * 1000;          // Waiting for the server to respond.
            request.ReadWriteTimeout = timeoutSeconds * 1000; // Waiting for each piece of the file once it starts.

            // Download to a temporary file first, so a failed download never leaves a half-written file
            // under the real name. A .tmp left by a failed run is simply overwritten next time.
            string tempPath = outputPath + ".tmp";
            using (WebResponse response = request.GetResponse())
            using (Stream body = response.GetResponseStream())
            using (FileStream file = File.Create(tempPath))
            {
                body.CopyTo(file);

                // If the connection drops early, some systems just report "end of file" instead of an error,
                // so check we got as many bytes as the server said it would send (-1 means it didn't say).
                if (response.ContentLength >= 0 && file.Length != response.ContentLength)
                {
                    throw new WebException("Download incomplete: received " + file.Length + " of " + response.ContentLength + " bytes.");
                }
            }

            // Swap the new file in, in one step, so anything reading outputPath sees either the old
            // complete file or the new complete file, never a missing or half-written one.
            if (File.Exists(outputPath))
            {
                File.Replace(tempPath, outputPath, null);
            }
            else
            {
                File.Move(tempPath, outputPath);
            }
        }

        private static Catalog Deserialize(string path)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(Catalog));
            using (FileStream file = File.OpenRead(path))
            {
                return (Catalog)serializer.Deserialize(file);
            }
        }
    }
}
