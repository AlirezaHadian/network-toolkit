using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DnsChanger.Models
{
    public enum SpeedTestPhase
    {
        DataCenterLookup,
        DownloadTest,
        UploadTest,
        PingTest,
        Done
    }

    public class SpeedTestProgress
    {
        public SpeedTestPhase Phase { get; set; }
        public double CurrentMbps { get; set; }
        public double PercentComplete { get; set; }
        public long? PingMs { get; set; }
        public long? JitterMs { get; set; }
        public string DataCenter { get; set; }
        public double? FinalDownloadMbps { get; set; }
        public double? FinalUploadMbps { get; set; }
    }
}
