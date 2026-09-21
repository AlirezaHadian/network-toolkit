using DnsChanger.Models;
using DnsChanger.Services;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace DnsChanger.Tests
{
    public class NetworkDiagnosticsServiceTests
    {
        private readonly Mock<IDnsService> _dnsMock = new();
        private readonly Mock<INetworkProbe> _probeMock = new();
        private readonly NetworkDiagnosticsService _service;

        // xUnit برای «هر تست» یه نمونه‌ی تازه از این کلاس می‌سازه،
        // پس این Constructor قبل از هر تست دوباره اجرا میشه و تست‌ها روی هم اثر نمی‌ذارن.
        // اینجا حالت «همه‌چی سالمه» رو تنظیم می‌کنیم؛ هر تست فقط چیزی که خراب می‌خواد رو عوض می‌کنه.
        public NetworkDiagnosticsServiceTests()
        {
            var fakeAdapter = new Mock<NetworkInterface>().Object;

            _dnsMock.Setup(s => s.GetActiveAdapter()).Returns(fakeAdapter);
            _probeMock.Setup(p => p.CanReachGatewayAsync(It.IsAny<NetworkInterface>())).ReturnsAsync(true);
            _probeMock.Setup(p => p.PingAsync(It.IsAny<string>())).ReturnsAsync(true);
            _probeMock.Setup(p => p.CanResolveAnyAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync(true);

            _service = new NetworkDiagnosticsService(_dnsMock.Object, _probeMock.Object);
        }

        [Fact]
        public async Task NoActiveAdapter_ReturnsSingleAdapterFailure()
        {
            _dnsMock.Setup(s => s.GetActiveAdapter()).Returns(null as NetworkInterface);

            var results = await _service.RunDiagnosticsAsync();

            Assert.Single(results);
            Assert.Equal(DiagnosticStepType.AdapterCheck, results[0].StepType);
            Assert.False(results[0].IsSuccess);
        }

        [Fact]
        public async Task NoActiveAdapter_DoesNotTouchDnsOrAdapter()
        {
            _dnsMock.Setup(s => s.GetActiveAdapter()).Returns(null as NetworkInterface);

            await _service.RunDiagnosticsAsync();

            _dnsMock.Verify(s => s.RestartActiveAdapter(), Times.Never);
            _dnsMock.Verify(s => s.SetDns(It.IsAny<DnsProvider>()), Times.Never);
        }

        [Fact]
        public async Task EverythingWorks_AllStepsSucceed_AndNothingIsChanged()
        {
            var results = await _service.RunDiagnosticsAsync();

            Assert.Equal(4, results.Count);
            Assert.All(results, r => Assert.True(r.IsSuccess));
            _dnsMock.Verify(s => s.RestartActiveAdapter(), Times.Never);
            _dnsMock.Verify(s => s.SetDns(It.IsAny<DnsProvider>()), Times.Never);
        }

        [Fact]
        public async Task RouterDown_RestartsAdapterOnce_ThenContinues()
        {
            // بار اول روتر جواب نمیده، بعد از ری‌استارت جواب میده
            _probeMock.SetupSequence(p => p.CanReachGatewayAsync(It.IsAny<NetworkInterface>()))
                      .ReturnsAsync(false)
                      .ReturnsAsync(true);

            var results = await _service.RunDiagnosticsAsync();

            _dnsMock.Verify(s => s.RestartActiveAdapter(), Times.Once);
            var restartStep = results.Single(r => r.StepType == DiagnosticStepType.AdapterRestartAttempt);
            Assert.True(restartStep.IsSuccess);
            Assert.True(results.Last().IsSuccess);
        }

        [Fact]
        public async Task NoInternet_StopsBeforeCheckingDns()
        {
            _probeMock.Setup(p => p.PingAsync(It.IsAny<string>())).ReturnsAsync(false);

            var results = await _service.RunDiagnosticsAsync();

            Assert.Equal(DiagnosticStepType.InternetCheck, results.Last().StepType);
            Assert.False(results.Last().IsSuccess);
            _probeMock.Verify(p => p.CanResolveAnyAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
            _dnsMock.Verify(s => s.SetDns(It.IsAny<DnsProvider>()), Times.Never);
        }

        [Fact]
        public async Task DnsDown_SwitchesToFirstWorkingProvider()
        {
            // بار اول DNS کار نمی‌کنه، بعد از اولین تعویض درست میشه
            _probeMock.SetupSequence(p => p.CanResolveAnyAsync(It.IsAny<IEnumerable<string>>()))
                      .ReturnsAsync(false)
                      .ReturnsAsync(true);

            var results = await _service.RunDiagnosticsAsync();

            _dnsMock.Verify(s => s.SetDns(It.Is<DnsProvider>(p => p.Name == "Shecan")), Times.Once);
            Assert.Equal(DiagnosticStepType.DnsFallbackSuccess, results.Last().StepType);
            Assert.Equal("Shecan", results.Last().ExtraData);
        }

        [Fact]
        public async Task DnsDown_AllProvidersFail_TriesEachOnce_AndReportsFailure()
        {
            _probeMock.Setup(p => p.CanResolveAnyAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync(false);

            var results = await _service.RunDiagnosticsAsync();

            _dnsMock.Verify(s => s.SetDns(It.IsAny<DnsProvider>()), Times.Exactly(3));
            Assert.Equal(DiagnosticStepType.DnsFallbackFailure, results.Last().StepType);
            Assert.False(results.Last().IsSuccess);
        }
    }
}
