using System.Net;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

// Settings come from appsettings.json and can be overridden on the command line:
//   dotnet run -- --FilesPath /some/folder --Port 9000
// A relative FilesPath is resolved from this project's folder, not the current directory.
var filesPath = Path.GetFullPath(builder.Configuration["FilesPath"] ?? "SharedFiles",
    builder.Environment.ContentRootPath);
var port = builder.Configuration.GetValue("Port", 8080);
Directory.CreateDirectory(filesPath);
builder.WebHost.UseUrls($"http://*:{port}");

var app = builder.Build();
var contentTypes = new FileExtensionContentTypeProvider();

// Visible files in the shared folder, sorted by name.
IEnumerable<FileInfo> ListFiles() => new DirectoryInfo(filesPath)
    .EnumerateFiles()
    .Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden) && !f.Name.StartsWith('.'))
    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

// Home page: list every file in the shared folder with a download link.
app.MapGet("/", () =>
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

    var html = $$"""
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
    return Results.Content(html, "text/html");
});

// JSON file list for programs (see the Client project).
app.MapGet("/api/files", () =>
    ListFiles().Select(f => new { f.Name, Size = f.Length, Modified = f.LastWriteTimeUtc }));

// Download a single file by name.
app.MapGet("/download/{name}", (string name) =>
{
    // Only allow plain file names inside the shared folder (blocks "../" tricks).
    var fullPath = Path.GetFullPath(Path.Combine(filesPath, name));
    if (Path.GetFileName(name) != name
        || Path.GetDirectoryName(fullPath) != filesPath.TrimEnd(Path.DirectorySeparatorChar)
        || !File.Exists(fullPath))
        return Results.NotFound("File not found.");

    if (!contentTypes.TryGetContentType(name, out var contentType))
        contentType = "application/octet-stream";

    return Results.File(fullPath, contentType, fileDownloadName: name, enableRangeProcessing: true);
});

app.Logger.LogInformation("Sharing files from {Path} on port {Port}", filesPath, port);
app.Run();

static string FormatSize(long bytes)
{
    string[] units = ["B", "KB", "MB", "GB", "TB"];
    double size = bytes;
    var i = 0;
    while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
    return $"{size:0.#} {units[i]}";
}
