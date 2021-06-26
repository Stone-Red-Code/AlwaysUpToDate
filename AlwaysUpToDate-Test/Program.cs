using System;
using System.Threading;
using System.Threading.Tasks;
using AlwaysUpToDate;

namespace AlwaysUpToDate_Test
{
    internal class Program
    {
        static private Updater updater = new Updater(new TimeSpan(0, 0, 1), "https://raw.githubusercontent.com/Stone-Red-Code/Test/main/test.json", "./", true);

        private async static Task Main(string[] args)
        {
            updater.Start();
            updater.ProgressChanged += Updater_ProgressChanged;
            updater.UpdateAvailible += Updater_UpdateAvailible;
            updater.NoUpdateAvailible += Updater_NoUpdateAvailible;

            await Task.Delay(-1);
        }

        private static void Updater_UpdateAvailible(string version, string additionalInformation)
        {
            updater.Stop();
            Console.WriteLine("New Update avalible: " + version);
            Console.WriteLine("Do you want to install the new update? (y/n)");

            char input = Console.ReadKey().KeyChar;

            if (char.ToLower(input) == 'y')
                updater.Update();
        }

        private static void Updater_ProgressChanged(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage)
        {
            Console.WriteLine($"{totalBytesDownloaded}/{totalFileSize}  {progressPercentage}%");
        }

        private static void Updater_NoUpdateAvailible()
        {
            Console.WriteLine("You are up to date");
        }
    }
}