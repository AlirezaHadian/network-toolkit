using System.Collections.Generic;
using System.Threading.Tasks;
using DnsChanger.Models;

namespace DnsChanger.Services
{
    public interface IWifiService
    {
        Task<List<WifiNetworkInfo>> GetAvailableNetworksAsync();
        Task<bool> HasSavedProfileAsync(string ssid);
        Task<bool> ConnectToSavedProfileAsync(string ssid);
        Task<bool> ConnectWithPasswordAsync(string ssid, string password, bool isSecured);
    }
}
