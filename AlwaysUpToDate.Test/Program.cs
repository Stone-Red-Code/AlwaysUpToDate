using System;
using System.Threading.Tasks;

namespace AlwaysUpToDate.Test;

internal class Program
{
    private static readonly Updater updater = new Updater(new TimeSpan(0, 0, 1), "https://echohub.voidcube.cloud/api/app/version", true);

    private static async Task Main(string[] args)
    {
        updater.ProgressChanged += Updater_ProgressChanged;
        updater.UpdateAvailable += Updater_UpdateAvailable;
        updater.UpdateStarted += Updater_UpdateStarted;
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
            await updater.UpdateAsync();
        }
    }

    private static void Updater_ProgressChanged(UpdateStep step, long itemsProcessed, long? totalItems, double? progressPercentage)
    {
        Console.WriteLine($"[{step}] {itemsProcessed}/{totalItems}  {progressPercentage}%");
    }

    private static void Updater_UpdateStarted(string version)
    {
        Console.WriteLine($"Starting update to version {version}...");
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