# AlwaysUpToDate
> A simple .NET auto updater

## Download

### Package Manager
`Install-Package AlwaysUpToDate`

### .NET CLI
`dotnet add package AlwaysUpToDate`

### NuGet
https://www.nuget.org/packages/AlwaysUpToDate

## How it works
AlwaysUpToDate downloads an XML manifest file containing update information from your server.
The manifest is used to determine if an update is available. If the version in the manifest is greater than the assembly version of your application then either the update event gets triggered or, if the update is marked as mandatory, it gets downloaded and installed instantly.
After the install the new version will be started automatically.

## More detailed how it works
After you start the updater, it will check at the specified time interval if a new version is available by downloading the XML manifest.\
If the latest version is greater than the current assembly version (not the assembly file version!), and the update is not mandatory, the `UpdateAvailable` event will be triggered and you can handle the timing of the update yourself. If the update is mandatory, the new version will be downloaded instantly without asking.\
The updater matches manifest items by OS and processor architecture. It first looks for an exact architecture match (e.g., `osx-arm64`), then falls back to a generic OS entry (e.g., `linux` with no architecture).\
While extracting the zip file, all existing files that have the same name/path as in the zip file will get marked as old (by adding `_OLD_` to the name). After the extraction, the updater tries to delete all files marked as old. Files it is unable to delete will remain and the updater tries to delete them at the next update. After that it will start the new version (for that to work the executable name has to be the same as the old one).

## Usage
### Creating the XML manifest
The manifest is an XML file with one `<item>` per target platform.

| Element | Required | Description |
|---|---|---|
| `os` | Yes | Target platform. Can be just an OS (`windows`, `linux`, `macos`/`osx`) or a combined OS-architecture value (`osx-arm64`, `windows-x64`, `linux-arm64`, etc.). |
| `arch` | No | Target architecture (`x86`, `x64`, `arm`, `arm64`, `any`). Overrides any architecture embedded in `<os>`. Only needed when using the separate-element format. |
| `version` | Yes | Version in `X.X.X.X` format. |
| `url` | Yes | URL to the update ZIP file. Supports an optional `rootPath` attribute to specify a path prefix inside the ZIP to treat as the extraction root (e.g., `<url rootPath="release/net8.0">...</url>`). When omitted, the updater auto-detects a single common root folder. |
| `changelog` | No | URL pointing to a changelog. Passed to the `UpdateAvailable` event. |
| `mandatory` | No | `true` or `false` (default `false`). Mandatory updates are installed immediately. |
| `checksum` | No | Hex-encoded hash of the ZIP file. The `algorithm` attribute can be `SHA1`, `MD5`, `SHA256`, or `SHA512`. |

#### Combined OS-architecture format
Architecture is embedded in the `<os>` value. This is the simplest format and recommended for most use cases.
```xml
<updates>
    <item>
        <os>linux</os>
        <version>1.0.0.0</version>
        <url>https://example.com/app-linux-x64.zip</url>
        <changelog>https://example.com/changelog/v1.0.0.html</changelog>
        <mandatory>false</mandatory>
        <checksum algorithm="SHA256">abc123...</checksum>
    </item>
    <item>
        <os>osx-arm64</os>
        <version>1.0.0.0</version>
        <url>https://example.com/app-osx-arm64.zip</url>
    </item>
    <item>
        <os>osx-x64</os>
        <version>1.0.0.0</version>
        <url>https://example.com/app-osx-x64.zip</url>
    </item>
    <item>
        <os>windows</os>
        <version>1.0.0.0</version>
        <url>https://example.com/app-win-x64.zip</url>
    </item>
</updates>
```

#### Separate element format
OS and architecture are specified in separate elements.
```xml
<updates>
    <item>
        <os>macos</os>
        <arch>arm64</arch>
        <version>1.0.0.0</version>
        <url>https://example.com/app-osx-arm64.zip</url>
    </item>
</updates>
```

#### Supported OS values
`windows` / `win`, `macos` / `osx`, `linux`

#### Supported architecture values
`x86`, `x64`, `arm`, `arm64`, `any` (default when omitted)

### Creating new `Updater` and starting it
```cs
Updater updater = new Updater(
    new TimeSpan(0, 0, 1), // Update interval
    "https://example.com/updates.xml", // XML manifest URL
    "./" // Install path
);
updater.Start();
```
### Adding event handlers
```cs
updater.ProgressChanged += Updater_ProgressChanged;
updater.UpdateAvailable += Updater_UpdateAvailable;
updater.UpdateStarted += Updater_UpdateStarted;
updater.NoUpdateAvailable += Updater_NoUpdateAvailable;
updater.OnException += Updater_OnException;

private static void Updater_UpdateAvailable(string version, string changelogUrl)
{
}

private static void Updater_UpdateStarted(string version)
{
}

private static void Updater_NoUpdateAvailable()
{
}

private static void Updater_OnException(Exception exception)
{
}

private static void Updater_ProgressChanged(UpdateStep step, long itemsProcessed, long? totalItems, double? progressPercentage)
{
}
```
### Handling the update
```cs
private static async void Updater_UpdateAvailable(string version, string changelogUrl)
{
    Console.WriteLine("New update available: " + version);
    if (!string.IsNullOrEmpty(changelogUrl))
    {
        Console.WriteLine("Changelog: " + changelogUrl);
    }

    Console.WriteLine("Do you want to install the new update? (y/n)");

    char input = Console.ReadKey().KeyChar;

    if (char.ToLower(input) == 'y')
    {
        await updater.UpdateAsync();
    }
}
```
### Update process reporting
```cs
private static void Updater_ProgressChanged(UpdateStep step, long itemsProcessed, long? totalItems, double? progressPercentage)
{
    Console.WriteLine($"[{step}] {itemsProcessed}/{totalItems}  {progressPercentage}%");
}
```
