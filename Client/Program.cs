// Demo client for FileHost: downloads files from the server.
//
// Usage:
//   dotnet run -- [--server http://host:port] [--output folder] [--timeout seconds] [file names...]
//
// With file names, only those files are downloaded, straight from the server.
// With no file names, the client asks the server for its file list and downloads everything.

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;

var server = "http://localhost:8080";
var outputPath = "Downloads";
var timeoutSeconds = 30;
var wanted = new List<string>();
const string usage =
    "Usage: dotnet run -- [--server http://host:port] [--output folder] [--timeout seconds] [file names...]";

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--server" when i + 1 < args.Length: server = args[++i]; break;
        case "--output" when i + 1 < args.Length: outputPath = args[++i]; break;
        case "--timeout" when i + 1 < args.Length:
            if (!int.TryParse(args[++i], out timeoutSeconds) || timeoutSeconds <= 0)
            {
                Console.Error.WriteLine("--timeout must be a whole number of seconds greater than 0.");
                return 1;
            }
            break;
        case "--help" or "-h":
            Console.WriteLine(usage);
            return 0;
        default: wanted.Add(args[i]); break;
    }
}

outputPath = Path.GetFullPath(outputPath);
Directory.CreateDirectory(outputPath);

// How long to wait for the server to respond, and how long a download may go without receiving
// any data. A slow download that keeps making progress is never cut off.
var timeout = TimeSpan.FromSeconds(timeoutSeconds);

// Timeouts are handled per request below, so turn off HttpClient's own 100-second limit.
using var http = new HttpClient { BaseAddress = new Uri(server), Timeout = System.Threading.Timeout.InfiniteTimeSpan };

// 1. Work out which files to download.
var failed = 0;
List<string> names;
if (wanted.Count > 0)
{
    // Specific files requested: no need for the full list, just ask for each file directly.
    names = wanted;
}
else
{
    var files = await FetchFileListAsync();
    if (files is null)
        return 1;

    Console.WriteLine($"{files.Count} file(s) on {server}:");
    foreach (var f in files)
        Console.WriteLine($"  {f.Name,-40} {f.Size,12:N0} bytes");
    names = files.Select(f => f.Name).ToList();
}

// 2. Download each file into the output folder.
Console.WriteLine($"\nDownloading {names.Count} file(s) from {server} to {outputPath}");
var downloaded = new List<string>();
foreach (var name in names)
{
    var path = await DownloadAsync(name);
    if (path is null)
        failed++;
    else
        downloaded.Add(path);
}

// 3. Turn any downloaded catalog XML files into C# objects (see Catalog.cs).
var serializer = new XmlSerializer(typeof(Catalog));
foreach (var path in downloaded.Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
{
    try
    {
        using var reader = XmlReader.Create(path);
        if (!serializer.CanDeserialize(reader))
        {
            Console.WriteLine($"\n{Path.GetFileName(path)}: not a <Catalog> file, skipped.");
            continue;
        }

        var catalog = (Catalog)serializer.Deserialize(reader)!;
        Console.WriteLine($"\n{Path.GetFileName(path)}: \"{catalog.Name}\", updated {catalog.Updated:u}, {catalog.Products.Count} product(s)");
        foreach (var p in catalog.Products)
        {
            var price = p.Price.Amount.ToString("0.00", CultureInfo.InvariantCulture);
            Console.WriteLine($"  #{p.Id} {p.Name,-22} {p.Category,-9} {price,8} {p.Price.Currency}  " +
                              $"qty {p.Quantity,4}  {(p.InStock ? "in stock" : "out of stock"),-12} [{string.Join(", ", p.Tags)}]");
        }
    }
    catch (Exception ex) when (ex is XmlException or InvalidOperationException)
    {
        Console.Error.WriteLine($"\n{Path.GetFileName(path)}: could not read XML: {ex.GetBaseException().Message}");
        failed++;
    }
}

return failed == 0 ? 0 : 1;

// Asks the server for its file list. Returns null (after printing why) if that fails.
async Task<List<RemoteFile>?> FetchFileListAsync()
{
    using var cts = new CancellationTokenSource(timeout);
    try
    {
        return await http.GetFromJsonAsync<List<RemoteFile>>("/api/files", cts.Token) ?? [];
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    {
        Console.Error.WriteLine($"Timed out after {timeoutSeconds}s waiting for the file list from {server}.");
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"Could not reach {server}: {ex.Message}");
    }
    catch (JsonException ex)
    {
        Console.Error.WriteLine($"{server} sent a file list that isn't valid JSON: {ex.Message}");
    }
    return null;
}

// Downloads one file into the output folder. Returns its path, or null (after printing why) if it failed.
async Task<string?> DownloadAsync(string name)
{
    var destination = Path.Combine(outputPath, Path.GetFileName(name));
    var partial = destination + ".part";

    // Cancels if the server doesn't respond within the timeout, or later goes quiet for that long.
    using var stalled = new CancellationTokenSource(timeout);
    try
    {
        using var response = await http.GetAsync(
            "/download/" + Uri.EscapeDataString(name), HttpCompletionOption.ResponseHeadersRead, stalled.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Console.Error.WriteLine($"  MISSING  {name}: not on server");
            return null;
        }
        response.EnsureSuccessStatusCode();

        // On .NET Framework a read that is already waiting ignores the token, so also close the
        // response on timeout; that makes the stuck read fail straight away.
        using var closeOnStall = stalled.Token.Register(response.Dispose);

        // Stream to a temporary file so a failed download never leaves a half-written file behind.
        using (var source = await response.Content.ReadAsStreamAsync())
        using (var target = File.Create(partial))
        {
            var buffer = new byte[81920];
            int read;
            stalled.CancelAfter(timeout);
            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, stalled.Token)) > 0)
            {
                await target.WriteAsync(buffer, 0, read);
                stalled.CancelAfter(timeout); // Data arrived, so restart the countdown.
            }
        }

        File.Delete(destination);
        File.Move(partial, destination);
        Console.WriteLine($"  OK       {name}");
        return destination;
    }
    catch (Exception) when (stalled.IsCancellationRequested)
    {
        Console.Error.WriteLine($"  TIMEOUT  {name}: no data from the server for {timeoutSeconds}s");
    }
    catch (Exception ex) when (ex is HttpRequestException or IOException)
    {
        Console.Error.WriteLine($"  FAIL     {name}: {ex.Message}");
    }
    File.Delete(partial);
    return null;
}

class RemoteFile
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public DateTime Modified { get; set; }
}
