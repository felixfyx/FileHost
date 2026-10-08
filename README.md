# FileHost

Reference code for serving one XML file over HTTP and downloading and deserializing it, in **C# for .NET Framework 4.8** with no third-party libraries.

- **Server** (`Server/`) serves a single XML file using `HttpListener`, to several clients at once.
- **Client** (`Client/`) downloads that file on a background thread with `HttpWebRequest`, saves a copy, turns it into C# objects with `XmlSerializer`, and downloads it again every few minutes.

Everything used is built into .NET Framework 4.8. The code uses C# 7.3, the default language version for 4.8 projects.

## Files

| File | What it is |
|------|------------|
| [`Server/Program.cs`](Server/Program.cs) | The whole server. |
| [`Server/App.config`](Server/App.config) | Server settings: `Port`, `FilePath`. |
| [`Server/dummy.xml`](Server/dummy.xml) | The sample XML file being served. |
| [`Client/CatalogLoader.cs`](Client/CatalogLoader.cs) | Downloads, saves and deserializes the XML on a background thread, and refreshes it on a timer. The class to reuse. |
| [`Client/Program.cs`](Client/Program.cs) | Demo of using `CatalogLoader`: start it, then print the current catalog whenever you press Enter. |
| [`Client/Catalog.cs`](Client/Catalog.cs) | C# classes matching the structure of `dummy.xml`. |
| [`Client/App.config`](Client/App.config) | Client settings: `Url`, `OutputPath`, `TimeoutSeconds`, `RefreshMinutes`. |

## Copying into your own project

**Server:** copy `Program.cs`, add the `appSettings` from `App.config` to your project's `App.config`, and add a reference to **System.Configuration** (Add Reference → Assemblies → Framework).

**Client:** copy `CatalogLoader.cs` and `Catalog.cs`, replacing `Catalog.cs` with classes for your own XML. `CatalogLoader` takes the URL, output path, timeout and refresh interval as constructor arguments, so it needs no settings or extra references. `Program.cs` shows how to use it, and reads those values from `App.config` with **System.Configuration**.

`HttpListener`, `HttpWebRequest` and `XmlSerializer` are in `System.dll` and `System.Xml.dll`, which every .NET Framework project already references.

## Settings

Settings are read from `App.config`, which becomes `Server.exe.config` or `Client.exe.config` next to the program. You can change them there without rebuilding. Relative paths are relative to the folder the `.exe` is in.

**Server:**

| Setting    | Default     | Meaning |
|------------|-------------|---------|
| `Port`     | `8080`      | Port to listen on. |
| `FilePath` | `dummy.xml` | The XML file to serve. It's available at `http://<server>:<Port>/<file name>`. |

**Client:**

| Setting          | Default                           | Meaning |
|------------------|-----------------------------------|---------|
| `Url`            | `http://localhost:8080/dummy.xml` | Full address of the XML file. |
| `OutputPath`     | `dummy.xml`                       | Where to save the downloaded copy. |
| `TimeoutSeconds` | `30`                              | Give up if the server doesn't respond, or stops sending data, for this long. |
| `RefreshMinutes` | `5`                               | Download the file again this often. |

## Running it

**On Windows** (Visual Studio or the command line):

```bat
dotnet build
Server\bin\Debug\net48\Server.exe
Client\bin\Debug\net48\Client.exe
```

Run the server and client in separate windows. The client prints:

```
Downloading http://localhost:8080/dummy.xml now and every 5 minute(s). Ready yet? False
Saved 864 bytes to ...\Client\bin\Debug\net48\dummy.xml

Demo Store (updated 2026-09-30 12:00:00Z), 3 product(s):
  #1001 Wireless Mouse [Hardware] 24.99 USD, qty 150, in stock, tags: peripheral, bluetooth
  #1002 Mechanical Keyboard [Hardware] 89.50 USD, qty 42, in stock, tags: peripheral, rgb
  #2001 Photo Editor Pro [Software] 49.00 USD, qty 0, out of stock, tags: license

Press Enter to print the current catalog again, or type q and press Enter to quit.
```

Press Enter after a refresh to see any changes to the server's file.

**On macOS or Linux**, build the same way and run the programs with [Mono](https://www.mono-project.com/): `mono Server/bin/Debug/net48/Server.exe`, `mono Client/bin/Debug/net48/Client.exe`.

## When the file is ready

### Client: waiting for the download

Creating a `CatalogLoader` starts a background thread and returns immediately. The thread downloads the file right away, then again every `RefreshMinutes`, until the loader is disposed:

```csharp
using (CatalogLoader loader = new CatalogLoader(url, outputPath, 30, 5))   // returns instantly
{
    // ...do other work...

    if (loader.IsReady) { /* a catalog has loaded, no waiting */ }
    Catalog catalog = loader.GetCatalog();   // waits only until the first download is done
}
```

`GetCatalog()` is an ordinary blocking call, so no `async` code is needed. It waits only until the first download finishes. After that it returns the latest catalog immediately, even while a refresh is running. Each refresh creates a new `Catalog` object, so a catalog you already have never changes underneath you. Call `GetCatalog()` again to get the newest one.

If no download has succeeded yet, `GetCatalog()` throws what went wrong, and the thread tries again at the next refresh. Once a catalog has loaded, a failed refresh keeps the previous catalog and puts the error in `LastRefreshError`, which goes back to `null` after the next successful download.

Downloads happen one after another on the same thread, so they never overlap. The saved file is also safe to read at any time. The new version is written to `<OutputPath>.tmp` and swapped in with `File.Replace` only once it's complete. Anything opening the file sees either the old complete copy or the new complete copy, never a missing or half-written one.

The download uses the normal blocking `HttpWebRequest` calls on its own thread instead of using its async methods. In .NET Framework, `Timeout` and `ReadWriteTimeout` only apply to the blocking calls.

### Server: updating the file while clients download

The server handles each request on its own, so a slow client doesn't hold up anyone else. To update the XML file while the server is running:

1. **Write the new version under a different name** in the same folder, for example `dummy.xml.new`.
2. **Then replace the old file in one step**, by deleting it and renaming the new one, or with `File.Replace`.

Downloads already in progress finish with the old version, and new requests get the new one. This works because the server opens the file with `FileShare.Delete`.

If a client asks while the file can't be opened (missing for a moment mid-replace, or being written directly), the server answers **503 Service Unavailable** with `Retry-After: 2`. The client waits 2 seconds and tries again, up to 5 times. Both numbers are constants at the top of `Client/CatalogLoader.cs`.

Avoid editing the file in place (opening `dummy.xml` itself in an editor and saving). On Windows the save fails with "file in use" while any download is running, because the server holds the file open to read it.

## Large files

The server and client both stream the file in small chunks, straight from disk and straight to disk. Memory use for the transfer stays low however large the file is, and there's no size limit beyond disk space.

Deserializing is different. `XmlSerializer` builds a C# object for every element, and those take several times the size of the XML in memory. For example, a list of 1 million users could need a few hundred MB to 1 GB of RAM. For files that size:

- **Build as 64-bit.** Visual Studio's classic console template turns on "Prefer 32-bit" (Project Properties → Build). A 32-bit process can run out of memory at a few hundred MB, so turn that setting off.
- **If even 64-bit needs too much memory,** read the file one record at a time with `XmlReader` instead of loading the whole list with `XmlSerializer`.

## Windows: administrator rights

The server listens on every network address so other computers can connect. Windows only allows that if the server runs as administrator, or if the address has been reserved once from an administrator command prompt:

```bat
netsh http add urlacl url=http://+:8080/ user=Everyone
```

Without either, the server stops at startup with "Access is denied".

## If something goes wrong

If a refresh fails after a catalog has loaded, the client prints `Latest refresh failed, showing the previous catalog:` with the error, then the previous catalog. If no catalog has loaded yet, it prints one of these lines instead and tries again at the next refresh:

| Message | Cause |
|---------|-------|
| `Download failed: Unable to connect to the remote server` | The server isn't running, or `Url` has the wrong address or port. |
| `Download failed: The remote server returned an error: (404) Not Found.` | The file name in `Url` doesn't match the server's file. |
| `Download failed: The operation has timed out.` | The server didn't respond, or stopped sending data, for `TimeoutSeconds`. |
| `Download failed: The remote server returned an error: (503) Server Unavailable.` | The server's file was unavailable (being replaced, written to, or deleted) for all 5 attempts. |
| `Could not save the file: ...` | The output file couldn't be replaced, for example because another program has it open without allowing it to be replaced. |
| `Download failed: Download incomplete: received X of Y bytes.` | The connection dropped partway through. The previously saved file, if any, is left as it was. |
| `Could not read the XML: ...` | The file isn't valid XML, or doesn't match the classes in `Catalog.cs`. |

If you change the structure of the XML, update `Catalog.cs` to match.

> **There is no login.** Anyone who can reach the server can download the file. Only run it on a network you trust.
