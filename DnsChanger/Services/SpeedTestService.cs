using DnsChanger.Models;
using System.Diagnostics;
using System.Net.Http;

namespace DnsChanger.Services
{
    public class SpeedTestService : ISpeedTestService
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly IPingService _pingService;
        public SpeedTestService(IPingService pingService)
        {
            _pingService = pingService;
        }
        public async Task<SpeedTestResult> RunTestAsync(IProgress<SpeedTestProgress> progress)
        {
            var result = new SpeedTestResult();

            progress?.Report(new SpeedTestProgress { Phase = SpeedTestPhase.DataCenterLookup });
            result.DataCenter = await GetDataCenterAsync().ConfigureAwait(false);
            progress?.Report(new SpeedTestProgress { Phase =SpeedTestPhase.DownloadTest, DataCenter = result.DataCenter });

            result.DownloadMbps = await TestDownloadAsync(progress).ConfigureAwait(false);
            progress.Report(new SpeedTestProgress { Phase = SpeedTestPhase.UploadTest, FinalDownloadMbps = result.DownloadMbps });

            result.UploadMbps = await TestUploadAsync(progress).ConfigureAwait(false);
            progress?.Report(new SpeedTestProgress { Phase = SpeedTestPhase.PingTest, FinalUploadMbps = result.UploadMbps });

            var (avgPing, jitter) = await MeasureLatencyAsync().ConfigureAwait(false);
            result.PingMs = avgPing;
            result.JitterMs = jitter;

            progress?.Report(new SpeedTestProgress { Phase = SpeedTestPhase.Done, PercentComplete = 100 });
            return result;
        }
        private async Task<(long avgPing, long jitter)> MeasureLatencyAsync()
        {
            var samples = new List<long>();
            for (int i = 0; i < 6; i++)
            {
                var ms = await _pingService.PingAsync("1.1.1.1").ConfigureAwait(false);
                if (ms.HasValue) samples.Add(ms.Value);
                await Task.Delay(100).ConfigureAwait(false);
            }

            if (samples.Count == 0) return (0, 0);
            
            long avg = (long)samples.Average();
            long jitterSum = 0;
            for (int i = 1; i < samples.Count; i++)
                jitterSum += Math.Abs(samples[i] - samples[i - 1]);
            long jitter = samples.Count > 1 ? jitterSum / (samples.Count - 1) : 0;

            return (avg, jitter);
        }
        private async Task<string> GetDataCenterAsync()
        {
            try
            {
                var trace = await _http.GetStringAsync("https://speed.cloudflare.com/cdn-cgi/trace").ConfigureAwait(false);
                var line = trace.Split('\n').FirstOrDefault(l => l.StartsWith("colo="));
                return line != null ? line.Substring(5) : "نامشخص";
            }
            catch { return "نامشخص"; }
        }
        private async Task<double> TestDownloadAsync(IProgress<SpeedTestProgress> progress)
        {
            try
            {
                const long totalBytes = 25_000_000;
                using var response = await _http.GetAsync(
                    $"https://speed.cloudflare.com/__down?bytes={totalBytes}",
                    HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

                var buffer = new byte[65536];
                long totalRead = 0;
                var sw = Stopwatch.StartNew();
                double lastReportSeconds = 0;

                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    totalRead += bytesRead;
                    double elapsed = sw.Elapsed.TotalSeconds;

                    if (elapsed - lastReportSeconds > 0.15)
                    {
                        double currentMbps = (totalRead * 8.0 / 1_000_000.0) / elapsed;
                        progress?.Report(new SpeedTestProgress
                        {
                            Phase = SpeedTestPhase.DownloadTest,
                            CurrentMbps = Math.Round(currentMbps, 1),
                            PercentComplete = Math.Min(100, totalRead * 100.0 / totalBytes)
                        });
                        lastReportSeconds = elapsed;
                    }
                }
                sw.Stop();
                return Math.Round((totalRead * 8.0 / 1_000_000.0) / sw.Elapsed.TotalSeconds, 1);
            }
            catch { return 0; }
        }

        private async Task<double> TestUploadAsync(IProgress<SpeedTestProgress> progress)
        {
            try
            {
                const int totalBytes = 6_000_000;
                const int chunkCount = 10;
                const int chunkSize = totalBytes / chunkCount;

                var random = new Random();
                long totalSent = 0;
                var overallSw = Stopwatch.StartNew();

                for (int i = 0; i < chunkCount; i++)
                {
                    var chunk = new byte[chunkSize];
                    random.NextBytes(chunk);

                    var chunkSw = Stopwatch.StartNew();
                    await _http.PostAsync("https://speed.cloudflare.com/__up", new ByteArrayContent(chunk)).ConfigureAwait(false);
                    chunkSw.Stop();

                    totalSent += chunkSize;
                    double chunkMbps = (chunkSize * 8.0 / 1_000_000.0) / chunkSw.Elapsed.TotalSeconds;

                    progress?.Report(new SpeedTestProgress
                    {
                        Phase = SpeedTestPhase.UploadTest,
                        CurrentMbps = Math.Round(chunkMbps, 1),
                        PercentComplete = (i + 1) * 100.0 / chunkCount
                    });
                }
                overallSw.Stop();
                return Math.Round((totalSent * 8.0 / 1_000_000.0) / overallSw.Elapsed.TotalSeconds, 1);
            }
            catch { return 0; }
        }
    }
}
