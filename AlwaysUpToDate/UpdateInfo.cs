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
    /// Supports both combined OS-architecture values (e.g., <c>osx-arm64</c>) and
    /// separate <c>&lt;os&gt;</c> / <c>&lt;arch&gt;</c> elements.
    /// </summary>
    public class UpdateItem
    {
        /// <summary>
        /// Gets or sets the raw OS string from the manifest (e.g., <c>"windows"</c>, <c>"osx-arm64"</c>, <c>"linux"</c>).
        /// Supports combined OS-architecture values for backward compatibility.
        /// </summary>
        [XmlElement("os")]
        public string RawOS { get; set; }

        /// <summary>
        /// Gets or sets the raw architecture string from the manifest.
        /// When present, overrides any architecture embedded in <see cref="RawOS"/>.
        /// </summary>
        [XmlElement("arch")]
        public string RawArchitecture { get; set; }

        /// <summary>
        /// Gets the parsed target operating system, extracted from <see cref="RawOS"/>.
        /// Recognizes <c>windows</c>/<c>win</c>, <c>macos</c>/<c>osx</c>, and <c>linux</c> prefixes.
        /// </summary>
        [XmlIgnore]
        public TargetOS OS => ParseTargetOS(RawOS);

        /// <summary>
        /// Gets the parsed target architecture. Uses <see cref="RawArchitecture"/> if present;
        /// otherwise extracts the architecture suffix from <see cref="RawOS"/> (e.g., <c>osx-arm64</c> → <see cref="TargetArchitecture.Arm64"/>).
        /// Defaults to <see cref="TargetArchitecture.Any"/> when no architecture is specified.
        /// </summary>
        [XmlIgnore]
        public TargetArchitecture Architecture =>
            !string.IsNullOrEmpty(RawArchitecture)
                ? ParseArchitectureString(RawArchitecture)
                : ParseEmbeddedArchitecture(RawOS);

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

        private static TargetOS ParseTargetOS(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return default;
            }

            int dashIndex = raw.IndexOf('-');
            string osPart = dashIndex >= 0 ? raw[..dashIndex] : raw;

            switch (osPart.ToLowerInvariant())
            {
                case "windows":
                case "win":
                    return TargetOS.Windows;
                case "macos":
                case "osx":
                    return TargetOS.MacOS;
                case "linux":
                    return TargetOS.Linux;
                default:
                    throw new System.NotSupportedException($"Unknown operating system: '{osPart}'");
            }
        }

        private static TargetArchitecture ParseEmbeddedArchitecture(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return TargetArchitecture.Any;
            }

            int dashIndex = raw.IndexOf('-');
            if (dashIndex < 0)
            {
                return TargetArchitecture.Any;
            }

            return ParseArchitectureString(raw[(dashIndex + 1)..]);
        }

        private static TargetArchitecture ParseArchitectureString(string raw)
        {
            return (raw?.ToLowerInvariant()) switch
            {
                "x86" => TargetArchitecture.X86,
                "x64" => TargetArchitecture.X64,
                "arm" => TargetArchitecture.Arm,
                "arm64" => TargetArchitecture.Arm64,
                "any" => TargetArchitecture.Any,
                _ => throw new System.NotSupportedException($"Unknown architecture: '{raw}'"),
            };
        }
    }

    /// <summary>
    /// Specifies the hash algorithm used for checksum verification of downloaded updates.
    /// </summary>
    public enum HashAlgorithmType
    {
        /// <summary>SHA-1 (default).</summary>
        [XmlEnum("SHA1")]
        SHA1,

        /// <summary>MD5.</summary>
        [XmlEnum("MD5")]
        MD5,

        /// <summary>SHA-256.</summary>
        [XmlEnum("SHA256")]
        SHA256,

        /// <summary>SHA-512.</summary>
        [XmlEnum("SHA512")]
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
    /// Specifies the target processor architecture for an update item.
    /// <see cref="Any"/> means the update applies to all architectures.
    /// </summary>
    public enum TargetArchitecture
    {
        /// <summary>Any architecture (default). Used when the update is architecture-independent.</summary>
        [XmlEnum("any")]
        Any,

        /// <summary>Intel/AMD 32-bit (x86).</summary>
        [XmlEnum("x86")]
        X86,

        /// <summary>Intel/AMD 64-bit (x64).</summary>
        [XmlEnum("x64")]
        X64,

        /// <summary>ARM 32-bit.</summary>
        [XmlEnum("arm")]
        Arm,

        /// <summary>ARM 64-bit.</summary>
        [XmlEnum("arm64")]
        Arm64,
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