using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DnsChanger.Services
{
    public class NetworkProbe : INetworkProbe
    {
        public async Task<bool> CanReachGatewayAsync(NetworkInterface adapter)
        {
            var gateway = adapter?.GetIPProperties().GatewayAddresses.FirstOrDefault();
            return gateway != null && await PingAsync(gateway.Address.ToString());
        }

        public async Task<bool> PingAsync(string host)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, 1500);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> CanResolveAnyAsync(IEnumerable<string> hosts)
        {
            foreach (var host in hosts)
            {
                try
                {
                    await System.Net.Dns.GetHostEntryAsync(host);
                    return true;
                }
                catch (SocketException)
                {
                }
            }
            return false;
        }
    }
}
