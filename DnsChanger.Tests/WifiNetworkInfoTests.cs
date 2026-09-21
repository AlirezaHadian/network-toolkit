using DnsChanger.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DnsChanger.Tests
{
    public class WifiNetworkInfoTests
    {
        //[Fact]
        //public void StrongSignal_AllBarsActive()
        //{
        //    // Arrange
        //    var network = new WifiNetworkInfo { SignalPercent = 90 };

        //    // Act & Assert
        //    Assert.True(network.Bar1Active);
        //    Assert.True(network.Bar2Active);
        //    Assert.True(network.Bar3Active);
        //}

        [Theory]
        [InlineData(5, false, false, false)]
        [InlineData(10, false, false, false)]
        [InlineData(11, true, false, false)]
        [InlineData(41, true, true, false)]
        [InlineData(71, true, true, true)]
        public void SignalBars_MatchSignalStrength(int percent, bool bar1, bool bar2, bool bar3)
        {
            var network = new WifiNetworkInfo { SignalPercent = percent };

            Assert.Equal(bar1, network.Bar1Active);
            Assert.Equal(bar2, network.Bar2Active);
            Assert.Equal(bar3, network.Bar3Active);
        }


    }
}
