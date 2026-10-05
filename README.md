# FileHost

A small C# file server and demo client. The server shares a folder over HTTP, and clients can download files from it with a browser or with the included client program.

- **Server** lists the files in a folder on a web page and lets anyone who can reach it download them.
- **Client** is a console app that gets the file list, downloads the files, and reads any catalog XML files into C# objects.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer to build.
- The server and client each run on **.NET 8** (Windows, macOS or Linux) **or .NET Framework 4.8**. See [Running on .NET Framework 4.8](#running-on-net-framework-48).

## Quickstart

1. **Start the server** from the repo root:

   ```bash
   dotnet run --project Server -f net8.0
   ```

   It prints the folder it's sharing and the port:

   ```
   Sharing files from /.../FileHost/Server/SharedFiles on port 8080. Press Ctrl+C to stop.
   ```

2. **Open the file list in a browser** at <http://localhost:8080>. You should see `dummy.xml`, the sample file that comes with the repo. Click a file name to download it.

3. **Run the client** in a second terminal:

   ```bash
   dotnet run --project Client -f net8.0
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
| `FilesPath` | `SharedFiles` | Folder to share. Use a full path, or a relative path, which is taken from the folder that holds `appsettings.json`. |

The server reads `appsettings.json` from the folder you start it in if there is one there, and otherwise from the program's own folder. `dotnet run` starts it in `Server/`, so the defaults share `Server/SharedFiles`.

You can override either setting for one run from the command line:

```bash
dotnet run --project Server -f net8.0 -- --FilesPath ~/Desktop/ToShare --Port 9000
```

## Client options

```bash
dotnet run --project Client -f net8.0 -- [--server http://host:port] [--output folder] [--timeout seconds] [file names...]
```

| Option      | Default                 | Meaning |
|-------------|-------------------------|---------|
| `--server`  | `http://localhost:8080` | Address of the server. |
| `--output`  | `Downloads`             | Folder to save files into (created if missing). |
| `--timeout` | `30`                    | Seconds to wait for the server to respond, and the longest a download may go without receiving any data. A slow download that keeps making progress is never cut off. |
| file names  | all files               | Download only these files. |

**With file names**, the client requests each file directly and doesn't fetch the server's file list. A file that isn't on the server is reported as `MISSING`. **Without file names**, it fetches the list, prints it, and downloads everything.

Each file ends with one of these results:

| Result    | Meaning |
|-----------|---------|
| `OK`      | Downloaded. |
| `MISSING` | The server doesn't have a file with that name. |
| `TIMEOUT` | The server didn't respond, or stopped sending data, for `--timeout` seconds. |
| `FAIL`    | Any other error, such as a dropped connection. |

A file that doesn't finish downloading is never left behind half-written.

Examples:

```bash
# Download everything from another machine on the network
dotnet run --project Client -f net8.0 -- --server http://192.168.1.20:8080

# Download only dummy.xml into a folder on the Desktop
dotnet run --project Client -f net8.0 -- --output ~/Desktop/files dummy.xml

# Give up on a server that doesn't answer within 5 seconds
dotnet run --project Client -f net8.0 -- --timeout 5
```

The client exits with code `1` if any file isn't `OK`, an XML file can't be read, or the server can't be reached, times out, or sends a file list that isn't valid JSON.

## Running on .NET Framework 4.8

Both projects are built for .NET 8 and .NET Framework 4.8. `dotnet build` produces both versions of each:

```
Server/bin/Debug/net8.0/Server.dll     Server/bin/Debug/net48/Server.exe
Client/bin/Debug/net8.0/Client.dll     Client/bin/Debug/net48/Client.exe
```

Because of this, `dotnet run` needs `-f` to say which version to run, as in the examples above.

**On a Windows machine with .NET Framework 4.8**, copy the whole `net48` folder over and run the `.exe` inside it. The server's `net48` folder already contains `appsettings.json`, and a relative `FilesPath` is taken from that folder.

```bat
Server.exe
Client.exe --server http://192.168.1.20:8080
```

On macOS or Linux, you can run the 4.8 builds with [Mono](https://www.mono-project.com/): `mono Server.exe`, `mono Client.exe --server ...`.

**Windows and other computers:** Windows only lets a program accept connections from other computers if it runs as administrator or the port has been reserved for it. If neither is true, the server still starts but only accepts connections from the same computer, and it prints the command that fixes this. Run it once in an administrator command prompt:

```bat
netsh http add urlacl url=http://+:8080/ user=Everyone
```

To use the client code inside your own .NET Framework 4.8 project, copy `Program.cs` and `Catalog.cs`, and add a reference to `System.Net.Http` and the [`System.Net.Http.Json`](https://www.nuget.org/packages/System.Net.Http.Json) NuGet package.

## Downloading from other computers

The server accepts connections from other machines, not just your own. Find your computer's IP address (on macOS, System Settings → Wi-Fi → Details), then open `http://<that-ip>:8080` on the other machine or pass it to the client with `--server`. On Windows, see the note on administrator rights in [Running on .NET Framework 4.8](#running-on-net-framework-48). The first time you run the server, macOS or Windows may ask whether to allow incoming connections. Choose **Allow**.

> **There is no login.** Anyone who can reach the server can download every file in the shared folder. Only run it on a network you trust.

## Server endpoints

| Address            | Returns |
|--------------------|---------|
| `/`                | Web page listing the files. |
| `/api/files`       | JSON list of files: `[{ "name": ..., "size": ..., "modified": ... }]`. |
| `/download/{name}` | The file as a download. |

## Reading the XML in the client

[`Client/Catalog.cs`](Client/Catalog.cs) contains C# classes that match the structure of [`Server/SharedFiles/dummy.xml`](Server/SharedFiles/dummy.xml). After downloading, the client uses .NET's `XmlSerializer` to turn each `.xml` file whose top element is `<Catalog>` into these objects. Other XML files are skipped.

If you change the structure of the XML, update the classes in `Catalog.cs` to match.

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Page says **"No files available."** | The shared folder is empty. Check the `Sharing files from ...` line the server prints, and put files in that folder. |
| `localhost` doesn't load, or IPv6 addresses don't work (macOS/Linux) | On macOS and Linux the server only listens on IPv4. Browsers normally fall back to IPv4 automatically; if one doesn't, use `http://127.0.0.1:8080`. On Windows, IPv6 works. |
| **403 Forbidden** on port 5000 | On macOS, AirPlay Receiver uses port 5000. Use a different port. |
| Client says **"Could not reach ..."** | The server isn't running, or the address or port is wrong. |
| Server says **"Not allowed to listen on all addresses"** (Windows) | Other computers can't connect yet. Run the `netsh` command it prints once as administrator, or run the server as administrator. |
| **Address already in use** when starting | Another program, or an earlier copy of the server, is using the port. Stop it or pick another `Port`. |
