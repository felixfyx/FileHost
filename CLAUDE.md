# FileHost

Reference code for serving one XML file over HTTP (`Server/`) and downloading, saving and deserializing it
(`Client/`). [README.md](README.md) explains the design, the settings and how to run it; read it first.

## Constraints

- **.NET Framework 4.8 only, C# 7.3, no NuGet packages.** Everything must be built into the framework.
  No newer language features: no `using var`, switch expressions, nullable reference types or target-typed `new`.
- **Main stays synchronous.** The user does not want `async Main`. Background work runs on its own thread and
  callers block only when they must (see `CatalogLoader.GetCatalog()`).
- **`Client/CatalogLoader.cs` is the class people copy into their own projects.** It takes all settings as
  constructor arguments and must not read `App.config` itself. `Program.cs` is only a demo of using it.

## Code style

Match the existing code:
- explicit types rather than `var`
- strings joined with `+`
- `_camelCase` private fields
- braces on their own lines
- comments that explain *why* in plain language, at the density already there

Keep README.md in step with any change in behaviour or settings.

## Working with the user

- **Never commit or push unless the user asks for that change.**
- The user develops on Windows now (they moved from a Mac, where things were tested with Mono). Run Windows-specific
  checks with Windows PowerShell 5.1 (`powershell.exe`), which uses the same .NET Framework networking code as the
  client. `pwsh` is PowerShell 7, which runs on modern .NET.
- Commands that need administrator rights (`netsh http add sslcert` / `urlacl`, importing into
  `Cert:\LocalMachine\Root`, `New-SelfSignedCertificate` into `LocalMachine\My`): prepare them and let the user run
  them in an elevated window.

## Build and run (Windows)

Needs the .NET Framework 4.8 Developer Pack (or Visual Studio's 4.8 targeting component).

```bat
dotnet build
Server\bin\Debug\net48\Server.exe
Client\bin\Debug\net48\Client.exe
```

To watch a refresh happen, set `RefreshMinutes` to `1` in `Client\bin\Debug\net48\Client.exe.config`. The interval is
in whole minutes.

## Current behaviour

- **Client:** `CatalogLoader` starts a background thread that downloads at once, then every `RefreshMinutes`.
  - `GetCatalog()` blocks only until the first download finishes. After that it returns the latest good catalog at once.
  - A failed refresh keeps the previous catalog and sets `LastRefreshError`. Before any success, `GetCatalog()` throws.
  - `Dispose()` stops the refreshes.
  - The demo prints the catalog on Enter and quits on `q`.
- **Server:** HTTP only (`http://+:<Port>/` in `Server/Program.cs`). It answers 503 with `Retry-After: 2` while the
  file can't be opened, and the client retries up to 5 times.

## Open work: customer's HTTPS problem (as of 2026-10-28)

A customer reports the client fails against their HTTPS URL. Their server certificate comes from a CA. With a CA the
client needs **no code change**: `HttpWebRequest` trusts whatever the Windows store on the client machine trusts.

**What is known:**
- The customer ran `Invoke-WebRequest -UseBasicParsing <url>` in Windows PowerShell 5.1 and got 200. So the server,
  the certificate and the network path work from wherever they ran it.
- On the Mac, against a self-signed HTTPS server, the client failed with `TrustFailure` as expected. Mono's TLS
  differs from Windows', so this says nothing about the customer's machine.

**Still unknown** (the client's own error line usually settles it):
- the exact line the client prints after `Download failed:`
- whether the 200 came from the client's machine, under the client's account, against the exact `Url` in
  `Client.exe.config`, with no `ServicePointManager.ServerCertificateValidationCallback` override
- whether the client runs interactively, as a service or as a scheduled task (another account means a different
  certificate store and proxy settings)

**Likely causes by error line:**

| Client prints | Look at |
|---|---|
| `Could not establish trust relationship ...` | CA not trusted for that account or machine, name mismatch, or the test didn't match the conditions above |
| `(403) Forbidden` | Client sends no `User-Agent` (`Invoke-WebRequest` does); set `request.UserAgent` |
| `(404) Not Found` | Path in `Url` differs from what was tested |
| `Unable to connect` / timeout | Proxy missing for a service account, firewall |
| `Could not save the file` | Account can't write to `OutputPath` (for example under Program Files) |

**Diagnostic script sent to the customer.** Not yet run on a real Windows machine; check it works first. They run it
in the folder with `Client.exe`, in Windows PowerShell 5.1, as the client's account:

```powershell
"PowerShell: " + $PSVersionTable.PSVersion
"User:       " + (whoami)
[xml]$config = Get-Content .\Client.exe.config
$url = ($config.configuration.appSettings.add | Where-Object key -eq "Url").value
"Url:        " + $url
"Check override (should be empty): " + [Net.ServicePointManager]::ServerCertificateValidationCallback
"Web request: " + (Invoke-WebRequest -UseBasicParsing $url).StatusCode
.\Client.exe
```

To see why a particular certificate is refused, connect with `SslStream` and a callback that records the
`SslPolicyErrors`. Then build an `X509Chain` with `RevocationMode = NoCheck` and print `ChainStatus`:
`UntrustedRoot` means the CA isn't in `LocalMachine\Root`, `PartialChain` means an intermediate is missing, and
`NotTimeValid` means the certificate has expired or the clock is wrong.

**Next steps the user is considering (none started):**
1. Print more detail on download failures in the client: `WebException.Status`, the inner exception, and the
   certificate's `SslPolicyErrors` and chain status, logged by a validation callback that doesn't change what's
   accepted.
2. Reproduce on Windows: create a test CA and a server certificate with `openssl`, serve `dummy.xml` over HTTPS (for
   example with Python `http.server` wrapped in `ssl`), and run the client before and after importing the CA into
   `Cert:\LocalMachine\Root`. Then try another account and a URL with an IP address.
3. Optional thumbprint pinning in the client: a `CertificateThumbprint` setting, possibly a comma-separated list so
   renewal can overlap. Use `errors == SslPolicyErrors.None || <thumbprint matches>`. Never accept every certificate.
4. HTTPS support in `Server.exe` (an `https://` prefix; the certificate is bound with `netsh http add sslcert`).

Setup instructions for the customer's server administrator (self-signed certificate, binding, renewal) are in a
private Claude doc, "HTTPS Certificate Setup for the Catalog Server": https://claude.ai/artifact/BurLLgDD13uxjUrdMXLyvR
