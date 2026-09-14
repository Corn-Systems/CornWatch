using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using CornWatch.Core;
using CornWatch.Models;

namespace CornWatch.UI.Dashboard;

public partial class MainForm : Form
{
    private readonly SystemMonitor _monitor;
    private readonly AppSettings   _settings;
    private WebView2?    _webView;
    private NotifyIcon?  _trayIcon;
    private bool         _webViewReady;
    private SystemSnapshot? _lastSnap;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public MainForm(bool startMinimized = false)
    {
        _settings = SettingsManager.Current;

        InitializeComponent();
        RestoreWindowGeometry();
        InitTrayIcon();

        _monitor = new SystemMonitor();
        _monitor.PollIntervalMs = _settings.PollIntervalMs;
        _monitor.SnapshotReady += OnSnapshotReady;

        if (startMinimized)
        {
            WindowState   = FormWindowState.Minimized;
            ShowInTaskbar = false;
            Visible       = false;
        }

        // Fire-and-forget: check for updates after a short delay so the dashboard
        // is already visible when the notification appears.
        if (_settings.CheckForUpdates)
            _ = CheckForUpdateAsync();
    }

    // ── Window geometry ───────────────────────────────────────────────────────

    private void RestoreWindowGeometry()
    {
        Size = new Size(_settings.WindowWidth, _settings.WindowHeight);
        if (_settings.WindowState == "Maximized")
            WindowState = FormWindowState.Maximized;
    }

    private void SaveWindowGeometry()
    {
        if (WindowState == FormWindowState.Normal)
        {
            _settings.WindowWidth  = Width;
            _settings.WindowHeight = Height;
        }
        _settings.WindowState = WindowState == FormWindowState.Maximized ? "Maximized" : "Normal";
        SettingsManager.Save(_settings);
    }

    // ── Component initialisation ──────────────────────────────────────────────

    private void InitializeComponent()
    {
        Text          = "🌽 CornWatch — System Health Dashboard";
        MinimumSize   = new Size(960, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor     = Color.FromArgb(10, 10, 10);

        var icoPath = Path.Combine(AppContext.BaseDirectory, "assets", "cornwatch.ico");
        if (File.Exists(icoPath))
        {
            try { Icon = new Icon(icoPath); }
            catch (Exception ex) { SessionLog.Write("MAINFORM_ICON", ex); }
        }

        _ = InitWebViewAsync();
    }

    // ── Update check ─────────────────────────────────────────────────────────

    private async Task CheckForUpdateAsync()
    {
        await Task.Delay(3000); // let the dashboard render first
        try
        {
            var info = await UpdateChecker.CheckAsync();
            if (info?.IsNewer == true)
            {
                // Show a non-blocking tray balloon; clicking it opens the releases page.
                _trayIcon?.ShowBalloonTip(
                    6000,
                    "CornWatch update available",
                    $"Version {info.LatestTag} is available — click to download.",
                    ToolTipIcon.Info);

                if (_trayIcon is not null)
                    _trayIcon.BalloonTipClicked += (_, _) => OpenUrl(info.ReleaseUrl);
            }
        }
        catch (Exception ex) { SessionLog.Write("UPDATE_CHECK", ex); }
    }

    // ── System tray ──────────────────────────────────────────────────────────

    private void InitTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open CornWatch", null, (_, _) => ShowWindow());
        menu.Items.Add(new ToolStripSeparator());

        var startupItem = new ToolStripMenuItem("Launch at startup")
        {
            Checked      = StartupManager.IsEnabled,
            CheckOnClick = true,
        };
        startupItem.CheckedChanged += (_, _) =>
        {
            try { StartupManager.Toggle(); }
            catch (Exception ex)
            {
                MessageBox.Show("Could not update startup: " + ex.Message, "CornWatch",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            if (_trayIcon is not null) _trayIcon.Visible = false;
            Application.Exit();
        });

        _trayIcon = new NotifyIcon
        {
            Text             = "CornWatch",
            Icon             = Icon ?? SystemIcons.Application,
            Visible          = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private void ShowWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState   = FormWindowState.Normal;
        Activate();
        BringToFront();
        _monitor.SetPollInterval(1000);
    }

    private bool _balloonShown;
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            ShowInTaskbar = false;
            Hide();
            _monitor.SetPollInterval(5000);
            if (!_balloonShown)
            {
                _trayIcon?.ShowBalloonTip(2000, "CornWatch",
                    "Minimised to tray — double-click to restore", ToolTipIcon.Info);
                _balloonShown = true;
            }
        }
    }

    // ── WebView2 ─────────────────────────────────────────────────────────────

    private async Task InitWebViewAsync()
    {
        try
        {
            _webView = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(_webView);

            try
            {
                await _webView.EnsureCoreWebView2Async();
            }
            catch (Exception ex)
            {
                SessionLog.Write("WEBVIEW2_INIT", ex);
                // WebView2 Runtime not installed — show a plain fallback label.
                Controls.Remove(_webView);
                _webView.Dispose();
                _webView = null;
                ShowWebView2FallbackUi();
                return;
            }

            _webView.CoreWebView2.AddHostObjectToScript("cornBridge",
                new CornBridge(this, _monitor));

#if !DEBUG
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled            = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled            = false;
#endif

            var htmlPath = Path.Combine(AppContext.BaseDirectory,
                "UI", "Dashboard", "dashboard.html");

            // Use Uri.AbsoluteUri instead of raw string replace for spec-correct file:/// URL.
            string url = File.Exists(htmlPath)
                ? new Uri(htmlPath).AbsoluteUri
                : "about:blank";

            _webView.CoreWebView2.Navigate(url);
            _webView.CoreWebView2.NavigationCompleted += (_, _) => _webViewReady = true;
        }
        catch (Exception ex)
        {
            SessionLog.Write("WEBVIEW2_SETUP", ex);
        }
    }

    private void ShowWebView2FallbackUi()
    {
        var label = new Label
        {
            Text      = "⚠  WebView2 Runtime is not installed.\n\n" +
                        "Download it from:\nhttps://developer.microsoft.com/en-us/microsoft-edge/webview2/\n\n" +
                        "Restart CornWatch after installing.",
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(242, 176, 74),
            BackColor = Color.FromArgb(10, 10, 10),
            Font      = new Font("Courier New", 10f),
        };
        Controls.Add(label);
    }

    // ── Data bridge ──────────────────────────────────────────────────────────

    private void OnSnapshotReady(SystemSnapshot snap)
    {
        _lastSnap = snap;
        if (!_webViewReady || _webView is null) return;
        var json = JsonSerializer.Serialize(snap, _jsonOpts);
        if (InvokeRequired) Invoke(() => PushToJs(json));
        else PushToJs(json);
    }

    internal void PushToJs(string json) =>
        _ = _webView?.CoreWebView2.ExecuteScriptAsync($"window.cornWatch?.onSnapshot({json})");

    internal SystemSnapshot? LastSnapshot => _lastSnap;

    // ── PNG export ───────────────────────────────────────────────────────────

    internal async void ExportPngFromUi()
    {
        try
        {
            if (_webView?.CoreWebView2 is null) return;
            AppPaths.EnsureSnapshotsDir();
            var path = Path.Combine(AppPaths.SnapshotsDir,
                $"snapshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");

            await using (var fs = File.Create(path))
                await _webView.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png, fs);

            SessionLog.Write("[EXPORT] PNG written to " + path);
            OpenInExplorer(path);
        }
        catch (Exception ex)
        {
            SessionLog.Write("EXPORT_PNG", ex);
            MessageBox.Show("PNG export failed: " + ex.Message, "CornWatch",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void OpenInExplorer(string path)
    {
        try
        {
            var p = System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            if (p is null) SessionLog.Write("[SHELL] explorer.exe returned null handle");
        }
        catch (Exception ex) { SessionLog.Write("SHELL_EXPLORER", ex); }
    }

    internal static void OpenUrl(string url)
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                { UseShellExecute = true });
            if (p is null) SessionLog.Write($"[SHELL] browser launch returned null for {url}");
        }
        catch (Exception ex) { SessionLog.Write("SHELL_URL", ex); }
    }

    // ── Form close ───────────────────────────────────────────────────────────

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // X button → minimise to tray; tray Exit actually quits.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            _monitor.SetPollInterval(5000);
            return;
        }

        SaveWindowGeometry();
        _trayIcon?.Dispose();
        _monitor.Dispose();
        base.OnFormClosing(e);
    }
}

// ── JS ↔ C# bridge ───────────────────────────────────────────────────────────

[System.Runtime.InteropServices.ComVisible(true)]
public class CornBridge(MainForm form, SystemMonitor monitor)
{
    public void OpenProcessManager()
    {
        try
        {
            var p = System.Diagnostics.Process.Start("taskmgr.exe");
            if (p is null) SessionLog.Write("[BRIDGE] taskmgr.exe returned null handle");
        }
        catch (Exception ex) { SessionLog.Write("BRIDGE_TASKMGR", ex); }
    }

    public void OpenResourceMonitor()
    {
        try
        {
            var p = System.Diagnostics.Process.Start("resmon.exe");
            if (p is null) SessionLog.Write("[BRIDGE] resmon.exe returned null handle");
        }
        catch (Exception ex) { SessionLog.Write("BRIDGE_RESMON", ex); }
    }

    public string GetGpuSensorDump() => monitor.GpuSensorDump;

    public bool   GetStartupEnabled() => StartupManager.IsEnabled;
    public void   SetStartupEnabled(bool enabled)
    {
        if (enabled) StartupManager.Enable();
        else         StartupManager.Disable();
    }

    /// <summary>Captures the dashboard as a PNG (fire-and-forget; runs on the UI thread).</summary>
    public void ExportPng() => form.BeginInvoke((Action)form.ExportPngFromUi);

    /// <summary>Exports the last snapshot to the Snapshots folder and returns the file path.</summary>
    public string ExportSnapshot()
    {
        var snap = form.LastSnapshot;
        if (snap is null) return "No snapshot available yet.";
        try
        {
            var path = SnapshotExporter.Export(snap);
            MainForm.OpenInExplorer(path);
            return path;
        }
        catch (Exception ex)
        {
            SessionLog.Write("BRIDGE_EXPORT", ex);
            return "Export failed: " + ex.Message;
        }
    }

    // Expose current thresholds to the dashboard so it can render the
    // configurable values instead of hardcoded JS constants.
    public string GetSettings()
    {
        var s = SettingsManager.Current;
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            cpuWarnPercent     = s.CpuWarnPercent,
            cpuCritPercent     = s.CpuCritPercent,
            cpuWarnTempC       = s.CpuWarnTempC,
            cpuCritTempC       = s.CpuCritTempC,
            ramWarnPercent     = s.RamWarnPercent,
            ramCritPercent     = s.RamCritPercent,
            diskWarnPercent    = s.DiskWarnPercent,
            diskCritPercent    = s.DiskCritPercent,
            gpuWarnTempC       = s.GpuWarnTempC,
            gpuCritTempC       = s.GpuCritTempC,
            gpuVramWarnPercent = s.GpuVramWarnPercent,
            gpuVramCritPercent = s.GpuVramCritPercent,
        });
    }
}
