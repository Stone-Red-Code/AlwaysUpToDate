using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Serialization;

namespace AlwaysUpToDate
{
    public class Updater : IDisposable
    {
        public delegate void UpdaterChangedHandler(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage);

        public event UpdaterChangedHandler ProgressChanged;

        public delegate void UpdateAvailableHandler(string version, string changelogUrl);

        public event UpdateAvailableHandler UpdateAvailable;

        public delegate void NoUpdateAvailableHandler();

        public event NoUpdateAvailableHandler NoUpdateAvailable;

        public delegate void ExceptionHandler(Exception exception);

        public event ExceptionHandler OnException;

        private static readonly XmlSerializer manifestSerializer = new XmlSerializer(typeof(UpdateManifest));
        private readonly HttpClient httpClient = new HttpClient();
        private readonly Timer updateTimer = new Timer();
        private readonly string updateInfoUrl;
        private readonly string installPath;
        private string updateUrl;
        private bool updating;
        private bool disposed;

        public Updater(TimeSpan interval, Uri updateInfoUri, bool onlyUpdateOnce = false) : this(interval, updateInfoUri?.ToString(), "./", onlyUpdateOnce)
        {
        }
        public Updater(TimeSpan interval, Uri updateInfoUri, string installPath = "./", bool onlyUpdateOnce = false) : this(interval, updateInfoUri?.ToString(), installPath, onlyUpdateOnce)
        {
        }

        public Updater(TimeSpan interval, string updateInfoUrl, bool onlyUpdateOnce = false) : this(interval, updateInfoUrl, "./", onlyUpdateOnce)
        {
        }

        public Updater(TimeSpan interval, string updateInfoUrl, string installPath = "./", bool onlyUpdateOnce = false)
        {
            this.updateInfoUrl = updateInfoUrl ?? throw new ArgumentNullException(nameof(updateInfoUrl));
            this.installPath = installPath ?? throw new ArgumentNullException(nameof(installPath));

            if (interval.TotalMilliseconds > 0)
            {
                updateTimer.Interval = interval.TotalMilliseconds;
            }

            if (!onlyUpdateOnce)
            {
                updateTimer.Elapsed += UpdateTimer_Elapsed;
            }
        }

        public void Start()
        {
            ThrowIfDisposed();
            if (updateTimer.Interval > 0)
            {
                updateTimer.Start();
            }

            UpdateTimer_Elapsed(null, null);
        }

        public void Stop()
        {
            ThrowIfDisposed();
            updateTimer.Stop();
        }

        public async Task Update()
        {
            ThrowIfDisposed();
            if (!string.IsNullOrWhiteSpace(updateUrl) && !updating)
            {
                await DownloadFile();
            }
        }

        private async void UpdateTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                using HttpResponseMessage response = await httpClient.GetAsync(updateInfoUrl);
                _ = response.EnsureSuccessStatusCode();

                using Stream stream = await response.Content.ReadAsStreamAsync();
                UpdateManifest manifest = (UpdateManifest)manifestSerializer.Deserialize(stream);

                TargetOS currentOS = GetCurrentOS();
                UpdateItem updateItem = manifest.Items?.FirstOrDefault(i => i.OS == currentOS);

                if (updateItem == null)
                {
                    return;
                }

                Version assemblyVersion = Assembly.GetEntryAssembly().GetName().Version;

                if (!Version.TryParse(updateItem.Version, out Version version))
                {
                    return;
                }

                if (version > assemblyVersion && !updating)
                {
                    updateUrl = updateItem.DownloadUrl;
                    if (!updateItem.IsMandatory)
                    {
                        UpdateAvailable?.Invoke(updateItem.Version, updateItem.ChangelogUrl);
                    }
                    else
                    {
                        await Update();
                    }
                }
                else
                {
                    NoUpdateAvailable?.Invoke();
                }
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
            }
        }

        private static TargetOS GetCurrentOS()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return TargetOS.Windows;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return TargetOS.MacOS;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return TargetOS.Linux;
            }

            throw new PlatformNotSupportedException();
        }

        private async Task DownloadFile()
        {
            try
            {
                updateTimer.Stop();
                updating = true;

                using HttpResponseMessage response = await httpClient.GetAsync(updateUrl);
                _ = response.EnsureSuccessStatusCode();

                using Stream contentStream = await response.Content.ReadAsStreamAsync();
                await ProcessContentStream(response.Content.Headers.ContentLength, contentStream);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
            }
        }

        private void ExtractZipFile()
        {
            try
            {
                string executablePath = Process.GetCurrentProcess().MainModule?.FileName ?? Assembly.GetEntryAssembly()?.Location;

                if (string.IsNullOrEmpty(executablePath))
                {
                    throw new InvalidOperationException("Unable to determine the executable path.");
                }

                using (ZipArchive zipArchive = ZipFile.OpenRead(Path.Join(installPath, "Update.zip")))
                {
                    string fullInstallPath = Path.GetFullPath(installPath + Path.DirectorySeparatorChar);

                    foreach (ZipArchiveEntry entry in zipArchive.Entries)
                    {
                        string destinationPath = Path.GetFullPath(Path.Join(installPath, entry.FullName));
                        if (!destinationPath.StartsWith(fullInstallPath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (File.Exists(Path.Join(installPath, entry.FullName)))
                        {
                            string moveName = entry.FullName;
                            while (File.Exists(Path.Join(installPath, moveName)))
                            {
                                moveName += "_OLD_";
                            }
                            File.Move(Path.Join(installPath, entry.FullName), Path.Join(installPath, moveName));
                        }

                        if (entry.FullName.EndsWith('/'))
                        {
                            if (!Directory.Exists(Path.Join(installPath, entry.FullName)))
                            {
                                _ = Directory.CreateDirectory(Path.Join(installPath, entry.FullName));
                            }
                        }
                        else
                        {
                            entry.ExtractToFile(Path.Join(installPath, entry.FullName), true);
                        }
                    }
                }

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

                //if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                //{
                //    Process.Start("chmod", $"+x \"{executablePath}\"")?.WaitForExit();
                //}

                _ = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false });
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
            }
        }

        private async Task ProcessContentStream(long? totalDownloadSize, Stream contentStream)
        {
            try
            {
                long totalBytesRead = 0;
                int readCount = 0;
                byte[] buffer = new byte[8192];
                bool isMoreToRead = true;

                using FileStream fileStream = new FileStream(Path.Join(installPath, "Update.zip"), FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                do
                {
                    int bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length);
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

                ExtractZipFile();
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
            }
        }

        private void TriggerProgressChanged(long? totalDownloadSize, long totalBytesRead)
        {
            double? progressPercentage = null;
            if (totalDownloadSize.HasValue)
            {
                progressPercentage = Math.Round((double)totalBytesRead / totalDownloadSize.Value * 100, 2);
            }

            ProgressChanged?.Invoke(totalDownloadSize, totalBytesRead, progressPercentage);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(Updater));
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    updateTimer.Dispose();
                    httpClient.Dispose();
                }
                disposed = true;
            }
        }
    }
}
