using DnsChanger.Models;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace DnsChanger.Services
{
    public class NetworkDiagnosticsService : INetworkDiagnosticsService
    {
        private readonly IDnsService _dnsService;

        private static readonly string[] TestHosts = { "www.google.com", "www.cloudflare.com", "www.microsoft.com" };

        private static readonly DnsProvider[] FixCandidates =
        {
            new DnsProvider { Name = "Shecan", Primary = "178.22.122.100", Secondary = "185.51.200.2" },
            new DnsProvider { Name = "Cloudflare", Primary = "1.1.1.1", Secondary = "1.0.0.1" },
            new DnsProvider { Name = "Google", Primary = "8.8.8.8", Secondary = "8.8.4.4" },
        };

        public NetworkDiagnosticsService(IDnsService dnsService)
        {
            _dnsService = dnsService;
        }

        public async Task<List<DiagnosticStepResult>> RunDiagnosticsAsync()
        {
            var results = new List<DiagnosticStepResult>();

            // Step 1: Check if there is an active network adapter
            var adapter = _dnsService.GetActiveAdapter();
            if (adapter == null)
            {
                results.Add(new DiagnosticStepResult
                {
                    StepType = DiagnosticStepType.AdapterCheck,
                    Status = DiagnosticStatus.Failure,
                    IsSuccess = false
                });
                return results;
            }
            results.Add(new DiagnosticStepResult
            {
                StepType = DiagnosticStepType.AdapterCheck,
                Status = DiagnosticStatus.Success,
                IsSuccess = true
            });

            // Step 2: Gateway (Router) ping
            var gateway = adapter.GetIPProperties().GatewayAddresses.FirstOrDefault();
            bool gatewayOk = gateway != null && await PingHostAsync(gateway.Address.ToString());

            if (!gatewayOk)
            {
                results.Add(new DiagnosticStepResult
                {
                    StepType = DiagnosticStepType.GatewayCheck,
                    Status = DiagnosticStatus.Failure,
                    IsSuccess = false
                });

                _dnsService.RestartActiveAdapter();
                await Task.Delay(3000);

                adapter = _dnsService.GetActiveAdapter();
                gateway = adapter?.GetIPProperties().GatewayAddresses.FirstOrDefault();
                gatewayOk = gateway != null && await PingHostAsync(gateway.Address.ToString());

                results.Add(new DiagnosticStepResult
                {
                    StepType = DiagnosticStepType.AdapterRestartAttempt,
                    Status = gatewayOk ? DiagnosticStatus.Success : DiagnosticStatus.Failure,
                    IsSuccess = gatewayOk
                });

                if (!gatewayOk) return results;
            }
            else
            {
                results.Add(new DiagnosticStepResult
                {
                    StepType = DiagnosticStepType.GatewayCheck,
                    Status = DiagnosticStatus.Success,
                    IsSuccess = true
                });
            }

            // Step 3: Ping a well-known public IP (no DNS needed)
            bool internetOk = await PingHostAsync("8.8.8.8");
            results.Add(new DiagnosticStepResult
            {
                StepType = DiagnosticStepType.InternetCheck,
                Status = internetOk ? DiagnosticStatus.Success : DiagnosticStatus.Failure,
                IsSuccess = internetOk
            });
            if (!internetOk) return results;

            // Step 4: Check if DNS is working (resolve a domain)
            bool dnsOk = await CanResolveAnyAsync(TestHosts);
            if (dnsOk)
            {
                results.Add(new DiagnosticStepResult
                {
                    StepType = DiagnosticStepType.DnsCheck,
                    Status = DiagnosticStatus.Success,
                    IsSuccess = true
                });
            }
            else
            {
                results.Add(new DiagnosticStepResult
                {
                    StepType = DiagnosticStepType.DnsCheck,
                    Status = DiagnosticStatus.Failure,
                    IsSuccess = false
                });

                // رفع خودکار: چندتا DNS شناخته‌شده رو یکی‌یکی امتحان می‌کنیم تا یکی جواب بده
                foreach (var candidate in FixCandidates)
                {
                    _dnsService.SetDns(candidate);
                    await Task.Delay(1500);

                    bool fixedNow = await CanResolveAnyAsync(TestHosts);
                    if (fixedNow)
                    {
                        results.Add(new DiagnosticStepResult
                        {
                            StepType = DiagnosticStepType.DnsFallbackSuccess,
                            Status = DiagnosticStatus.Success,
                            IsSuccess = true,
                            ExtraData = candidate.Name
                        });
                        return results;
                    }
                }

                results.Add(new DiagnosticStepResult
                {
                    StepType = DiagnosticStepType.DnsFallbackFailure,
                    Status = DiagnosticStatus.Failure,
                    IsSuccess = false
                });
            }
            return results;
        }

        private async Task<bool> PingHostAsync(string host)
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

        private async Task<bool> CanResolveDnsAsync(string hostName)
        {
            try
            {
                await System.Net.Dns.GetHostEntryAsync(hostName);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private async Task<bool> CanResolveAnyAsync(IEnumerable<string> hosts)
        {
            foreach (var host in hosts)
            {
                if (await CanResolveDnsAsync(host))
                    return true;
            }
            return false;
        }
    }
}
