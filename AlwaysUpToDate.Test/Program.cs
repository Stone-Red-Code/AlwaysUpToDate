using System;
using System.Threading.Tasks;

namespace AlwaysUpToDate.Test;

internal class Program
{
    private static readonly Updater updater = new Updater(new TimeSpan(0, 0, 1), "https://raw.githubusercontent.com/Stone-Red-Code/Test/main/test.xml", true);

    private static async Task Main(string[] args)
    {
        updater.ProgressChanged += Updater_ProgressChanged;
        updater.UpdateAvailable += Updater_UpdateAvailable;
        updater.NoUpdateAvailable += Updater_NoUpdateAvailable;
        updater.OnException += Updater_OnException;
        updater.Start();

        await Task.Delay(-1);
    }

    private static async void Updater_UpdateAvailable(string version, string changelogUrl)
    {
        updater.Stop();
        Console.WriteLine("New Update available: " + version);
        if (!string.IsNullOrEmpty(changelogUrl))
        {
            Console.WriteLine("Changelog: " + changelogUrl);
        }

        Console.WriteLine("Do you want to install the new update? (y/n)");

        char input = Console.ReadKey().KeyChar;

        if (char.ToLower(input) == 'y')
        {
            await updater.Update();
        }
    }

    private static void Updater_ProgressChanged(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage)
    {
        Console.WriteLine($"{totalBytesDownloaded}/{totalFileSize}  {progressPercentage}%");
    }

    private static void Updater_NoUpdateAvailable()
    {
        Console.WriteLine("You are up to date!");
    }

    private static void Updater_OnException(Exception exception)
    {
        Console.WriteLine(exception.ToString());
    }
}