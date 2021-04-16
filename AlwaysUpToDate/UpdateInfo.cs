using System;
using System.Collections.Generic;
using System.Text;

namespace AlwaysUpToDate
{
    internal class UpdateInfo
    {
        public string Version { get; set; }
        public string FileUrl { get; set; }
        public string AdditionalInformationUrl { get; set; }
        public bool Mandatory { get; set; }
    }
}