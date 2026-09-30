// FileHost server: shares a folder over HTTP.
// Built on HttpListener so the same code runs on .NET 8 and .NET Framework 4.8.

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

// Settings come from appsettings.json and can be overridden on the command line:
//   Server --FilesPath /some/folder --Port 9000
// appsettings.json is read from the current folder if it has one, otherwise from the program's folder.
// A relative FilesPath is resolved from the folder that appsettings.json was read from.
var settingsDir = File.Exists(Path.Combine(Environment.CurrentDirectory, "appsettings.json"))
    ? Environment.CurrentDirectory
    : AppContext.BaseDirectory;
var config = new ConfigurationBuilder()
    .SetBasePath(settingsDir)
    .AddJsonFile("appsettings.json", optional: true)
    .AddCommandLine(args)
    .Build();

var filesPath = Path.GetFullPath(Path.Combine(settingsDir, config["FilesPath"] ?? "SharedFiles"))
    .TrimEnd(Path.DirectorySeparatorChar);
var port = config.GetValue("Port", 8080);
Directory.CreateDirectory(filesPath);

var listener = StartListener(port);
Console.WriteLine($"Sharing files from {filesPath} on port {port}. Press Ctrl+C to stop.");

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    listener.Stop();
};

// Handle each request on its own task so a slow download doesn't block other clients.
while (listener.IsListening)
{
    HttpListenerContext context;
    try { context = await listener.GetContextAsync(); }
    catch (Exception) when (!listener.IsListening) { break; }
    catch (HttpListenerException) { continue; }

    _ = Task.Run(() => HandleAsync(context));
}
Console.WriteLine("Server stopped.");
return 0;

// Listen on every network address. On Windows that needs admin rights (or a URL reservation),
// so fall back to localhost-only rather than failing to start.
static HttpListener StartListener(int port)
{
    var listener = new HttpListener();
    listener.Prefixes.Add($"http://+:{port}/");
    try
    {
        listener.Start();
        return listener;
    }
    catch (HttpListenerException ex) when (ex.ErrorCode == 5) // Access denied
    {
        Console.WriteLine("Not allowed to listen on all addresses, so only this computer can connect.");
        Console.WriteLine("To allow other computers, run once as administrator:");
        Console.WriteLine($"  netsh http add urlacl url=http://+:{port}/ user=Everyone");
        listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        return listener;
    }
}

async Task HandleAsync(HttpListenerContext context)
{
    var request = context.Request;
    var response = context.Response;
    var path = request.Url!.AbsolutePath;
    // HEAD gets the same headers as GET but no body (used by tools to check a file before downloading).
    var headOnly = request.HttpMethod == "HEAD";
    try
    {
        if (request.HttpMethod != "GET" && !headOnly)
            await WriteTextAsync(response, 405, "text/plain", "Method not allowed.", headOnly);
        else if (path == "/")
            await WriteTextAsync(response, 200, "text/html", IndexPage(), headOnly);
        else if (path == "/api/files")
            await WriteTextAsync(response, 200, "application/json", FileListJson(), headOnly);
        else if (path.StartsWith("/download/", StringComparison.Ordinal))
            await SendFileAsync(response, Uri.UnescapeDataString(path.Substring("/download/".Length)), headOnly);
        else
            await WriteTextAsync(response, 404, "text/plain", "Not found.", headOnly);

        Console.WriteLine($"{request.RemoteEndPoint} {request.HttpMethod} {path} -> {response.StatusCode}");
    }
    catch (Exception ex) when (ex is HttpListenerException or IOException)
    {
        // The client disconnected mid-response; nothing to do.
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error handling {path}: {ex}");
        try { response.StatusCode = 500; } catch (InvalidOperationException) { }
    }
    finally
    {
        try { response.Close(); } catch (HttpListenerException) { }
    }
}

// Visible files in the shared folder, sorted by name.
IEnumerable<FileInfo> ListFiles() => new DirectoryInfo(filesPath)
    .EnumerateFiles()
    .Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden) && !f.Name.StartsWith("."))
    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

// Home page: list every file in the shared folder with a download link.
string IndexPage()
{
    var rows = new StringBuilder();
    foreach (var f in ListFiles())
    {
        var name = WebUtility.HtmlEncode(f.Name);
        var url = "/download/" + Uri.EscapeDataString(f.Name);
        rows.Append($"<tr><td><a href=\"{url}\">{name}</a></td>" +
                    $"<td>{FormatSize(f.Length)}</td>" +
                    $"<td>{f.LastWriteTime:yyyy-MM-dd HH:mm}</td></tr>");
    }
    if (rows.Length == 0)
        rows.Append("<tr><td colspan=\"3\"><em>No files available.</em></td></tr>");

    return $$"""
        <!doctype html>
        <html>
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>File Downloads</title>
          <style>
            body { font-family: system-ui, sans-serif; max-width: 800px; margin: 2rem auto; padding: 0 1rem; }
            table { width: 100%; border-collapse: collapse; }
            th, td { text-align: left; padding: .5rem; border-bottom: 1px solid #ddd; }
            a { text-decoration: none; }
          </style>
        </head>
        <body>
          <h1>File Downloads</h1>
          <table>
            <tr><th>Name</th><th>Size</th><th>Modified</th></tr>
            {{rows}}
          </table>
        </body>
        </html>
        """;
}

// JSON file list for programs (see the Client project).
string FileListJson() => JsonSerializer.Serialize(
    ListFiles().Select(f => new { name = f.Name, size = f.Length, modified = f.LastWriteTimeUtc }));

// Download a single file by name.
async Task SendFileAsync(HttpListenerResponse response, string name, bool headOnly)
{
    // Only allow plain file names inside the shared folder (blocks "../" tricks).
    string fullPath;
    try { fullPath = Path.GetFullPath(Path.Combine(filesPath, name)); }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { fullPath = ""; }

    if (name.Length == 0
        || Path.GetFileName(name) != name
        || Path.GetDirectoryName(fullPath) != filesPath
        || !File.Exists(fullPath))
    {
        await WriteTextAsync(response, 404, "text/plain", "File not found.", headOnly);
        return;
    }

    using var file = File.OpenRead(fullPath);
    response.StatusCode = 200;
    response.ContentType = ContentTypeFor(name);
    response.ContentLength64 = file.Length;
    response.AddHeader("Content-Disposition",
        $"attachment; filename=\"{AsciiFileName(name)}\"; filename*=UTF-8''{Uri.EscapeDataString(name)}");
    if (!headOnly)
        await file.CopyToAsync(response.OutputStream);
}

static async Task WriteTextAsync(HttpListenerResponse response, int status, string contentType, string text, bool headOnly)
{
    var bytes = Encoding.UTF8.GetBytes(text);
    response.StatusCode = status;
    response.ContentType = contentType + "; charset=utf-8";
    response.ContentLength64 = bytes.Length;
    if (!headOnly)
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
}

// Plain-ASCII version of a file name for older clients that ignore filename*.
static string AsciiFileName(string name) =>
    new(name.Select(c => c < 32 || c > 126 || c == '"' || c == '\\' ? '_' : c).ToArray());

static string ContentTypeFor(string name) => Path.GetExtension(name).ToLowerInvariant() switch
{
    ".txt" or ".log" or ".csv" => "text/plain",
    ".htm" or ".html" => "text/html",
    ".xml" => "application/xml",
    ".json" => "application/json",
    ".pdf" => "application/pdf",
    ".zip" => "application/zip",
    ".png" => "image/png",
    ".jpg" or ".jpeg" => "image/jpeg",
    ".gif" => "image/gif",
    ".mp3" => "audio/mpeg",
    ".mp4" => "video/mp4",
    _ => "application/octet-stream",
};

static string FormatSize(long bytes)
{
    string[] units = ["B", "KB", "MB", "GB", "TB"];
    double size = bytes;
    var i = 0;
    while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
    return $"{size:0.#} {units[i]}";
}
