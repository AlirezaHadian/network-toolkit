using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DnsChanger.Models;
using ManagedNativeWifi;

namespace DnsChanger.Services
{
    public class WifiService : IWifiService
    {
        public Task<List<WifiNetworkInfo>> GetAvailableNetworksAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var connectedSsids = NativeWifi.EnumerateConnectedNetworkSsids()
                        .Select(s => s.ToString())
                        .ToHashSet();

                    var networks = NativeWifi.EnumerateAvailableNetworks()
                        .Where(n => !string.IsNullOrEmpty(n.Ssid.ToString()))
                        .GroupBy(n => n.Ssid.ToString())
                        .Select(g => g.OrderByDescending(n => n.SignalQuality).First());

                    return networks.Select(n => new WifiNetworkInfo
                    {
                        Name = n.Ssid.ToString(),
                        SignalPercent = (int)n.SignalQuality,
                        IsSecured = n.IsSecurityEnabled,
                        IsConnected = connectedSsids.Contains(n.Ssid.ToString())
                    })
                    .OrderByDescending(n => n.IsConnected)
                    .ThenByDescending(n => n.SignalPercent)
                    .ToList();
                }
                catch
                {
                    return new List<WifiNetworkInfo>();
                }
            });
        }

        public Task<bool> HasSavedProfileAsync(string ssid)
        {
            return Task.Run(() =>
            {
                try
                {
                    return NativeWifi.EnumerateProfileNames().Any(name => name == ssid);
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> ConnectToSavedProfileAsync(string ssid)
        {
            try
            {
                var interfaceId = await Task.Run(() => NativeWifi.EnumerateInterfaces().FirstOrDefault()?.Id);
                if (interfaceId == null) return false;

                return await NativeWifi.ConnectNetworkAsync(
                    interfaceId.Value, ssid, BssType.Infrastructure, TimeSpan.FromSeconds(10));
            }
            catch
            {
                // مثلاً وقتی از قبل به همین شبکه وصلی، یا پروفایل خراب/نصفه‌کاره‌ست —
                // به‌جای کرش، فقط بگو ناموفق بود تا کد بالادستی بره سراغ گرفتن رمز جدید
                return false;
            }
        }

        public async Task<bool> ConnectWithPasswordAsync(string ssid, string password, bool isSecured)
        {
            if (isSecured && (string.IsNullOrEmpty(password) || password.Length < 8 || password.Length > 63))
            {
                return false;
            }

            try
            {
                var interfaceId = await Task.Run(() => NativeWifi.EnumerateInterfaces().FirstOrDefault()?.Id);
                if (interfaceId == null) return false;

                string profileXml = isSecured ? BuildSecuredProfileXml(ssid, password) : BuildOpenProfileXml(ssid);

                bool profileSet = await Task.Run(() => NativeWifi.SetProfile(
                    interfaceId.Value, ProfileType.AllUser, profileXml, null, overwrite: true));

                if (!profileSet) return false;

                return await NativeWifi.ConnectNetworkAsync(
                    interfaceId.Value, ssid, BssType.Infrastructure, TimeSpan.FromSeconds(10));
            }
            catch
            {
                return false;
            }
        }

        private string BuildSecuredProfileXml(string ssid, string password) => $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{ssid}</name>
    <SSIDConfig><SSID><name>{ssid}</name></SSID></SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>manual</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{password}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";

        private string BuildOpenProfileXml(string ssid) => $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{ssid}</name>
    <SSIDConfig><SSID><name>{ssid}</name></SSID></SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>manual</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>open</authentication>
                <encryption>none</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
        </security>
    </MSM>
</WLANProfile>";

        public Task<List<WifiNetworkInfo>> GetAvailableNetworks()
        {
            throw new NotImplementedException();
        }
    }
}
