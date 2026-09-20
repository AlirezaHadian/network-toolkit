using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DnsChanger.Models;

namespace DnsChanger.Services
{
    public class IpInfoService : IIpInfoService
    {
        public async Task<IpInfoResult> GetIpInfoAsync(string ip = null)
        {
            bool isOwnIp = string.IsNullOrWhiteSpace(ip);

            if (isOwnIp)
            {
                FlushDnsCache();
            }

            string url = isOwnIp
    ? $"https://ipwho.is/?_={DateTime.Now.Ticks}"
    : $"https://ipwho.is/{ip}?_={DateTime.Now.Ticks}";

            // برای هر درخواست یه HttpClient تازه می‌سازیم و بعد دورش می‌ندازیم — برخلاف روش معمول.
            // دلیلش اینه که هر اتصالِ بازمانده باعث میشه بعد از روشن/خاموش کردن VPN، همچنان IP قدیمی گزارش بشه.
            using var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.Zero,
                PooledConnectionIdleTimeout = TimeSpan.Zero,
                MaxConnectionsPerServer = 1,
                UseProxy = true,
                UseCookies = false
            };

            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

            http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true,
                MustRevalidate = true
            };
            http.DefaultRequestHeaders.ConnectionClose = true;

            var json = await http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // ISP داخل شیء connection قرار داره
            string isp = "-";
            if (root.TryGetProperty("connection", out var connection) &&
                connection.TryGetProperty("isp", out var ispValue) &&
                ispValue.ValueKind == JsonValueKind.String)
            {
                isp = ispValue.GetString();
            }

            // منطقه‌ی زمانی هم داخل شیء timezone، تو فیلد id
            string timezone = "-";
            if (root.TryGetProperty("timezone", out var tz) &&
                tz.TryGetProperty("id", out var tzId) &&
                tzId.ValueKind == JsonValueKind.String)
            {
                timezone = tzId.GetString();
            }

            return new IpInfoResult
            {
                IpAddress = GetString(root, "ip", ip ?? "-"),
                CountryName = GetString(root, "country", "-"),
                CityName = GetString(root, "city", "-"),
                Isp = isp,
                TimeZone = timezone,
                IsProxy = false
            };
        }

        private static string GetString(JsonElement root, string propertyName, string fallback)
        {
            if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
            return fallback;
        }

        private static void FlushDnsCache()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("ipconfig", "/flushdns")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var process = System.Diagnostics.Process.Start(psi);
                process?.WaitForExit(2000);
            }
            catch
            {
            }
        }
    }
}
