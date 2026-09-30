// Demo client for FileHost: lists the files on the server and downloads them.
//
// Usage:
//   dotnet run -- [--server http://host:port] [--output folder] [file names...]
//
// With no file names, every file on the server is downloaded.

using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Xml;
using System.Xml.Serialization;

var server = "http://localhost:8080";
var outputPath = "Downloads";
var wanted = new List<string>();

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--server" when i + 1 < args.Length: server = args[++i]; break;
        case "--output" when i + 1 < args.Length: outputPath = args[++i]; break;
        case "--help" or "-h":
            Console.WriteLine("Usage: dotnet run -- [--server http://host:port] [--output folder] [file names...]");
            return 0;
        default: wanted.Add(args[i]); break;
    }
}

outputPath = Path.GetFullPath(outputPath);
Directory.CreateDirectory(outputPath);

using var http = new HttpClient { BaseAddress = new Uri(server) };

// 1. Ask the server what files it has.
List<RemoteFile> files;
try
{
    files = await http.GetFromJsonAsync<List<RemoteFile>>("/api/files") ?? [];
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Could not reach {server}: {ex.Message}");
    return 1;
}

Console.WriteLine($"{files.Count} file(s) on {server}:");
foreach (var f in files)
    Console.WriteLine($"  {f.Name,-40} {f.Size,12:N0} bytes");

var failed = 0;
if (wanted.Count > 0)
{
    foreach (var missing in wanted.Where(w => !files.Any(f => f.Name == w)))
    {
        Console.Error.WriteLine($"Not on server: {missing}");
        failed++;
    }
    files = files.Where(f => wanted.Contains(f.Name)).ToList();
}

// 2. Download each file into the output folder.
Console.WriteLine($"\nDownloading {files.Count} file(s) to {outputPath}");
var downloaded = new List<string>();
foreach (var f in files)
{
    var destination = Path.Combine(outputPath, Path.GetFileName(f.Name));
    var partial = destination + ".part";
    try
    {
        using var response = await http.GetAsync(
            "/download/" + Uri.EscapeDataString(f.Name), HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        // Stream to a temporary file so a failed download never leaves a half-written file behind.
        using (var source = await response.Content.ReadAsStreamAsync())
        using (var target = File.Create(partial))
            await source.CopyToAsync(target);

        File.Delete(destination);
        File.Move(partial, destination);
        downloaded.Add(destination);
        Console.WriteLine($"  OK    {f.Name}");
    }
    catch (Exception ex) when (ex is HttpRequestException or IOException)
    {
        File.Delete(partial);
        Console.Error.WriteLine($"  FAIL  {f.Name}: {ex.Message}");
        failed++;
    }
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

class RemoteFile
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public DateTime Modified { get; set; }
}
