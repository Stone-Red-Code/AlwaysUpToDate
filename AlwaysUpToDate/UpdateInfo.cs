using System.Collections.Generic;
using System.Xml.Serialization;

namespace AlwaysUpToDate
{
    /// <summary>
    /// Represents the root element of an update manifest XML document.
    /// </summary>
    [XmlRoot("updates")]
    public class UpdateManifest
    {
        /// <summary>
        /// Gets or sets the list of available update items, one per target OS.
        /// </summary>
        [XmlElement("item")]
        public List<UpdateItem> Items { get; set; } = new List<UpdateItem>();
    }

    /// <summary>
    /// Represents a single update entry in the manifest, targeting a specific OS.
    /// </summary>
    public class UpdateItem
    {
        /// <summary>
        /// Gets or sets the target operating system for this update.
        /// </summary>
        [XmlElement("os")]
        public TargetOS OS { get; set; }

        /// <summary>
        /// Gets or sets the version string of the update in <c>X.X.X.X</c> format.
        /// </summary>
        [XmlElement("version")]
        public string Version { get; set; }

        /// <summary>
        /// Gets or sets the URL from which the update ZIP file can be downloaded.
        /// </summary>
        [XmlElement("url")]
        public string DownloadUrl { get; set; }

        /// <summary>
        /// Gets or sets an optional URL pointing to a changelog for this update.
        /// </summary>
        [XmlElement("changelog")]
        public string ChangelogUrl { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this update is mandatory.
        /// When <see langword="true"/>, the update is downloaded and installed immediately without raising <see cref="Updater.UpdateAvailable"/>.
        /// </summary>
        [XmlElement("mandatory")]
        public bool IsMandatory { get; set; }

        /// <summary>
        /// Gets or sets an optional checksum used to verify the integrity of the downloaded update.
        /// </summary>
        [XmlElement("checksum")]
        public Checksum Checksum { get; set; }
    }

    /// <summary>
    /// Specifies the hash algorithm used for checksum verification of downloaded updates.
    /// </summary>
    public enum HashAlgorithmType
    {
        /// <summary>SHA-1 (default).</summary>
        [XmlEnum("sha1")]
        SHA1,

        /// <summary>MD5.</summary>
        [XmlEnum("md5")]
        MD5,

        /// <summary>SHA-256.</summary>
        [XmlEnum("sha256")]
        SHA256,

        /// <summary>SHA-512.</summary>
        [XmlEnum("sha512")]
        SHA512,
    }

    /// <summary>
    /// Represents a checksum value and its associated hash algorithm for verifying download integrity.
    /// </summary>
    public class Checksum
    {
        /// <summary>
        /// Gets or sets the hash algorithm used to compute the checksum. Defaults to <see cref="HashAlgorithmType.SHA1"/>.
        /// </summary>
        [XmlAttribute("algorithm")]
        public HashAlgorithmType Algorithm { get; set; }

        /// <summary>
        /// Gets or sets the expected hex-encoded hash value of the downloaded file.
        /// </summary>
        [XmlText]
        public string Value { get; set; }
    }

    /// <summary>
    /// Specifies the target operating system for an update item.
    /// </summary>
    public enum TargetOS
    {
        /// <summary>Microsoft Windows.</summary>
        [XmlEnum("windows")]
        Windows,

        /// <summary>Apple macOS.</summary>
        [XmlEnum("macos")]
        MacOS,

        /// <summary>Linux.</summary>
        [XmlEnum("linux")]
        Linux,
    }

    /// <summary>
    /// Describes the current phase of the update process, reported via <see cref="Updater.ProgressChanged"/>.
    /// </summary>
    public enum UpdateStep
    {
        /// <summary>The update ZIP file is being downloaded. Progress is measured in bytes.</summary>
        Downloading,

        /// <summary>The downloaded file's checksum is being verified against the manifest.</summary>
        VerifyingChecksum,

        /// <summary>ZIP entries are being extracted to the install path. Progress is measured in entries.</summary>
        Extracting,

        /// <summary>Old files from the previous version are being deleted. Progress is measured in files.</summary>
        CleaningUp,

        /// <summary>The updated application is about to be launched.</summary>
        Restarting,
    }
}