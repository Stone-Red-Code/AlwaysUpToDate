using System;
using AlwaysUpToDate;

namespace AlwaysUpToDate_Test
{
    internal class Program
    {
        static private Updater downloader = new Updater(new TimeSpan(0, 0, 1), "https://raw.githubusercontent.com/Stone-Red-Code/Test/main/test.json", "./", false);

        private static void Main(string[] args)
        {
            downloader.Start();
            downloader.ProgressChanged += Downloader_ProgressChanged;
            downloader.UpdateAvailible += Downloader_UpdateAvailible;

            Console.ReadLine();
        }

        private static void Downloader_UpdateAvailible(string version, string additionalInformationUrl)
        {
            Console.WriteLine("New Update: " + version);
            downloader.Update();
        }

        private static void Downloader_ProgressChanged(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage)
        {
            Console.WriteLine($"{totalBytesDownloaded}/{totalFileSize}  {progressPercentage}%");
        }
    }
}