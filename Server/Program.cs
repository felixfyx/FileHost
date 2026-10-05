using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace FileHostServer
{
    // Serves a single XML file over HTTP, at http://<this computer>:<Port>/<file name>.
    // Handles several clients at once. Settings are in App.config.
    internal static class Program
    {
        private static async Task Main()
        {
            int port = int.Parse(ConfigurationManager.AppSettings["Port"]);
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigurationManager.AppSettings["FilePath"]);
            string fileName = Path.GetFileName(filePath);

            if (!File.Exists(filePath))
            {
                Console.WriteLine("File not found: " + filePath);
                return;
            }

            // "+" means listen on every network address, so other computers can connect.
            // On Windows this needs the program to run as administrator, or a one-time reservation
            // from an administrator command prompt:
            //   netsh http add urlacl url=http://+:8080/ user=Everyone
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://+:" + port + "/");
            listener.Start();

            Console.WriteLine("Serving " + filePath);
            Console.WriteLine("Download it from http://localhost:" + port + "/" + fileName);
            Console.WriteLine("Press Ctrl+C to stop.");

            while (true)
            {
                // Wait for the next request, then handle it without waiting for it to finish,
                // so the loop is immediately ready for the next client.
                HttpListenerContext context = await listener.GetContextAsync();
                _ = HandleRequestAsync(context, filePath, fileName);
            }
        }

        private static async Task HandleRequestAsync(HttpListenerContext context, string filePath, string fileName)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            string client = request.RemoteEndPoint.ToString(); // Read now: it's gone once the client disconnects.

            // Each request runs on its own, so it must catch its own errors: an uncaught exception
            // here would stop the whole server.
            try
            {
                if (request.Url.AbsolutePath != "/" + fileName)
                {
                    response.StatusCode = 404;
                }
                else
                {
                    FileStream file = TryOpen(filePath);
                    if (file == null)
                    {
                        // Not ready: someone is writing or replacing the file right now.
                        // Tell the client to try again shortly.
                        response.StatusCode = 503;
                        response.AddHeader("Retry-After", "2");
                    }
                    else
                    {
                        // CopyToAsync sends the file in small chunks, so memory use stays low however big it is.
                        using (file)
                        {
                            response.ContentType = "application/xml";
                            response.ContentLength64 = file.Length;
                            await file.CopyToAsync(response.OutputStream);
                        }
                    }
                }

                Console.WriteLine(client + " " + request.HttpMethod + " " + request.Url.AbsolutePath + " -> " + response.StatusCode);
                response.Close();
            }
            catch (Exception ex)
            {
                // E.g. the client disconnected mid-download. Drop this request; others are unaffected.
                Console.WriteLine(client + " request failed: " + ex.Message);
                response.Abort();
            }
        }

        // Opens the file for reading, or returns null if it's busy (being written) or briefly missing (being replaced).
        // FileShare.Delete lets someone replace the file while downloads are in progress: those downloads
        // finish with the old version, and new requests get the new one.
        private static FileStream TryOpen(string filePath)
        {
            try
            {
                return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
