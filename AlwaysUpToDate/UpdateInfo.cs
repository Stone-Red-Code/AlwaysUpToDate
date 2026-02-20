using System.Collections.Generic;
using System.Xml.Serialization;

namespace AlwaysUpToDate
{
    [XmlRoot("updates")]
    public class UpdateManifest
    {
        [XmlElement("item")]
        public List<UpdateItem> Items { get; set; } = new List<UpdateItem>();
    }

    public class UpdateItem
    {
        [XmlElement("os")]
        public TargetOS OS { get; set; }

        [XmlElement("version")]
        public string Version { get; set; }

        [XmlElement("url")]
        public string DownloadUrl { get; set; }

        [XmlElement("changelog")]
        public string ChangelogUrl { get; set; }

        [XmlElement("mandatory")]
        public bool IsMandatory { get; set; }

        [XmlElement("checksum")]
        public Checksum Checksum { get; set; }
    }

    public enum HashAlgorithmType
    {
        [XmlEnum("sha1")]
        SHA1,

        [XmlEnum("md5")]
        MD5,

        [XmlEnum("sha256")]
        SHA256,

        [XmlEnum("sha512")]
        SHA512,
    }

    public class Checksum
    {
        [XmlAttribute("algorithm")]
        public HashAlgorithmType Algorithm { get; set; }

        [XmlText]
        public string Value { get; set; }
    }

    public enum TargetOS
    {
        [XmlEnum("windows")]
        Windows,

        [XmlEnum("macos")]
        MacOS,

        [XmlEnum("linux")]
        Linux,
    }

    public enum UpdateStep
    {
        Downloading,
        VerifyingChecksum,
        Extracting,
        CleaningUp,
        Restarting,
    }
}