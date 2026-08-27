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
    private WebView2?    _webView;
    private NotifyIcon?  _trayIcon;
    private bool         _webViewReady = false;
    private SystemSnapshot? _lastSnap;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Without this, AlertSeverity serializes as 0/1/2 and the dashboard's
        // severity.toLowerCase() throws — killing the Health Alerts panel.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public MainForm(bool startMinimized = false)
    {
        InitializeComponent();
        InitTrayIcon();
        _monitor = new SystemMonitor();
        _monitor.SnapshotReady += OnSnapshotReady;

        if (startMinimized)
        {
            WindowState   = FormWindowState.Minimized;
            ShowInTaskbar = false;
            Visible       = false;
        }
    }

    private void InitializeComponent()
    {
        Text          = "🌽 CornWatch — System Health Dashboard";
        var scale     = DeviceDpi / 96f;
        Size          = new Size((int)(1280 * scale), (int)(800 * scale));
        MinimumSize   = new Size((int)(960  * scale), (int)(640 * scale));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor     = Color.FromArgb(10, 10, 10);

        // Load custom icon (folder on disk is lowercase "assets" — keep casing
        // exact so case-sensitive build environments don't break)
        var icoPath = Path.Combine(AppContext.BaseDirectory, "assets", "cornwatch.ico");
        if (File.Exists(icoPath))
            Icon = new Icon(icoPath);

        _ = InitWebViewAsync();
    }

    // ── System Tray ──────────────────────────────────────────────────────────
    private void InitTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open CornWatch",  null, (_, _) => ShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        var startupItem = new ToolStripMenuItem("Launch at startup")
        {
            Checked      = StartupManager.IsEnabled,
            CheckOnClick = true,
        };
        startupItem.CheckedChanged += (_, _) =>
        {
            try { StartupManager.Toggle(); }
            catch (Exception ex) { MessageBox.Show("Could not update startup: " + ex.Message); }
        };
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => { _trayIcon!.Visible = false; Application.Exit(); });

        _trayIcon = new NotifyIcon
        {
            Text    = "CornWatch",
            Icon    = this.Icon ?? SystemIcons.Application,
            Visible = true,
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
        _monitor.SetPollInterval(1000);   // back to live 1s updates
    }

    private bool _balloonShown = false;
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            ShowInTaskbar = false;
            Hide();
            _monitor.SetPollInterval(5000);   // ease off while hidden in tray
            if (!_balloonShown)
            {
                _trayIcon!.ShowBalloonTip(2000, "CornWatch", "Minimized to tray — double-click to restore", ToolTipIcon.Info);
                _balloonShown = true;
            }
        }
    }

    // ── WebView2 ─────────────────────────────────────────────────────────────
    private async Task InitWebViewAsync()
    {
        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);
        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.AddHostObjectToScript("cornBridge", new CornBridge(this, _monitor));

#if !DEBUG
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.IsStatusBarEnabled            = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled            = false;
#endif

        var htmlPath = Path.Combine(AppContext.BaseDirectory, "UI", "Dashboard", "dashboard.html");
        _webView.CoreWebView2.Navigate(File.Exists(htmlPath)
            ? "file:///" + htmlPath.Replace('\\', '/')
            : "about:blank");

        _webView.CoreWebView2.NavigationCompleted += (_, _) => _webViewReady = true;
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

    /// <summary>Captures the dashboard as a PNG into Documents/CornWatch/Snapshots/.</summary>
    internal async void ExportPngFromUi()
    {
        try
        {
            if (_webView?.CoreWebView2 is null) return;
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "CornWatch", "Snapshots");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"snapshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");

            await using (var fs = File.Create(path))
                await _webView.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png, fs);

            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show("PNG export failed: " + ex.Message, "CornWatch");
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Close button minimizes to tray; tray Exit menu actually quits
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            _monitor.SetPollInterval(5000);   // ease off while hidden in tray
            return;
        }
        _trayIcon?.Dispose();
        _monitor.Dispose();
        base.OnFormClosing(e);
    }
}

// ── JS ↔ C# bridge ───────────────────────────────────────────────────────────
[System.Runtime.InteropServices.ComVisible(true)]
public class CornBridge(MainForm form, SystemMonitor monitor)
{
    public void OpenProcessManager()  => System.Diagnostics.Process.Start("taskmgr.exe");
    public void OpenResourceMonitor() => System.Diagnostics.Process.Start("resmon.exe");
    public string GetGpuSensorDump()  => monitor.GpuSensorDump;

    public bool GetStartupEnabled() => StartupManager.IsEnabled;
    public void SetStartupEnabled(bool enabled)
    {
        if (enabled) StartupManager.Enable();
        else         StartupManager.Disable();
    }

    /// <summary>Captures the dashboard as a PNG (fire-and-forget; runs on the UI thread).</summary>
    public void ExportPng() => form.BeginInvoke((Action)form.ExportPngFromUi);

    /// <summary>Exports the last snapshot to Documents/CornWatch/Snapshots/ and returns the path.</summary>
    public string ExportSnapshot()
    {
        var snap = form.LastSnapshot;
        if (snap is null) return "No snapshot available yet.";
        try
        {
            var path = SnapshotExporter.Export(snap);
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            return path;
        }
        catch (Exception ex) { return "Export failed: " + ex.Message; }
    }
}