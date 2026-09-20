using DnsChanger.Models;
using DnsChanger.Services;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.Net;
using DnsChanger.Repository;
using System.Net.NetworkInformation;
using System.Windows.Media.Animation;
using Application = System.Windows.Application;

namespace DnsChanger
{
    public partial class MainWindow : Window
    {
        private readonly IDnsService _dnsService;
        private readonly ICustomDnsRepository _customDnsRepository;
        private readonly INetworkDiagnosticsService _diagnosticsService;
        private readonly IActivityLogRepository _activityLog;
        private readonly IPingService _pingService;
        private readonly ISpeedTestService _speedTestService;
        private readonly IWifiService _wifiService;
        private readonly IIpInfoService _ipInfoService;

        private readonly ObservableCollection<CustomDnsEntry> _customDnsEntries = new();
        private readonly List<double> _speedChartValues = new();
        private DispatcherTimer _messageTimer;
        private Storyboard _spinnerStoryboard;
        private System.Windows.Forms.NotifyIcon _trayIcon;
        private Window _trayMenuHost;
        private bool _isExiting = false;

        public MainWindow(IDnsService dnsService, ICustomDnsRepository customDnsRepository, INetworkDiagnosticsService diagnosticsService,
            IActivityLogRepository activityLog, IPingService pingService, ISpeedTestService speedTestService, IWifiService wifiService, IIpInfoService ipInfoService)
        {
            InitializeComponent();
            _dnsService = dnsService;
            _customDnsRepository = customDnsRepository;
            _diagnosticsService = diagnosticsService;
            _activityLog = activityLog;
            _pingService = pingService;
            _speedTestService = speedTestService;
            _wifiService = wifiService;
            _ipInfoService = ipInfoService;

            CustomDnsItemsControl.ItemsSource = _customDnsEntries;
            LoadCustomDnsEntries();
            ApplySavedSettings();
            RefreshConnectionStatus();

            NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;

            _messageTimer = new DispatcherTimer();
            _messageTimer.Interval = TimeSpan.FromSeconds(5);
            _messageTimer.Tick += MessageTimer_Tick;

            InitializeTrayIcon();
        }
        public static void AdminPermissionCheck()
        {
            bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);

            if (!isAdmin)
            {
                CustomDialog.ShowWarning("برای اعمال تغییرات شبکه، برنامه رو با دسترسی Administrator اجرا کن.");
                return;
            }
        }
        private void ApplySavedSettings()
        {
            var settings = AppSettings.Load();

            if (settings.IsDarkMode)
                DarkModeButton_Click(this, null);
            else
                LightModeButton_Click(this, null);

            if (settings.IsEnglish)
            {
                Language_Click(LangEnButton, null);
            }

            if (FindName(settings.AccentName) is Button savedSwatch)
            {
                AccentSwatch_Click(savedSwatch, null);
            }
        }
        protected override void OnClosed(EventArgs e)
        {
            NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;
            _trayIcon?.Dispose();
            _trayMenuHost?.Close();
            base.OnClosed(e);
        }
        #region DNS 
        /// <summary>
        /// Function
        /// </summary>
        private void LoadCustomDnsEntries()
        {
            _customDnsEntries.Clear();
            foreach (var entry in _customDnsRepository.GetAllDns())
            {
                _customDnsEntries.Add(entry);
            }
        }
        private void ApplyProviderAndNotify(DnsProvider provider)
        {
            _dnsService.SetDns(provider);
            _activityLog.Add(string.Format((string)FindResource("Log_DnsSet"), provider.Name),
    $"{provider.Primary}, {provider.Secondary}");
            LoadHistory();
            RefreshConnectionStatus();
            ShowMessage($"{provider.Name} {(string)FindResource("Dns_SetMessage")}", isSuccess: true);
        }
        private void ShowMessage(string text, bool isSuccess)
        {
            MessageTextBlock.Text = text;
            MessageBorder.Background = new SolidColorBrush(isSuccess
                ? Color.FromRgb(0x22, 0xC5, 0x5E)
                : Color.FromRgb(0xEF, 0x44, 0x44));
            MessageBorder.Visibility = Visibility.Visible;

            _messageTimer.Stop();
            _messageTimer.Start();
        }
        private async void RefreshConnectionStatus()
        {
            var adapter = _dnsService.GetActiveAdapter();

            if (adapter == null)
            {
                ActiveAdapterText.Text = "—";
                CurrentDnsText.Text = "—";
                PublicIpText.Text = "—";
                ConnectionStatusText.Text = (string)FindResource("Dns_Disconnected");
                ConnectionStatusText.Foreground = (Brush)FindResource("Danger");
                ConnectionStatusDot.Fill = (Brush)FindResource("Danger");
                return;
            }

            ActiveAdapterText.Text = adapter.Name;

            var dnsAddresses = adapter.GetIPProperties().DnsAddresses;
            CurrentDnsText.Text = dnsAddresses.Count > 0
                ? string.Join(", ", dnsAddresses)
                : (string)FindResource("Dns_AutoDhcp");

            ConnectionStatusText.Text = (string)FindResource("Dns_Connected");
            ConnectionStatusText.Foreground = (Brush)FindResource("Success");
            ConnectionStatusDot.Fill = (Brush)FindResource("Success");

            PublicIpText.Text = (string)FindResource("Dns_Fetching");
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                PublicIpText.Text = await client.GetStringAsync("https://api.ipify.org");
            }
            catch
            {
                PublicIpText.Text = (string)FindResource("Dns_Unknown");
            }
        }
        /// <summary>
        /// Events
        /// </summary>
        private void MessageTimer_Tick(object sender, EventArgs e)
        {
            MessageBorder.Visibility = Visibility.Hidden;
            _messageTimer.Stop();
        }
        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            AdminPermissionCheck();
            _dnsService.UnsetDns();
            _activityLog.Add((string)FindResource("Log_DnsReset"));
            LoadHistory();
            RefreshConnectionStatus();
            ShowMessage((string)FindResource("Dns_ResetMessage"), isSuccess: false);
        }
        private void AddCustomDnsButton_Click(object sender, RoutedEventArgs e)
        {
            string name = CustomNameBox.Text.Trim();
            string primary = CustomPrimaryBox.Text.Trim();
            string secondary = CustomSecondaryBox.Text.Trim();

            if (string.IsNullOrEmpty(name) || !IPAddress.TryParse(primary, out _))
            {
                CustomDialog.ShowError((string)FindResource("Dns_InvalidNameOrIp"));
                return;
            }
            if (!string.IsNullOrWhiteSpace(secondary) && !IPAddress.TryParse(secondary, out _))
            {
                CustomDialog.ShowError((string)FindResource("Dns_InvalidSecondaryIp"));
                return;
            }

            var entry = new CustomDnsEntry
            {
                Name = name,
                Primary = primary,
                Secondary = string.IsNullOrWhiteSpace(secondary) ? null : secondary,
                CreatedAt = DateTime.Now
            };

            _customDnsRepository.Add(entry);
            _activityLog.Add(string.Format((string)FindResource("Log_DnsAdded"), entry.Name),
    $"{entry.Primary}, {entry.Secondary}");
            LoadHistory();
            LoadCustomDnsEntries();

            CustomNameBox.Clear();
            CustomPrimaryBox.Clear();
            CustomSecondaryBox.Clear();
        }
        private void ApplyCustomDns_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is CustomDnsEntry entry)
            {
                AdminPermissionCheck();
                ApplyProviderAndNotify(new DnsProvider { Name = entry.Name, Primary = entry.Primary, Secondary = entry.Secondary });
            }
        }
        private void DeleteCustomDns_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is CustomDnsEntry entry)
            {
                _customDnsRepository.Delete(entry.Id);
                LoadCustomDnsEntries();
                _activityLog.Add(string.Format((string)FindResource("Log_DnsDeleted"), entry.Name));
                LoadHistory();
            }
        }
        private async void CheckPingButton_Click(object sender, RoutedEventArgs e)
        {
            CheckPingButton.IsEnabled = false;

            var tasks = _customDnsEntries.Select(async entry =>
            {
                entry.PingText = "...";
                var ms = await _pingService.PingAsync(entry.Primary);
                entry.PingText = ms.HasValue ? $"{ms} ms" : "timeout";
            });
            await Task.WhenAll(tasks);

            CheckPingButton.IsEnabled = true;
        }
        private void NetworkChange_NetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            Dispatcher.Invoke(RefreshConnectionStatus);
        }
        private void NetworkChange_NetworkAddressChanged(object sender, EventArgs e)
        {
            Dispatcher.Invoke(RefreshConnectionStatus);
        }
        #endregion
        #region SpeedTest
        private async void StartSpeedTestButton_Click(object sender, RoutedEventArgs e)
        {
            StartSpeedTestButton.IsEnabled = false;
            DownloadSpeedText.Text = "—";
            UploadSpeedText.Text = "—";
            DownloadSpeedMBText.Text = "— MB/s";
            UploadSpeedMBText.Text = "— MB/s";
            PingResultText.Text = "—";
            JitterResultText.Text = "—";
            DataCenterText.Text = "—";
            ClearSpeedChart();
            StartIndeterminateSpinner();

            bool spinnerStopped = false;
            var progress = new Progress<SpeedTestProgress>(p =>
            {
                SpeedTestStatusText.Text = p.Phase switch
                {
                    SpeedTestPhase.DataCenterLookup => (string)FindResource("SpeedTest_PhaseDataCenter"),
                    SpeedTestPhase.DownloadTest => (string)FindResource("SpeedTest_PhaseDownload"),
                    SpeedTestPhase.UploadTest => (string)FindResource("SpeedTest_PhaseUpload"),
                    SpeedTestPhase.PingTest => (string)FindResource("SpeedTest_PhasePing"),
                    SpeedTestPhase.Done => (string)FindResource("SpeedTest_PhaseDone"),
                    _ => ""
                };

                if (p.CurrentMbps > 0)
                {
                    if (!spinnerStopped)
                    {
                        StopIndeterminateSpinner();
                        spinnerStopped = true;
                    }
                    SpeedTestLiveNumber.Text = p.CurrentMbps.ToString("0.0");
                    UpdateProgressRing(p.PercentComplete);
                    AddSpeedChartPoint(p.CurrentMbps);
                }
                if (!string.IsNullOrEmpty(p.DataCenter)) DataCenterText.Text = p.DataCenter;

                if (p.FinalDownloadMbps.HasValue)
                {
                    DownloadSpeedText.Text = p.FinalDownloadMbps.Value.ToString("0.0");
                    DownloadSpeedMBText.Text = $"{(p.FinalDownloadMbps.Value / 8):0.0} MB/s";
                }
                if (p.FinalUploadMbps.HasValue)
                {
                    UploadSpeedText.Text = p.FinalUploadMbps.Value.ToString("0.0");
                    UploadSpeedMBText.Text = $"{(p.FinalUploadMbps.Value / 8):0.0} MB/s";
                }

                // این دوتا زودتر از بقیه آماده میشن، همون لحظه نشونشون بده
                if (p.PingMs.HasValue) PingResultText.Text = p.PingMs.Value.ToString();
                if (!string.IsNullOrEmpty(p.DataCenter)) DataCenterText.Text = p.DataCenter;
            });

            var result = await _speedTestService.RunTestAsync(progress);

            DownloadSpeedText.Text = result.DownloadMbps.ToString("0.0");
            UploadSpeedText.Text = result.UploadMbps.ToString("0.0");
            DownloadSpeedMBText.Text = $"{(result.DownloadMbps / 8):0.0} MB/s";
            UploadSpeedMBText.Text = $"{(result.UploadMbps / 8):0.0} MB/s";
            PingResultText.Text = result.PingMs.ToString();
            JitterResultText.Text = result.JitterMs.ToString();
            DataCenterText.Text = result.DataCenter;

            SpeedTestLiveNumber.Text = "0.0";
            SpeedTestStatusText.Text = (string)FindResource("SpeedTest_Ready");
            UpdateProgressRing(0);

            _activityLog.Add((string)FindResource("Log_SpeedTestRun"),
                string.Format((string)FindResource("Log_SpeedTestDetails"),
                    result.DownloadMbps, result.UploadMbps, result.PingMs, result.JitterMs));

            LoadHistory();

            StartSpeedTestButton.IsEnabled = true;
        }
        private void UpdateProgressRing(double percent)
        {
            const double radius = 95; // (200 - 10) / 2 — چون Width=200 و StrokeThickness=10
            const double thickness = 10;
            double circumferenceInUnits = (2 * Math.PI * radius) / thickness;
            double dash = Math.Max(0.001, percent / 100.0 * circumferenceInUnits);
            double gap = Math.Max(0.001, circumferenceInUnits - dash);
            SpeedTestProgressRing.StrokeDashArray = new System.Windows.Media.DoubleCollection { dash, gap };
        }
        private void AddSpeedChartPoint(double mbps)
        {
            _speedChartValues.Add(mbps);
            if (_speedChartValues.Count > 50) _speedChartValues.RemoveAt(0);

            double width = SpeedChartCanvas.ActualWidth;
            double height = SpeedChartCanvas.ActualHeight;
            if (width <= 0 || height <= 0 || _speedChartValues.Count < 2) return;

            double maxValue = Math.Max(1, _speedChartValues.Max());
            double stepX = width / (_speedChartValues.Count - 1);

            var points = new System.Windows.Media.PointCollection();
            for (int i = 0; i < _speedChartValues.Count; i++)
            {
                double x = i * stepX;
                double y = height - (_speedChartValues[i] / maxValue * height * 0.9);
                points.Add(new System.Windows.Point(x, y));
            }
            SpeedChartLine.Points = points;
        }
        private void ClearSpeedChart()
        {
            _speedChartValues.Clear();
            SpeedChartLine.Points = new System.Windows.Media.PointCollection();
        }
        private void StartIndeterminateSpinner()
        {
            SpeedTestProgressRing.StrokeDashArray = new System.Windows.Media.DoubleCollection { 2.2, 7 };

            var animation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.2))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            _spinnerStoryboard = new Storyboard();
            Storyboard.SetTarget(animation, SpeedTestProgressRing);
            Storyboard.SetTargetProperty(animation, new PropertyPath("(Ellipse.RenderTransform).(RotateTransform.Angle)"));
            _spinnerStoryboard.Children.Add(animation);
            _spinnerStoryboard.Begin();
        }
        private void StopIndeterminateSpinner()
        {
            _spinnerStoryboard?.Stop();
            SpeedTestProgressRing.RenderTransform = new RotateTransform(-90);
        }
        #endregion
        #region Troubleshoot
        private async void RunDiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            RunDiagnosticsButton.IsEnabled = false;
            RunDiagnosticsButton.Content = FindResource("Troubleshoot_Checking");
            DiagnosticsResultsItemsControl.ItemsSource = null;

            var results = await _diagnosticsService.RunDiagnosticsAsync();

            foreach (var step in results)
            {
                step.Title = GetDiagnosticTitle(step.StepType);
                step.Message = GetDiagnosticMessage(step.StepType, step.IsSuccess, step.ExtraData);
            }

            DiagnosticsResultsItemsControl.ItemsSource = null;
            DiagnosticsResultsItemsControl.ItemsSource = results;

            bool overallOk = results.Count > 0 && results[^1].IsSuccess;
            _activityLog.Add((string)FindResource("Troubleshoot_DiagnoseTitle"),
    overallOk ? (string)FindResource("SpeedTest_PhaseDone") : "");
            LoadHistory();

            RunDiagnosticsButton.IsEnabled = true;
            RunDiagnosticsButton.Content = FindResource("Troubleshoot_DiagnoseButton");
        }
        private async void FlushDnsButton_Click(object sender, RoutedEventArgs e)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process.WaitForExit();
            _activityLog.Add((string)FindResource("Log_FlushDns"));
            LoadHistory();
            ShowMessage((string)FindResource("Troubleshoot_FlushSuccess"), isSuccess: true);
        }
        private void RestartAdapterButton_Click(object sender, RoutedEventArgs e)
        {
            AdminPermissionCheck();
            _dnsService.RestartActiveAdapter();
            _activityLog.Add((string)FindResource("Log_RestartAdapter"));
            LoadHistory();
            ShowMessage((string)FindResource("Troubleshoot_RestartSuccess"), isSuccess: true);
        }
        private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (CustomDialog.Confirm((string)FindResource("History_ClearConfirm")))
            {
                _activityLog.DeleteAll();
                LoadHistory();
            }
        }
        private string GetDiagnosticTitle(DiagnosticStepType type) => type switch
        {
            DiagnosticStepType.AdapterCheck => (string)FindResource("Diag_AdapterTitle"),
            DiagnosticStepType.GatewayCheck => (string)FindResource("Diag_GatewayTitle"),
            DiagnosticStepType.AdapterRestartAttempt => (string)FindResource("Diag_RestartTitle"),
            DiagnosticStepType.InternetCheck => (string)FindResource("Diag_InternetTitle"),
            DiagnosticStepType.DnsCheck => (string)FindResource("Diag_DnsTitle"),
            DiagnosticStepType.DnsFallbackSuccess => (string)FindResource("Diag_FallbackTitle"),
            DiagnosticStepType.DnsFallbackFailure => (string)FindResource("Diag_FallbackTitle"),
            _ => ""
        };

        private string GetDiagnosticMessage(DiagnosticStepType type, bool isSuccess, string extraData) => type switch
        {
            DiagnosticStepType.AdapterCheck => (string)FindResource(isSuccess ? "Diag_AdapterOk" : "Diag_AdapterFail"),
            DiagnosticStepType.GatewayCheck => (string)FindResource(isSuccess ? "Diag_GatewayOk" : "Diag_GatewayFail"),
            DiagnosticStepType.AdapterRestartAttempt => (string)FindResource(isSuccess ? "Diag_RestartOk" : "Diag_RestartFail"),
            DiagnosticStepType.InternetCheck => (string)FindResource(isSuccess ? "Diag_InternetOk" : "Diag_InternetFail"),
            DiagnosticStepType.DnsCheck => (string)FindResource(isSuccess ? "Diag_DnsOk" : "Diag_DnsFail"),
            DiagnosticStepType.DnsFallbackSuccess => string.Format((string)FindResource("Diag_FallbackOk"), extraData),
            DiagnosticStepType.DnsFallbackFailure => (string)FindResource("Diag_FallbackFail"),
            _ => ""
        };
        #endregion
        #region Wifi
        private async Task LoadWifiNetworksAsync()
        {
            WifiLoadingText.Visibility = Visibility.Visible;
            WifiNetworksItemsControl.ItemsSource = null;

            var networks = await _wifiService.GetAvailableNetworksAsync();

            WifiNetworksItemsControl.ItemsSource = networks;
            WifiLoadingText.Visibility = Visibility.Collapsed;
        }
        private async void RefreshWifiButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadWifiNetworksAsync();
        }
        private async void ConnectWifi_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not WifiNetworkInfo network) return;

            button.IsEnabled = false;
            var originalContent = button.Content;
            button.Content = Application.Current.FindResource("Wifi_Connecting");

            bool success = false;

            if (await _wifiService.HasSavedProfileAsync(network.Name)) success = await _wifiService.ConnectToSavedProfileAsync(network.Name);

            if (!success)
            {
                string password = null;
                if (network.IsSecured)
                {
                    password = CustomDialog.PromptPassword(network.Name);
                    if (password == null)
                    {
                        button.IsEnabled = true;
                        button.Content = originalContent;
                        return;
                    }
                }

                success = await _wifiService.ConnectWithPasswordAsync(network.Name, password, network.IsSecured);
            }

            button.IsEnabled = true;
            button.Content = originalContent;

            if (success)
            {
                CustomDialog.ShowInfo(string.Format((string)FindResource("Wifi_ConnectSuccess"), network.Name));
                _activityLog.Add(string.Format((string)FindResource("Log_WifiConnected"), network.Name));
                LoadHistory();
                await LoadWifiNetworksAsync();
                RefreshConnectionStatus();
            }
            else
            {
                CustomDialog.ShowError(string.Format((string)FindResource("Wifi_ConnectFailed"), network.Name));
            }
        }
        #endregion
        #region Ip Info
        private async Task LoadMyIpInfoAsync()
        {
            try
            {
                var info = await _ipInfoService.GetIpInfoAsync();

                MyIpAddressText.Text = info.IpAddress;
                MyIpLocationText.Text = info.CountryName;
                MyIpIspText.Text = info.Isp;
                MyIpTimezoneText.Text = info.TimeZone;
            }
            catch
            {
                MyIpAddressText.Text = "-";
                MyIpLocationText.Text = "-";
                MyIpIspText.Text = "-";
                MyIpTimezoneText.Text = "-";
            }
        }
        private async void CheckCustomIpButton_Click(object sender, RoutedEventArgs e)
        {
            string ip = CustomIpBox.Text.Trim();
            if (!System.Net.IPAddress.TryParse(ip, out _))
            {
                CustomDialog.ShowError((string)FindResource("IpInfo_InvalidIp"));
                return;
            }

            try
            {
                var info = await _ipInfoService.GetIpInfoAsync(ip);
                CustomIpAddressText.Text = info.IpAddress;
                CustomIpLocationText.Text = $"{info.CountryName} / {info.CityName}";
                CustomIpIspText.Text = info.Isp;
                CustomIpTimezoneText.Text = info.TimeZone;
                CustomIpResultsPanel.Visibility = Visibility.Visible;

                CustomIpProxyBadge.Visibility = info.IsProxy ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                CustomDialog.ShowError((string)FindResource("IpInfo_LookupFailed"));
            }
        }
        #endregion
        #region History
        private void LoadHistory()
        {
            HistoryListBox.ItemsSource = _activityLog.GetRecent();
        }
        #endregion
        #region Settings
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        private async void NavItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clickedButton) return;
            string targetPage = clickedButton.Tag?.ToString();

            DnsPagePanel.Visibility = Visibility.Collapsed;
            HistoryPagePanel.Visibility = Visibility.Collapsed;
            SpeedTestPagePanel.Visibility = Visibility.Collapsed;
            TroubleshootPagePanel.Visibility = Visibility.Collapsed;
            WifiPagePanel.Visibility = Visibility.Collapsed;
            IpInfoPagePanel.Visibility = Visibility.Collapsed;
            SettingsPagePanel.Visibility = Visibility.Collapsed;

            // نمایش فقط صفحه‌ی انتخاب‌شده
            switch (targetPage)
            {
                case "Dns": DnsPagePanel.Visibility = Visibility.Visible; break;
                case "History":
                    HistoryPagePanel.Visibility = Visibility.Visible;
                    LoadHistory();
                    break;
                case "SpeedTest": SpeedTestPagePanel.Visibility = Visibility.Visible; break;
                case "Troubleshoot": TroubleshootPagePanel.Visibility = Visibility.Visible; break;
                case "Wifi":
                    WifiPagePanel.Visibility = Visibility.Visible;
                    await LoadWifiNetworksAsync();
                    break;
                case "IpInfo":
                    IpInfoPagePanel.Visibility = Visibility.Visible;
                    await LoadMyIpInfoAsync();
                    break;
                case "Settings": SettingsPagePanel.Visibility = Visibility.Visible; break;
            }

            // برگردوندن استایل عادی به همه‌ی دکمه‌های Sidebar
            Style defaultStyle = (Style)FindResource("SidebarButtonStyle");
            NavDnsButton.Style = defaultStyle;
            NavHistoryButton.Style = defaultStyle;
            NavSpeedTestButton.Style = defaultStyle;
            NavTroubleshootButton.Style = defaultStyle;
            NavWifiButton.Style = defaultStyle;
            NavSettingsButton.Style = defaultStyle;
            NavIpInfoButton.Style = defaultStyle;

            // دادن استایل فعال به دکمه‌ای که کلیک شده
            clickedButton.Style = (Style)FindResource("SidebarButtonActiveStyle");
        }
        private void DarkModeButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Resources["BgDark"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x11, 0x17));
            Application.Current.Resources["BgCard"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x29));
            Application.Current.Resources["BgCardHover"] = new SolidColorBrush(Color.FromRgb(0x23, 0x27, 0x39));
            Application.Current.Resources["BgInput"] = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x1C));
            Application.Current.Resources["BgChip"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
            Application.Current.Resources["BorderColor"] = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x3F));
            Application.Current.Resources["TextMuted"] = new SolidColorBrush(Color.FromRgb(0x56, 0x5B, 0x6B));
            Application.Current.Resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(0xF1, 0xF2, 0xF6));
            Application.Current.Resources["TextSecondary"] = new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x9E));
            Application.Current.Resources["SidebarBg"] = new SolidColorBrush(Color.FromRgb(0x15, 0x18, 0x22));

            DarkModeButton.Tag = "Selected";
            LightModeButton.Tag = null;

            var settings = AppSettings.Load();
            settings.IsDarkMode = true;
            settings.Save();
        }
        private void LightModeButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Resources["BgDark"] = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xFA));
            Application.Current.Resources["BgCard"] = new SolidColorBrush(Colors.White);
            Application.Current.Resources["BgCardHover"] = new SolidColorBrush(Color.FromRgb(0xEC, 0xED, 0xF2));
            Application.Current.Resources["BgInput"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF1, 0xF5));
            Application.Current.Resources["BgChip"] = new SolidColorBrush(Color.FromRgb(0xE2, 0xE5, 0xEC));
            Application.Current.Resources["BorderColor"] = new SolidColorBrush(Color.FromRgb(0xDD, 0xE0, 0xE6));
            Application.Current.Resources["TextMuted"] = new SolidColorBrush(Color.FromRgb(0xA0, 0xA5, 0xB0));
            Application.Current.Resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x29));
            Application.Current.Resources["TextSecondary"] = new SolidColorBrush(Color.FromRgb(0x6B, 0x70, 0x80));
            Application.Current.Resources["SidebarBg"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

            LightModeButton.Tag = "Selected";
            DarkModeButton.Tag = null;

            var settings = AppSettings.Load();
            settings.IsDarkMode = false;
            settings.Save();
        }
        private void AccentSwatch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clickedSwatch) return;


            var gradient = (LinearGradientBrush)clickedSwatch.Background;
            Application.Current.Resources["AccentBlue"] = new SolidColorBrush(gradient.GradientStops[0].Color);
            Application.Current.Resources["AccentGradient"] = gradient.Clone();

            AccentSwatchBluePurple.Tag = null;
            AccentSwatchGreen.Tag = null;
            AccentSwatchOrange.Tag = null;
            AccentSwatchPink.Tag = null;
            AccentSwatchCyan.Tag = null;
            AccentSwatchRed.Tag = null;
            AccentSwatchSlate.Tag = null;
            clickedSwatch.Tag = "Selected";

            var settings = AppSettings.Load();
            settings.AccentName = clickedSwatch.Name;
            settings.Save();
        }
        private void Language_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clickedLang) return;

            LangFaButton.Tag = null;
            LangEnButton.Tag = null;
            clickedLang.Tag = "Selected";

            bool isEnglish = clickedLang == LangEnButton;
            string langFile = isEnglish ? "Lang.English.xaml" : "Lang.Persian.xaml";

            var oldDict = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.StartsWith("Lang."));
            if (oldDict != null)
                Application.Current.Resources.MergedDictionaries.Remove(oldDict);

            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(langFile, UriKind.Relative)
            });

            FlowDirection = isEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

            var settings = AppSettings.Load();
            settings.IsEnglish = isEnglish;
            settings.Save();
        }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        #endregion
        #region Windows Tray
        private void InitializeTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location),
                Visible = true,
                Text = "DNS Changer"
            };

            _trayIcon.MouseUp += TrayIcon_MouseUp;

            _trayMenuHost = new Window
            {
                Width = 0,
                Height = 0,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Left = -2000,
                Top = -2000
            };
            _trayMenuHost.Show();
        }
        private void TrayIcon_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                var menu = (ContextMenu)FindResource("TrayContextMenu");
                menu.PlacementTarget = this;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                _trayMenuHost.Activate();
                menu.IsOpen = true;
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                ShowFromTray();
            }
        }
        private void TrayApplyDnsMenu_Click(object sender, RoutedEventArgs e)
        {
            var dnsListMenu = new ContextMenu
            {
                Background = (Brush)FindResource("BgCard"),
                BorderBrush = (Brush)FindResource("BorderColor"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                Template = ((ContextMenu)FindResource("TrayContextMenu")).Template
            };

            foreach (var entry in _customDnsRepository.GetAllDns())
            {
                var item = new MenuItem
                {
                    Header = entry.Name,
                    Style = (Style)FindResource("TrayMenuItemStyle")
                };
                item.Click += (s, ev) => ApplyProviderAndNotify(new DnsProvider
                {
                    Name = entry.Name,
                    Primary = entry.Primary,
                    Secondary = entry.Secondary
                });
                dnsListMenu.Items.Add(item);
            }

            dnsListMenu.PlacementTarget = (MenuItem)sender;
            dnsListMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            _trayMenuHost.Activate();
            dnsListMenu.IsOpen = true;
        }
        private void TrayOpen_Click(object sender, RoutedEventArgs e) => ShowFromTray();
        private void BuildTrayDnsMenu()
        {
            var menu = (ContextMenu)FindResource("TrayContextMenu");
            var dnsMenu = menu.Items.OfType<MenuItem>().FirstOrDefault(m => (string)m.Header == "اعمال DNS");
            if (dnsMenu == null) return;

            dnsMenu.Items.Clear();

            foreach (var entry in _customDnsRepository.GetAllDns())
            {
                var item = new MenuItem
                {
                    Header = entry.Name,
                    Style = (Style)FindResource("TrayMenuItemStyle")
                };
                item.Click += (s, e) => ApplyProviderAndNotify(new DnsProvider
                {
                    Name = entry.Name,
                    Primary = entry.Primary,
                    Secondary = entry.Secondary
                });
                dnsMenu.Items.Add(item);
            }
        }
        private void TrayResetDns_Click(object sender, RoutedEventArgs e)
        {
            _dnsService.UnsetDns();
            _activityLog.Add((string)FindResource("Log_DnsResetFromTray"));
        }
        private async void TrayRunDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            await _diagnosticsService.RunDiagnosticsAsync();
            _activityLog.Add((string)FindResource("Log_DiagnosticsFromTray"));
        }
        private void TrayExit_Click(object sender, RoutedEventArgs e) => ExitApplication();
        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
        private void ExitApplication()
        {
            _isExiting = true;
            Application.Current.Shutdown();
        }
        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
                Hide();
            base.OnStateChanged(e);
        }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExiting)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }
        #endregion

    }
}