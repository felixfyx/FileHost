using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace FileHostClient
{
    // Downloads the catalog XML on a background thread as soon as it's created, then downloads it again every
    // few minutes. Each download saves a copy and deserializes it.
    //
    //   using (CatalogLoader loader = new CatalogLoader(url, outputPath, 30, 5))  // returns immediately
    //   {
    //       ...
    //       if (loader.IsReady) { ... }                // check without waiting
    //       Catalog catalog = loader.GetCatalog();     // waits only until the first download is done
    //   }
    //
    // Until a download has succeeded, GetCatalog throws what went wrong: WebException (download), IOException
    // (saving the file) or InvalidOperationException (bad XML). After that, a failed refresh keeps the previous
    // catalog and is reported through LastRefreshError instead.
    public class CatalogLoader : IDisposable
    {
        // If the server says the file is being updated (503), wait this long and try again, up to this many times.
        private const int RetryDelaySeconds = 2;
        private const int MaxAttempts = 5;

        private readonly string _url;
        private readonly string _outputPath;
        private readonly int _timeoutSeconds;
        private readonly TimeSpan _refreshInterval;

        // Completes when the first download finishes, so GetCatalog has something to wait on until then.
        private readonly TaskCompletionSource<Catalog> _firstDownload = new TaskCompletionSource<Catalog>();

        // The newest result: a finished task holding the latest good catalog or, if none has loaded yet, the error.
        // Swapped in whole by the background thread, so readers always see one complete result.
        private volatile Task<Catalog> _latest;
        private volatile Exception _lastRefreshError;

        // Set by Dispose to wake the background thread and stop it. Never disposed itself, because the
        // background thread may still be waiting on it.
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);

        public CatalogLoader(string url, string outputPath, int timeoutSeconds, int refreshMinutes)
        {
            _url = url;
            _outputPath = outputPath;
            _timeoutSeconds = timeoutSeconds;
            _refreshInterval = TimeSpan.FromMinutes(refreshMinutes);
            _latest = _firstDownload.Task;

            // One thread does every download, one after another, so two downloads never overlap or write the
            // same file at once. A background thread doesn't keep the program running after Main returns.
            //
            // The normal (blocking) download is used instead of HttpWebRequest's async methods because Timeout
            // and ReadWriteTimeout only work for the blocking calls.
            Thread thread = new Thread(RefreshLoop) { IsBackground = true, Name = "Catalog refresh" };
            thread.Start();
        }

        // True once a catalog has downloaded and loaded successfully.
        public bool IsReady
        {
            get { return _latest.Status == TaskStatus.RanToCompletion; }
        }

        // What went wrong with the most recent download, or null if it succeeded.
        public Exception LastRefreshError
        {
            get { return _lastRefreshError; }
        }

        // Returns the latest catalog. Waits only if the first download hasn't finished yet, and throws if no
        // download has succeeded so far. Each refresh produces a new Catalog object, so one already returned
        // never changes underneath the caller.
        public Catalog GetCatalog()
        {
            // GetAwaiter().GetResult() throws the original exception, where .Result would wrap it in an
            // AggregateException.
            return _latest.GetAwaiter().GetResult();
        }

        // Stops the refreshes. A download already in progress finishes first.
        public void Dispose()
        {
            _stop.Set();
        }

        // Runs on the background thread: download now, then again every refresh interval until stopped.
        private void RefreshLoop()
        {
            do
            {
                try
                {
                    Catalog catalog = DownloadAndRead(_url, _outputPath, _timeoutSeconds);
                    _lastRefreshError = null;
                    _latest = Task.FromResult(catalog);
                    _firstDownload.TrySetResult(catalog);
                }
                catch (Exception ex)
                {
                    // Catch everything: an exception escaping this thread would end the whole program.
                    _lastRefreshError = ex;
                    if (!IsReady)
                    {
                        // Nothing good loaded yet, so pass the error on to GetCatalog.
                        TaskCompletionSource<Catalog> failed = new TaskCompletionSource<Catalog>();
                        failed.SetException(ex);
                        _latest = failed.Task;
                        _firstDownload.TrySetException(ex);
                    }
                }
            }
            while (!_stop.WaitOne(_refreshInterval));
        }

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
