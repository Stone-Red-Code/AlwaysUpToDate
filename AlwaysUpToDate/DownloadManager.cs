using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace AlwaysUpToDate
{
    public class Updater : IDisposable
    {
        public delegate void UpdaterChangedHandler(UpdateStep step, long? totalItems, long itemsProcessed, double? progressPercentage);

        public event UpdaterChangedHandler ProgressChanged;

        public delegate void UpdateAvailableHandler(string version, string changelogUrl);

        public event UpdateAvailableHandler UpdateAvailable;

        public delegate void NoUpdateAvailableHandler();

        public event NoUpdateAvailableHandler NoUpdateAvailable;

        public delegate void ExceptionHandler(Exception exception);

        public event ExceptionHandler OnException;

        private static readonly XmlSerializer manifestSerializer = new XmlSerializer(typeof(UpdateManifest));
        private readonly HttpClient httpClient = new HttpClient();
        private readonly System.Timers.Timer updateTimer = new System.Timers.Timer();
        private readonly string updateInfoUrl;
        private readonly string installPath;
        private string updateUrl;
        private UpdateItem pendingUpdateItem;
        private int updating;
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
            if (!string.IsNullOrWhiteSpace(updateUrl) && Interlocked.CompareExchange(ref updating, 1, 0) == 0)
            {
                await DownloadFile();
            }
        }

        private async void UpdateTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (Interlocked.CompareExchange(ref updating, 0, 0) != 0)
            {
                return;
            }

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

                Assembly entryAssembly = Assembly.GetEntryAssembly();
                if (entryAssembly == null)
                {
                    return;
                }

                Version assemblyVersion = entryAssembly.GetName().Version;

                if (!Version.TryParse(updateItem.Version, out Version version))
                {
                    return;
                }

                if (version > assemblyVersion && Interlocked.CompareExchange(ref updating, 0, 0) == 0)
                {
                    updateUrl = updateItem.DownloadUrl;
                    pendingUpdateItem = updateItem;
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
                _ = Interlocked.Exchange(ref updating, 1);

                using HttpResponseMessage response = await httpClient.GetAsync(updateUrl);
                _ = response.EnsureSuccessStatusCode();

                using Stream contentStream = await response.Content.ReadAsStreamAsync();
                await ProcessContentStream(response.Content.Headers.ContentLength, contentStream);
            }
            catch (Exception ex)
            {
                _ = Interlocked.Exchange(ref updating, 0);
                OnException?.Invoke(ex);
            }
        }

        private void ExtractZipFile()
        {
            try
            {
                string zipPath = Path.Join(installPath, "Update.zip");

                TriggerProgressChanged(UpdateStep.VerifyingChecksum, null, 0);
                if (!VerifyChecksum(zipPath))
                {
                    File.Delete(zipPath);
                    _ = Interlocked.Exchange(ref updating, 0);
                    throw new InvalidOperationException("Checksum verification failed for the downloaded update.");
                }
                TriggerProgressChanged(UpdateStep.VerifyingChecksum, 1, 1);

                string executablePath = Process.GetCurrentProcess().MainModule?.FileName ?? Assembly.GetEntryAssembly()?.Location;

                if (string.IsNullOrEmpty(executablePath))
                {
                    throw new InvalidOperationException("Unable to determine the executable path.");
                }

                using (ZipArchive zipArchive = ZipFile.OpenRead(zipPath))
                {
                    string fullInstallPath = Path.GetFullPath(installPath + Path.DirectorySeparatorChar);
                    long totalEntries = zipArchive.Entries.Count;
                    long processedEntries = 0;

                    foreach (ZipArchiveEntry entry in zipArchive.Entries)
                    {
                        string destinationPath = Path.GetFullPath(Path.Join(installPath, entry.FullName));
                        if (!destinationPath.StartsWith(fullInstallPath, StringComparison.OrdinalIgnoreCase))
                        {
                            processedEntries++;
                            continue;
                        }

                        if (File.Exists(Path.Join(installPath, entry.FullName)))
                        {
                            string moveName = Path.GetFileNameWithoutExtension(entry.FullName) + "_OLD_" + Path.GetExtension(entry.FullName);
                            int counter = 1;

                            while (File.Exists(Path.Join(installPath, moveName)))
                            {
                                moveName = Path.GetFileNameWithoutExtension(entry.FullName) + "_OLD_" + counter + Path.GetExtension(entry.FullName);
                                counter++;
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

                        processedEntries++;
                        TriggerProgressChanged(UpdateStep.Extracting, totalEntries, processedEntries);
                    }
                }

                string[] allFiles = Directory.GetFiles(installPath, "*.*", SearchOption.AllDirectories);
                string[] oldFiles = Array.FindAll(allFiles, f => f.Contains("_OLD_"));
                long totalOldFiles = oldFiles.Length;
                long deletedFiles = 0;

                foreach (string filePath in oldFiles)
                {
                    try
                    {
                        File.Delete(filePath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }

                    deletedFiles++;
                    TriggerProgressChanged(UpdateStep.CleaningUp, totalOldFiles, deletedFiles);
                }

                File.Delete(zipPath);

                TriggerProgressChanged(UpdateStep.Restarting, null, 0);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("chmod", $"+x \"{executablePath}\"")?.WaitForExit();
                }

                _ = Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                _ = Interlocked.Exchange(ref updating, 0);
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
                        TriggerProgressChanged(UpdateStep.Downloading, totalDownloadSize, totalBytesRead);
                        continue;
                    }

                    await fileStream.WriteAsync(buffer, 0, bytesRead);

                    totalBytesRead += bytesRead;
                    readCount += 1;

                    if (readCount >= 10)
                    {
                        readCount = 0;
                        TriggerProgressChanged(UpdateStep.Downloading, totalDownloadSize, totalBytesRead);
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

        private bool VerifyChecksum(string filePath)
        {
            UpdateItem item = pendingUpdateItem;
            if (item?.Checksum == null || string.IsNullOrWhiteSpace(item.Checksum.Value))
            {
                return true;
            }

            using HashAlgorithm algorithm = CreateHashAlgorithm(item.Checksum.Algorithm);
            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = algorithm.ComputeHash(stream);
            string hashString = BitConverter.ToString(hash).Replace("-", "");
            return string.Equals(hashString, item.Checksum.Value, StringComparison.OrdinalIgnoreCase);
        }

        private static HashAlgorithm CreateHashAlgorithm(HashAlgorithmType algorithm)
        {
            return algorithm switch
            {
                HashAlgorithmType.MD5 => MD5.Create(),
                HashAlgorithmType.SHA256 => SHA256.Create(),
                HashAlgorithmType.SHA512 => SHA512.Create(),
                HashAlgorithmType.SHA1 => SHA1.Create(),
                _ => throw new NotSupportedException($"Hash algorithm '{algorithm}' is not supported."),
            };
        }

        private void TriggerProgressChanged(UpdateStep step, long? totalSize, long totalProcessed)
        {
            double? progressPercentage = null;
            if (totalSize.HasValue && totalSize.Value > 0)
            {
                progressPercentage = Math.Round((double)totalProcessed / totalSize.Value * 100, 2);
            }

            ProgressChanged?.Invoke(step, totalSize, totalProcessed, progressPercentage);
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
