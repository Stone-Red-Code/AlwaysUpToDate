using System;
using System.IO;
using System.Net.Http;
using System.Timers;
using System.Text.Json;
using System.Reflection;
using System.IO.Compression;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AlwaysUpToDate
{
    public class Updater
    {
        public delegate void UpdaterChangedHandler(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage);

        public event UpdaterChangedHandler ProgressChanged;

        public delegate void UpdateAvalibleHandler(string version, string additionalInformation);

        public event UpdateAvalibleHandler UpdateAvailible;

        private readonly Timer updateTimer = new Timer();
        private readonly string updateInfoUrl;
        private readonly string installPath;
        private string updateUrl;
        private bool updaing = false;

        public Updater(TimeSpan interval, string updateInfoUrl, string installPath = "./", bool onlyUpdateOnce = false)
        {
            this.updateInfoUrl = updateInfoUrl;
            this.installPath = installPath;

            if (interval.TotalMilliseconds > 0)
                updateTimer.Interval = interval.TotalMilliseconds;

            if (!onlyUpdateOnce)
                updateTimer.Elapsed += UpdateTimer_Elapsed;
        }

        public void Start()
        {
            if (updateTimer.Interval > 0)
                updateTimer.Start();
            UpdateTimer_Elapsed(null, null);
        }

        public void Stop()
        {
            updateTimer.Stop();
        }

        public async void Update()
        {
            if (!string.IsNullOrWhiteSpace(updateUrl) && !updaing)
                await DownloadFile();
        }

        public async void UpdateTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            using HttpClient client = new HttpClient();
            HttpResponseMessage response = await client.GetAsync(updateInfoUrl);

            Stream stream = await response.Content.ReadAsStreamAsync();
            UpdateInfo updateInfo = await JsonSerializer.DeserializeAsync<UpdateInfo>(stream);

            Version assemblyVersion = Assembly.GetEntryAssembly().GetName().Version;
            bool succes = Version.TryParse(updateInfo.Version, out Version version);

            if (!succes)
                return;

            if (version > assemblyVersion && !updaing)
            {
                updateUrl = updateInfo.FileUrl;
                if (!updateInfo.Mandatory)
                {
                    UpdateAvailible(updateInfo.Version, updateInfo.AdditionalInformation);
                }
                else
                {
                    Update();
                }
            }
        }

        private async Task DownloadFile()
        {
            updateTimer.Stop();
            updaing = true;
            using HttpClient client = new HttpClient();

            HttpResponseMessage response = client.GetAsync(updateUrl).Result;
            using Stream contentStream = response.Content.ReadAsStreamAsync().Result;
            await ProcessContentStream(response.Content.Headers.ContentLength, contentStream);
        }

        private void ExtractZipFile()
        {
            ZipArchive zipArchive = ZipFile.OpenRead(Path.Join(installPath, "Update.zip"));
            string executablePath = Process.GetCurrentProcess().MainModule.FileName;
            Console.WriteLine(executablePath);

            foreach (ZipArchiveEntry entry in zipArchive.Entries)
            {
                if (File.Exists(Path.Join(installPath, entry.FullName)))
                {
                    string moveName = entry.FullName;
                    while (File.Exists(Path.Join(installPath, moveName)))
                    {
                        moveName += "_OLD_";
                    }
                    File.Move(Path.Join(installPath, entry.FullName), Path.Join(installPath, moveName));
                }
                if (entry.FullName.EndsWith("/"))
                {
                    if (!Directory.Exists(Path.Join(installPath, entry.FullName)))
                        Directory.CreateDirectory(Path.Join(installPath, entry.FullName));
                }
                else
                {
                    entry.ExtractToFile(Path.Join(installPath, entry.FullName), true);
                }
            }
            zipArchive.Dispose();

            foreach (string filePath in Directory.GetFiles(installPath, "*.*", SearchOption.AllDirectories))
            {
                if (filePath.Contains("_OLD_"))
                {
                    try
                    {
                        File.Delete(filePath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                }
            }

            File.Delete(Path.Join(installPath, "Update.zip"));
            Process.Start(executablePath);
            Environment.Exit(0);
        }

        private async Task ProcessContentStream(long? totalDownloadSize, Stream contentStream)
        {
            long totalBytesRead = 0;
            int readCount = 0;
            byte[] buffer = new byte[8192];
            bool isMoreToRead = true;

            using var fileStream = new FileStream($"{installPath}Update.zip", FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            do
            {
                int bytesRead = contentStream.ReadAsync(buffer, 0, buffer.Length).Result;
                if (bytesRead == 0)
                {
                    isMoreToRead = false;
                    TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                    continue;
                }

                await fileStream.WriteAsync(buffer, 0, bytesRead);

                totalBytesRead += bytesRead;
                readCount += 1;

                if (readCount >= 10)
                {
                    readCount = 0;
                    TriggerProgressChanged(totalDownloadSize, totalBytesRead);
                }
            }
            while (isMoreToRead);
            fileStream.Close();
            ExtractZipFile();
        }

        private void TriggerProgressChanged(long? totalDownloadSize, long totalBytesRead)
        {
            if (ProgressChanged == null)
                return;

            double? progressPercentage = null;
            if (totalDownloadSize.HasValue)
                progressPercentage = Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2);

            ProgressChanged(totalDownloadSize, totalBytesRead, progressPercentage);
        }
    }
}