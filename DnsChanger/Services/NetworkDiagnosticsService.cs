using DnsChanger.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DnsChanger.Services
{
    public class NetworkDiagnosticsService : INetworkDiagnosticsService
    {
        private readonly IDnsService _dnsService;
        private readonly INetworkProbe _probe;

        private static readonly string[] TestHosts = { "www.google.com", "www.cloudflare.com", "www.microsoft.com" };

        private static readonly DnsProvider[] FixCandidates =
        {
            new DnsProvider { Name = "Shecan", Primary = "178.22.122.100", Secondary = "185.51.200.2" },
            new DnsProvider { Name = "Cloudflare", Primary = "1.1.1.1", Secondary = "1.0.0.1" },
            new DnsProvider { Name = "Google", Primary = "8.8.8.8", Secondary = "8.8.4.4" },
        };

        public NetworkDiagnosticsService(IDnsService dnsService, INetworkProbe probe)
        {
            _dnsService = dnsService;
            _probe = probe;
        }

        public async Task<List<DiagnosticStepResult>> RunDiagnosticsAsync()
        {
            var results = new List<DiagnosticStepResult>();

            // Step 1: Active adapter
            var adapter = _dnsService.GetActiveAdapter();
            if (adapter == null)
            {
                results.Add(new DiagnosticStepResult { StepType = DiagnosticStepType.AdapterCheck, Status = DiagnosticStatus.Failure, IsSuccess = false });
                return results;
            }
            results.Add(new DiagnosticStepResult { StepType = DiagnosticStepType.AdapterCheck, Status = DiagnosticStatus.Success, IsSuccess = true });

            // Step 2: Gateway (router)
            bool gatewayOk = await _probe.CanReachGatewayAsync(adapter);
            if (!gatewayOk)
            {
                results.Add(new DiagnosticStepResult { StepType = DiagnosticStepType.GatewayCheck, Status = DiagnosticStatus.Failure, IsSuccess = false });

                _dnsService.RestartActiveAdapter();
                await Task.Delay(3000);

                adapter = _dnsService.GetActiveAdapter();
                gatewayOk = adapter != null && await _probe.CanReachGatewayAsync(adapter);

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
                results.Add(new DiagnosticStepResult { StepType = DiagnosticStepType.GatewayCheck, Status = DiagnosticStatus.Success, IsSuccess = true });
            }

            // Step 3: Internet (by IP, no DNS needed)
            bool internetOk = await _probe.PingAsync("8.8.8.8");
            results.Add(new DiagnosticStepResult
            {
                StepType = DiagnosticStepType.InternetCheck,
                Status = internetOk ? DiagnosticStatus.Success : DiagnosticStatus.Failure,
                IsSuccess = internetOk
            });
            if (!internetOk) return results;

            // Step 4: DNS resolution
            bool dnsOk = await _probe.CanResolveAnyAsync(TestHosts);
            if (dnsOk)
            {
                results.Add(new DiagnosticStepResult { StepType = DiagnosticStepType.DnsCheck, Status = DiagnosticStatus.Success, IsSuccess = true });
                return results;
            }

            results.Add(new DiagnosticStepResult { StepType = DiagnosticStepType.DnsCheck, Status = DiagnosticStatus.Failure, IsSuccess = false });

            // Auto-fix: try known DNS providers one by one
            foreach (var candidate in FixCandidates)
            {
                _dnsService.SetDns(candidate);
                await Task.Delay(1500);

                if (await _probe.CanResolveAnyAsync(TestHosts))
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

            results.Add(new DiagnosticStepResult { StepType = DiagnosticStepType.DnsFallbackFailure, Status = DiagnosticStatus.Failure, IsSuccess = false });
            return results;
        }
    }
}
