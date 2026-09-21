using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace DnsChanger.Services
{
    public interface INetworkProbe
    {
        Task<bool> CanReachGatewayAsync(NetworkInterface adapter);
        Task<bool> PingAsync(string host);
        Task<bool> CanResolveAnyAsync(IEnumerable<string> hosts);
    }
}
