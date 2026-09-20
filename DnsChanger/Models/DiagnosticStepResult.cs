using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace DnsChanger.Models
{
    public enum DiagnosticStepType
    {
        AdapterCheck,
        GatewayCheck,
        AdapterRestartAttempt,
        InternetCheck,
        DnsCheck,
        DnsFallbackSuccess,
        DnsFallbackFailure
    }
    public enum DiagnosticStatus { Success, Warning, Failure }
    public class DiagnosticStepResult
    {
        public DiagnosticStepType StepType { get; set; }
        public DiagnosticStatus Status { get; set; }
        public bool IsSuccess { get; set; }

        public string Title { get; set; }
        public string Message { get; set; }
        public string ExtraData { get; set; }

        public Brush StatusColor => IsSuccess
            ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))
            : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    }
}
