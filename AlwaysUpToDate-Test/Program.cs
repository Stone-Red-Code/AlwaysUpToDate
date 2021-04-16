using System;
using AlwaysUpToDate;

namespace AlwaysUpToDate_Test
{
    internal class Program
    {
        static private Updater updater = new Updater(new TimeSpan(0, 0, 1), "https://raw.githubusercontent.com/Stone-Red-Code/Test/main/test.json", "./", false);

        private static void Main(string[] args)
        {
            updater.Start();
            updater.ProgressChanged += Updater_ProgressChanged;
            updater.UpdateAvailible += Updater_UpdateAvailible;

            Console.ReadLine();
        }

        private static void Updater_UpdateAvailible(string version, string additionalInformationUrl)
        {
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
    }
}