using DnsChanger.Repository;
using DnsChanger.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Configuration;
using System.Data;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace DnsChanger
{
    public partial class App : Application
    {
        private ServiceProvider _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DnsChanger.Data.DatabaseInitializer.Initialize();

            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("Lang.Persian.xaml", UriKind.Relative)
            });

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private void ConfigureServices(ServiceCollection services)
        {
            services.AddSingleton<IDnsService, DnsService>();
            services.AddSingleton<ICustomDnsRepository, CustomDnsRepository>();
            services.AddSingleton<INetworkDiagnosticsService, NetworkDiagnosticsService>();
            services.AddSingleton<IActivityLogRepository, ActivityLogRepository>();
            services.AddSingleton<IPingService, PingService>();
            services.AddSingleton<ISpeedTestService, SpeedTestService>();
            services.AddSingleton<IWifiService, WifiService>();
            services.AddSingleton<IIpInfoService, IpInfoService>();
            services.AddTransient<MainWindow>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }
    }
    public class AppSettings
    {
        public bool IsDarkMode { get; set; } = true;
        public string AccentName { get; set; } = "BluePurple";
        public bool IsEnglish { get; set; } = false;

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DnsChanger", "settings.json");

        public static AppSettings Load()
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            return new AppSettings();
        }

        public void Save()
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
        }
    }


}
