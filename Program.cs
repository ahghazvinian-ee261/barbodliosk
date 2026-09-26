using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BarbodKiosk
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // Ensure only one instance runs at a time.
            using var mutex = new Mutex(true, "BarbodKiosk_SingleInstance", out bool isNew);
            if (!isNew)
            {
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new KioskForm());
        }
    }

    /// <summary>
    /// Borderless fullscreen kiosk window that hosts a WebView2 browser,
    /// pinned to the largest / extended monitor, with polling-driven
    /// URL switching and hard reloads — mirrors the original Python/Selenium script.
    /// </summary>
    public class KioskForm : Form
    {
        // --------------------------------------------------
        // CONFIG (mirrors the Python script)
        // --------------------------------------------------
        private const string UrlSource = "https://www.barbodinstitute.ir/clock/calendar/url.txt";
        private const string TxtUrl = "https://wwww.barbodinstitute.ir/clock/calendar/number.txt";
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

        private readonly HttpClient _http;
        private WebView2? _webView;
        private System.Windows.Forms.Timer? _pollTimer;

        private string? _targetUrl;
        private string? _lastCode;
        private bool _isPolling; // reentrancy guard for the timer tick

        public KioskForm()
        {
            _http = new HttpClient { Timeout = HttpTimeout };
            _http.DefaultRequestHeaders.CacheControl =
                new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

            // ---- Window chrome: true borderless fullscreen ----
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal; // we set bounds manually to the target screen
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = System.Drawing.Color.Black;
            Text = "Barbod Kiosk";
            ShowInTaskbar = false;

            PositionOnLargestExtendedMonitor();

            Load += KioskForm_Load;
            KeyDown += KioskForm_KeyDown;
            KeyPreview = true;
        }

        // --------------------------------------------------
        // MONITOR SELECTION: largest area, preferring an
        // extended (non-primary) monitor when it's the biggest.
        // --------------------------------------------------
        private void PositionOnLargestExtendedMonitor()
        {
            var screens = Screen.AllScreens;

            var chosen = screens
                .OrderByDescending(s => (long)s.Bounds.Width * s.Bounds.Height) // largest first
                .ThenBy(s => s.Primary ? 1 : 0)   // tie-break: prefer non-primary (extended)
                .First();

            Bounds = chosen.Bounds; // exact pixel bounds of that monitor, true fullscreen
        }

        // --------------------------------------------------
        // ESC to exit (handy for kiosk maintenance); remove
        // if you want it fully locked down.
        // --------------------------------------------------
        private void KioskForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && e.Control && e.Shift)
            {
                Application.Exit();
            }
        }

        private async void KioskForm_Load(object? sender, EventArgs e)
        {
            _webView = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(_webView);

            // Use a dedicated user-data folder next to the exe so it's portable
            // and so we can fully control caching.
            var userDataFolder = Path.Combine(
                Path.GetDirectoryName(Application.ExecutablePath) ?? AppContext.BaseDirectory,
                "WebView2Data");

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);

            await _webView.EnsureCoreWebView2Async(env);

            ConfigureWebViewSettings();

            _targetUrl = await GetTargetUrlAsync();
            if (string.IsNullOrEmpty(_targetUrl))
            {
                MessageBox.Show(
                    "Failed to load target URL from server. Exiting.",
                    "Barbod Kiosk",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Application.Exit();
                return;
            }

            _webView.CoreWebView2.Navigate(_targetUrl);

            _lastCode = await GetCurrentCodeAsync();

            _pollTimer = new System.Windows.Forms.Timer { Interval = (int)CheckInterval.TotalMilliseconds };
            _pollTimer.Tick += PollTimer_Tick;
            _pollTimer.Start();
        }

        private void ConfigureWebViewSettings()
        {
            if (_webView?.CoreWebView2 == null) return;

            var settings = _webView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;

            // Disable disk cache equivalent: WebView2 doesn't expose a direct
            // "disable cache" flag, so we rely on cache-busting query params
            // (see HardReloadAsync) plus clearing browsing data on each reload.
        }

        // --------------------------------------------------
        // GET TARGET URL FROM SERVER FILE
        // --------------------------------------------------
        private async Task<string?> GetTargetUrlAsync()
        {
            try
            {
                var text = await _http.GetStringAsync(UrlSource);
                var url = text.Trim();
                if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("URL ERROR: " + ex.Message);
            }

            return null;
        }

        // --------------------------------------------------
        // GET CURRENT 4-DIGIT CODE
        // --------------------------------------------------
        private async Task<string?> GetCurrentCodeAsync()
        {
            try
            {
                var text = await _http.GetStringAsync(TxtUrl);
                var match = Regex.Match(text, @"\b(\d{4})\b");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("CODE ERROR: " + ex.Message);
            }

            return null;
        }

        // --------------------------------------------------
        // POLL LOOP
        // --------------------------------------------------
        private async void PollTimer_Tick(object? sender, EventArgs e)
        {
            if (_isPolling) return; // avoid overlapping ticks if a request is slow
            _isPolling = true;

            try
            {
                var newCode = await GetCurrentCodeAsync();
                Console.WriteLine("Current code: " + newCode);

                if (newCode != null && newCode != _lastCode)
                {
                    Console.WriteLine($"CODE CHANGED: {_lastCode} -> {newCode}");
                    await HardReloadAsync();
                    _lastCode = newCode;
                }
            }
            finally
            {
                _isPolling = false;
            }
        }

        private async Task HardReloadAsync()
        {
            Console.WriteLine("Checking URL + reloading...");

            var newUrl = await GetTargetUrlAsync();
            if (newUrl != null && newUrl != _targetUrl)
            {
                Console.WriteLine($"URL CHANGED: {_targetUrl} -> {newUrl}");
                _targetUrl = newUrl;
            }

            if (_webView?.CoreWebView2 == null || _targetUrl == null) return;

            try
            {
                // Clear storage, mirroring localStorage.clear()/sessionStorage.clear()
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    "localStorage.clear(); sessionStorage.clear();");
            }
            catch
            {
                // ignore, mirrors the Python try/except pass
            }

            var cacheBusted = _targetUrl +
                (_targetUrl.Contains('?') ? "&" : "?") +
                "nocache=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            _webView.CoreWebView2.Navigate(cacheBusted);

            // Re-assert fullscreen bounds in case anything (e.g. a monitor
            // hot-plug event) changed them.
            PositionOnLargestExtendedMonitor();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _pollTimer?.Stop();
            _pollTimer?.Dispose();
            _http.Dispose();
            base.OnFormClosed(e);
        }
    }
}
