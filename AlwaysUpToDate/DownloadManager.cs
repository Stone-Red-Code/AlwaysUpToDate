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
    /// <summary>
    /// Provides automatic update checking, downloading, verification, extraction, and application restart.
    /// Periodically polls a remote XML manifest for new versions and manages the full update lifecycle.
    /// </summary>
    public class Updater : IDisposable
    {
        /// <summary>
        /// Represents the method that will handle update progress notifications.
        /// </summary>
        /// <param name="step">The current phase of the update process.</param>
        /// <param name="totalItems">The total number of items to process in this step, or <see langword="null"/> if unknown.</param>
        /// <param name="itemsProcessed">The number of items processed so far in this step.</param>
        /// <param name="progressPercentage">The progress percentage (0–100), or <see langword="null"/> if <paramref name="totalItems"/> is unknown.</param>
        public delegate void UpdaterChangedHandler(UpdateStep step, long itemsProcessed, long? totalItems, double? progressPercentage);

        /// <summary>
        /// Occurs when progress is made during any phase of the update process.
        /// </summary>
        public event UpdaterChangedHandler ProgressChanged;

        /// <summary>
        /// Represents the method that will handle notifications when a non-mandatory update is available.
        /// </summary>
        /// <param name="version">The version string of the available update.</param>
        /// <param name="changelogUrl">An optional URL pointing to the changelog, or <see langword="null"/> if not provided.</param>
        public delegate void UpdateAvailableHandler(string version, string changelogUrl);

        /// <summary>
        /// Occurs when a non-mandatory update is available. Call <see cref="UpdateAsync"/> to begin downloading.
        /// </summary>
        public event UpdateAvailableHandler UpdateAvailable;

        /// <summary>
        /// Represents the method that will handle notifications when no update is available.
        /// </summary>
        public delegate void NoUpdateAvailableHandler();

        /// <summary>
        /// Occurs when the remote manifest version is not newer than the current assembly version.
        /// </summary>
        public event NoUpdateAvailableHandler NoUpdateAvailable;

        /// <summary>
        /// Represents the method that will handle exceptions raised during the update process.
        /// </summary>
        /// <param name="exception">The exception that occurred.</param>
        public delegate void ExceptionHandler(Exception exception);

        /// <summary>
        /// Occurs when an exception is caught during update checking, downloading, extraction, or verification.
        /// </summary>
        public event ExceptionHandler OnException;

        /// <summary>
        /// Represents the method that will handle notifications when the update process begins.
        /// </summary>
        /// <param name="version">The version string of the update being installed.</param>
        public delegate void UpdateStartedHandler(string version);

        /// <summary>
        /// Occurs once when the update download begins, before the first <see cref="ProgressChanged"/> event.
        /// </summary>
        public event UpdateStartedHandler UpdateStarted;

        private static readonly XmlSerializer manifestSerializer = new XmlSerializer(typeof(UpdateManifest));
        private readonly HttpClient httpClient = new HttpClient();
        private readonly System.Timers.Timer updateTimer = new System.Timers.Timer();
        private readonly string updateInfoUrl;
        private readonly string installPath;
        private string updateUrl;
        private UpdateItem pendingUpdateItem;
        private int updating;
        private bool disposed;

        /// <inheritdoc cref="Updater(TimeSpan, string, string, bool)"/>
        public Updater(TimeSpan interval, Uri updateInfoUri, bool onlyUpdateOnce = false) : this(interval, updateInfoUri?.ToString(), "./", onlyUpdateOnce)
        {
        }

        /// <inheritdoc cref="Updater(TimeSpan, string, string, bool)"/>
        public Updater(TimeSpan interval, Uri updateInfoUri, string installPath = "./", bool onlyUpdateOnce = false) : this(interval, updateInfoUri?.ToString(), installPath, onlyUpdateOnce)
        {
        }

        /// <inheritdoc cref="Updater(TimeSpan, string, string, bool)"/>
        public Updater(TimeSpan interval, string updateInfoUrl, bool onlyUpdateOnce = false) : this(interval, updateInfoUrl, "./", onlyUpdateOnce)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Updater"/> class.
        /// </summary>
        /// <param name="interval">The interval between automatic update checks. Use <see cref="TimeSpan.Zero"/> to disable periodic checks.</param>
        /// <param name="updateInfoUrl">The URL of the remote XML update manifest.</param>
        /// <param name="installPath">The local directory where the update will be extracted. Defaults to the current directory.</param>
        /// <param name="onlyUpdateOnce">If <see langword="true"/>, performs a single update check on <see cref="Start"/> without subscribing to the periodic timer.</param>
        /// <exception cref="ArgumentNullException"><paramref name="updateInfoUrl"/> or <paramref name="installPath"/> is <see langword="null"/>.</exception>
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

        /// <summary>
        /// Starts the updater. Performs an immediate update check and, if a periodic interval was configured, begins recurring checks.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The updater has been disposed.</exception>
        public void Start()
        {
            ThrowIfDisposed();
            if (updateTimer.Interval > 0)
            {
                updateTimer.Start();
            }

            UpdateTimer_Elapsed(null, null);
        }

        /// <summary>
        /// Stops periodic update checking. Does not cancel an update that is already in progress.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The updater has been disposed.</exception>
        public void Stop()
        {
            ThrowIfDisposed();
            updateTimer.Stop();
        }

        /// <summary>
        /// Downloads and installs the available update. This method is typically called from the <see cref="UpdateAvailable"/> handler.
        /// If an update is already in progress or no update URL is available, the call is ignored.
        /// </summary>
        /// <returns>A task that represents the asynchronous update operation.</returns>
        /// <exception cref="ObjectDisposedException">The updater has been disposed.</exception>
        public async Task UpdateAsync()
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
                        await UpdateAsync();
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

                UpdateStarted?.Invoke(pendingUpdateItem?.Version);

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

            ProgressChanged?.Invoke(step, totalProcessed, totalSize, progressPercentage);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(Updater));
            }
        }

        /// <summary>
        /// Releases all resources used by the <see cref="Updater"/>.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="Updater"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing"><see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
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
