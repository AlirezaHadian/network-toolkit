using System.Windows;
using System.Windows.Media;

namespace DnsChanger
{
    public enum DialogType { Success, Error, Warning, Question }

    public partial class CustomDialog : Window
    {
        public bool Result { get; private set; }

        private CustomDialog(string title, string message, DialogType type, bool showCancel)
        {
            InitializeComponent();

            // جهت دیالوگ رو از پنجره‌ی اصلی می‌گیریم — فارسی=RTL، انگلیسی=LTR
            FlowDirection = Application.Current.MainWindow?.FlowDirection ?? FlowDirection.RightToLeft;

            TitleText.Text = title;
            MessageText.Text = message;

            Brush circleColor;
            switch (type)
            {
                case DialogType.Success:
                    SuccessIcon.Visibility = Visibility.Visible;
                    circleColor = (Brush)FindResource("Success");
                    break;
                case DialogType.Error:
                    ErrorIcon.Visibility = Visibility.Visible;
                    circleColor = (Brush)FindResource("Danger");
                    break;
                case DialogType.Warning:
                    WarningIcon.Visibility = Visibility.Visible;
                    circleColor = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16));
                    break;
                default: // Question
                    QuestionIcon.Visibility = Visibility.Visible;
                    circleColor = (Brush)FindResource("AccentGradient");
                    break;
            }
            IconCircle.Background = circleColor;

            if (showCancel)
            {
                SecondaryButton.Visibility = Visibility.Visible;
                PrimaryButton.Content = Loc("Dialog_YesButton", "بله");
                SecondaryButton.Content = Loc("Dialog_NoButton", "خیر");
            }
            else
            {
                PrimaryButton.Content = Loc("Dialog_OkButton", "باشه");
            }
        }

        private void PrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            DialogResult = true;
        }

        private void SecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            DialogResult = false;
        }

        // هرجا یه Key تو دیکشنری زبان فعلی پیدا نشه، به‌جای Exception و کرش،
        // این مقدار Fallback رو برمی‌گردونه
        private static string Loc(string key, string fallback)
        {
            return Application.Current.TryFindResource(key) as string ?? fallback;
        }

        public static void ShowInfo(string message, string title = null)
        {
            title ??= Loc("Dialog_InfoTitle", "توجه");
            var dialog = new CustomDialog(title, message, DialogType.Success, showCancel: false);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
        }

        public static void ShowError(string message, string title = null)
        {
            title ??= Loc("Dialog_ErrorTitle", "خطا");
            var dialog = new CustomDialog(title, message, DialogType.Error, showCancel: false);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
        }

        public static void ShowWarning(string message, string title = null)
        {
            title ??= Loc("Dialog_WarningTitle", "هشدار");
            var dialog = new CustomDialog(title, message, DialogType.Warning, showCancel: false);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
        }

        public static bool Confirm(string message, string title = null)
        {
            title ??= Loc("Dialog_ConfirmTitle", "تایید");
            var dialog = new CustomDialog(title, message, DialogType.Question, showCancel: true);
            dialog.Owner = Application.Current.MainWindow;
            dialog.ShowDialog();
            return dialog.Result;
        }

        public static string PromptPassword(string ssid)
        {
            string title = string.Format(Loc("Wifi_PasswordTitle", "اتصال به «{0}»"), ssid);
            string message = Loc("Wifi_PasswordPrompt", "رمز عبور شبکه رو وارد کن:");

            var dialog = new CustomDialog(title, message, DialogType.Question, showCancel: true);
            dialog.PasswordInput.Visibility = Visibility.Visible;
            dialog.PrimaryButton.Content = Loc("Wifi_ConnectButton", "اتصال");
            dialog.SecondaryButton.Content = Loc("Dialog_CancelButton", "انصراف");
            dialog.Owner = Application.Current.MainWindow;

            bool? result = dialog.ShowDialog();
            return result == true ? dialog.PasswordInput.Password : null;
        }
    }
}
