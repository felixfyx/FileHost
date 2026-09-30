# FileHost

A small C# file server and demo client. The server shares a folder over HTTP, and clients can download files from it with a browser or with the included client program.

- **Server** lists the files in a folder on a web page and lets anyone who can reach it download them.
- **Client** is a console app that gets the file list, downloads the files, and reads any catalog XML files into C# objects.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer

## Quickstart

1. **Start the server** from the repo root:

   ```bash
   dotnet run --project Server
   ```

   It prints the folder it's sharing and the port:

   ```
   Sharing files from /.../FileHost/Server/SharedFiles on port 8080
   ```

2. **Open the file list in a browser** at <http://localhost:8080>. You should see `dummy.xml`, the sample file that comes with the repo. Click a file name to download it.

3. **Run the client** in a second terminal:

   ```bash
   dotnet run --project Client
   ```

   It downloads every file into `./Downloads` and prints the contents of `dummy.xml`:

   ```
   dummy.xml: "Demo Store", updated 2026-09-30 12:00:00Z, 3 product(s)
     #1001 Wireless Mouse         Hardware     24.99 USD  qty  150  in stock     [peripheral, bluetooth]
     #1002 Mechanical Keyboard    Hardware     89.50 USD  qty   42  in stock     [peripheral, rgb]
     #2001 Photo Editor Pro       Software     49.00 USD  qty    0  out of stock [license]
   ```

To share your own files, put them in `Server/SharedFiles`. They show up as soon as you refresh the page, with no restart needed.

## Server settings

Settings are in [`Server/appsettings.json`](Server/appsettings.json):

```json
{
  "Port": 8080,
  "FilesPath": "SharedFiles"
}
```

| Setting     | Default       | Meaning |
|-------------|---------------|---------|
| `Port`      | `8080`        | Port the server listens on. |
| `FilesPath` | `SharedFiles` | Folder to share. Use a full path, or a relative path, which is taken from the `Server/` folder no matter where you start the server from. |

You can override either setting for one run from the command line:

```bash
dotnet run --project Server -- --FilesPath ~/Desktop/ToShare --Port 9000
```

## Client options

```bash
dotnet run --project Client -- [--server http://host:port] [--output folder] [file names...]
```

| Option     | Default                 | Meaning |
|------------|-------------------------|---------|
| `--server` | `http://localhost:8080` | Address of the server. |
| `--output` | `Downloads`             | Folder to save files into (created if missing). |
| file names | all files               | Download only these files. |

Examples:

```bash
# Download everything from another machine on the network
dotnet run --project Client -- --server http://192.168.1.20:8080

# Download only dummy.xml into a folder on the Desktop
dotnet run --project Client -- --output ~/Desktop/files dummy.xml
```

The client exits with code `1` if a download fails, a requested file isn't on the server, an XML file can't be read, or the server can't be reached.

## Downloading from other computers

The server accepts connections from other machines, not just your own. Find your computer's IP address (on macOS, System Settings → Wi-Fi → Details), then open `http://<that-ip>:8080` on the other machine or pass it to the client with `--server`. The first time you run the server, macOS may ask whether to allow incoming connections. Choose **Allow**.

> **There is no login.** Anyone who can reach the server can download every file in the shared folder. Only run it on a network you trust.

## Server endpoints

| Address            | Returns |
|--------------------|---------|
| `/`                | Web page listing the files. |
| `/api/files`       | JSON list of files: `[{ "name": ..., "size": ..., "modified": ... }]`. |
| `/download/{name}` | The file as a download (supports resuming). |

## Reading the XML in the client

[`Client/Catalog.cs`](Client/Catalog.cs) contains C# classes that match the structure of [`Server/SharedFiles/dummy.xml`](Server/SharedFiles/dummy.xml). After downloading, the client uses .NET's `XmlSerializer` to turn each `.xml` file whose top element is `<Catalog>` into these objects. Other XML files are skipped.

If you change the structure of the XML, update the classes in `Catalog.cs` to match.

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Page says **"No files available."** | The shared folder is empty. Check the `Sharing files from ...` line the server prints, and put files in that folder. |
| **403 Forbidden** on port 5000 | On macOS, AirPlay Receiver uses port 5000. Use a different port. |
| Client says **"Could not reach ..."** | The server isn't running, or the address or port is wrong. |
| **Address already in use** when starting | Another program, or an earlier copy of the server, is using the port. Stop it or pick another `Port`. |
