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
AlwaysUpToDate downloads an json information file containing update information from your server.
The file gets used to determent if an update is available. If the version in the file is greater than the assembly version of your application then either the update event gets triggered or you can decide if you want to download the update or if the update is marked as mandatory it gets downloaded and installed instantly.
After the install the new version will be started automatically.

## More detailed how it works
After you started the updater, it will check at the specified time interval if a new version is available by downloading the json file.\
If the latest version of the software is greater the current assembly version (Not assembly file version!). If the update is not mandatory the `UpdateAvailible` will be triggered and you can handle the time of the update yourself. If the update is mandatory, the new version will be downloaded instantly without asking.\
While extracting the zip file all the files that have the same name/path as in the zip file will get marked as old(add `_OLD_` to the name). After the extraction, the installer tries to delete all files marked as old. The files it is unable to delete will just sit there and the installer tries to delete them at th next update. After that it will try to start the new version (for that to work the executable name has to be the same as the old one).

## Usage
### Creating json file
- Version (Required): The version should have this format `X.X.X.X`
- FileUrl (Required): Url to the newest installer version zip file
- Mandatory (Optional): Should be either true or false. The default is false
```json
{
   "Version":"version",
   "FileUrl":"url",
   "Mandatory":false
}
```

### Creating new `Updater` and starting it
```cs                           
Updater downloader = new Updater(
new TimeSpan(0, 0, 1), //Update interval
"https://raw.githubusercontent.com/Stone-Red-Code/Test/main/test.json", //Json file url
"./" //Install path
);
updater.Start();
```
### Adding event handlers
```cs
...
    updater.ProgressChanged += Updater_ProgressChanged;
    updater.UpdateAvailible += Updater_UpdateAvailible;
}

private static void Downloader_UpdateAvailible(string version, string additionalInformationUrl)
{
}

private static void Downloader_ProgressChanged(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage)
{
}
```
### Handeling the update
```
private static void Updater_UpdateAvailible(string version, string additionalInformationUrl)
{
    Console.WriteLine("New Update avalible: " + version);
    Console.WriteLine("Do you want to install the new update? (y/n)");

    char input = Console.ReadKey().KeyChar;

    if (char.ToLower(input) == 'y')
        updater.Update();
}
```
### Download process reporting
```
private static void Updater_ProgressChanged(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage)
{
    Console.WriteLine($"{totalBytesDownloaded}/{totalFileSize}  {progressPercentage}%");
}
```
