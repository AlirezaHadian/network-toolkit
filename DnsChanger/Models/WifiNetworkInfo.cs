using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace DnsChanger.Models
{
    public class WifiNetworkInfo
    {
        public string Name { get; set; }
        public int SignalPercent { get; set; }
        public bool IsSecured { get; set; }
        public bool IsConnected { get; set; }
        public bool Bar1Active => SignalPercent > 10;
        public bool Bar2Active => SignalPercent > 40;
        public bool Bar3Active => SignalPercent > 70;

        public string SecurityInfo => IsSecured ? 
            (string)Application.Current.FindResource("Wifi_Secured") 
            : (string)Application.Current.FindResource("Wifi_Open");

    }
}
