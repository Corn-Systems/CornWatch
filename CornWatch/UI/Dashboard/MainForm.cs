using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using CornWatch.Core;
using CornWatch.Models;

namespace CornWatch.UI.Dashboard;

public class MainForm : Form
{
    private const int TrayPollIntervalMs = 5000;
    private static readonly TimeSpan _alertCooldown = TimeSpan.FromMinutes(5);

    private readonly SystemMonitor _monitor;
    private readonly AppSettings _settings = SettingsManager.Current;
    private readonly NotifyIcon _trayIcon;
    private readonly CancellationTokenSource _cts = new();

    // Critical-alert notification state; only touched on the poll thread.
    private readonly Dictionary<string, DateTime> _alertNotifiedUtc = [];
    private HashSet<string> _activeCritical = [];

    private WebView2? _webView;
    private Action? _balloonAction;
    private FormWindowState _restoreState = FormWindowState.Normal;
    private volatile bool _webViewReady;
    private bool _startHidden, _inTray, _trayHintShown, _webViewStarted;

    internal SystemSnapshot? LastSnapshot { get; private set; }

    public MainForm(bool startMinimized = false)
    {
        Text = "🌽 CornWatch — System Health Dashboard";
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        Size = new Size(_settings.WindowWidth, _settings.WindowHeight);
        if (_settings.WindowState == "Maximized") WindowState = _restoreState = FormWindowState.Maximized;

        var icoPath = Path.Combine(AppContext.BaseDirectory, "assets", "cornwatch.ico");
        if (File.Exists(icoPath))
            try { Icon = new Icon(icoPath); }
            catch (Exception ex) { SessionLog.Write("MAINFORM_ICON", ex); }

        _trayIcon = CreateTrayIcon();
        _startHidden = _inTray = startMinimized;

        _monitor = new SystemMonitor(PollInterval);
        _monitor.SnapshotReady += OnSnapshotReady;

        if (_settings.CheckForUpdates) _ = CheckForUpdateAsync();
    }

    private int PollInterval => _inTray ? Math.Max(TrayPollIntervalMs, _settings.PollIntervalMs) : _settings.PollIntervalMs;

    internal static void Launch(string file, string args = "")
    {
        try { using var proc = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }); }
        catch (Exception ex) { SessionLog.Write($"[SHELL] {file}", ex); }
    }

    internal static void OpenInExplorer(string path) => Launch("explorer.exe", $"/select,\"{path}\"");

    // Called by the bridge after the dashboard saved new settings.
    internal void ApplySettings() => _monitor.SetPollInterval(PollInterval);

    internal async void ExportPngFromUi()
    {
        try
        {
            if (_webView?.CoreWebView2 is not { } core) return;
            var path = AppPaths.SnapshotFile(DateTime.Now, "png");
            await using (var fs = File.Create(path))
                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);

            SessionLog.Write("[EXPORT] PNG written to " + path);
            OpenInExplorer(path);
        }
        catch (Exception ex)
        {
            SessionLog.Write("EXPORT_PNG", ex);
            MessageBox.Show("PNG export failed: " + ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // --minimized / "start minimized": swallow the first show so the window never flashes,
    // but still create the handle so the poll thread can post to the UI thread.
    protected override void SetVisibleCore(bool value)
    {
        if (_startHidden)
        {
            _startHidden = false;
            if (!IsHandleCreated) CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_webViewStarted) return;
        _webViewStarted = true;
        _ = InitWebViewAsync();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState != FormWindowState.Minimized)
        {
            _restoreState = WindowState;
            return;
        }

        HideToTray();
        if (_trayHintShown) return;
        _trayHintShown = true;
        ShowBalloon(AppInfo.Name, "Still running in the tray — double-click the icon to restore.", ToolTipIcon.Info, ShowWindow);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // X button → minimise to tray; tray Exit actually quits.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (bounds.Width >= MinimumSize.Width && bounds.Height >= MinimumSize.Height)
        {
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
        }
        _settings.WindowState = _restoreState == FormWindowState.Maximized ? "Maximized" : "Normal";
        SettingsManager.Save();

        _cts.Cancel();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _monitor.Dispose();
        base.OnFormClosing(e);
    }

    private NotifyIcon CreateTrayIcon()
    {
        var startupItem = new ToolStripMenuItem("Launch at startup") { CheckOnClick = true };
        startupItem.Click += (_, _) =>
        {
            try { StartupManager.SetEnabled(startupItem.Checked); }
            catch (Exception ex)
            {
                startupItem.Checked = StartupManager.IsEnabled;
                MessageBox.Show("Could not update startup: " + ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open CornWatch", null, (_, _) => ShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        // The dashboard has its own startup toggle, so re-read the real state each time the menu opens.
        menu.Opening += (_, _) => startupItem.Checked = StartupManager.IsEnabled;

        var icon = new NotifyIcon
        {
            Text = AppInfo.Name,
            Icon = Icon ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
        };
        icon.DoubleClick += (_, _) => ShowWindow();
        icon.BalloonTipClicked += (_, _) => _balloonAction?.Invoke();
        return icon;
    }

    // One balloon at a time; clicking it runs the action of whichever balloon was shown last.
    private void ShowBalloon(string title, string text, ToolTipIcon icon, Action onClick)
    {
        _balloonAction = onClick;
        _trayIcon.ShowBalloonTip(6000, title, text, icon);
    }

    private void ShowWindow()
    {
        _inTray = false;
        ShowInTaskbar = true;
        Show();
        WindowState = _restoreState;
        Activate();
        _monitor.SetPollInterval(PollInterval);
    }

    private void HideToTray()
    {
        _inTray = true;
        ShowInTaskbar = false;
        Hide();
        _monitor.SetPollInterval(PollInterval);
    }

    private async Task CheckForUpdateAsync()
    {
        var ct = _cts.Token;
        try
        {
            await Task.Delay(3000, ct); // let the dashboard render first
            var info = await UpdateChecker.CheckAsync(ct);
            if (info?.IsNewer != true) return;

            ShowBalloon("CornWatch update available", $"Version {info.LatestTag ?? "?"} is available — click to download.",
                ToolTipIcon.Info, () => Launch(info.ReleaseUrl ?? AppInfo.ReleasesUrl));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SessionLog.Write("UPDATE_CHECK", ex); }
    }

    private async Task InitWebViewAsync()
    {
        var view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Theme.Bg };
        try
        {
            Controls.Add(view);
            AppPaths.EnsureDir(AppPaths.WebViewDataDir);
            var environment = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDataDir);
            await view.EnsureCoreWebView2Async(environment);

            var htmlPath = Path.Combine(AppContext.BaseDirectory, "UI", "Dashboard", "Dashboard.html");
            if (!File.Exists(htmlPath)) throw new FileNotFoundException("Dashboard.html is missing from the install folder.", htmlPath);

            var core = view.CoreWebView2;
            core.AddHostObjectToScript("cornBridge", new CornBridge(this, _monitor));
#if !DEBUG
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
#endif
            core.NavigationCompleted += (_, e) => _webViewReady = e.IsSuccess;
            core.Navigate(new Uri(htmlPath).AbsoluteUri);
            _webView = view;
        }
        catch (Exception ex)
        {
            SessionLog.Write("WEBVIEW2_INIT", ex);
            if (IsDisposed) return;
            Controls.Remove(view);
            view.Dispose();
            Controls.Add(new Label
            {
                Text = ex is WebView2RuntimeNotFoundException
                    ? "⚠  WebView2 Runtime is not installed.\n\n" +
                      "Download it from:\nhttps://developer.microsoft.com/en-us/microsoft-edge/webview2/\n\n" +
                      "Restart CornWatch after installing."
                    : "⚠  The dashboard could not be started.\n\n" + ex.Message +
                      "\n\nDetails are in the log under %AppData%\\CornSystems\\CornWatch\\logs.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Warning,
                BackColor = Theme.Bg,
                Font = new Font("Courier New", 10f),
            });
        }
    }

    // Runs on the poll thread; posts to the UI thread without blocking it.
    private void OnSnapshotReady(SystemSnapshot snap)
    {
        LastSnapshot = snap;
        NotifyNewCriticalAlerts(snap);
        if (!_webViewReady) return;

        var script = $"window.cornWatch?.onSnapshot({SnapshotExporter.ToJson(snap)})";
        Post(() => _ = _webView?.CoreWebView2?.ExecuteScriptAsync(script));
    }

    // Tray balloon when a critical alert appears that wasn't active on the previous tick.
    // The per-alert cooldown stops a value hovering around its threshold from nagging.
    private void NotifyNewCriticalAlerts(SystemSnapshot snap)
    {
        var critical = snap.Alerts.Where(a => a.Severity == AlertSeverity.Critical).ToList();
        var now = DateTime.UtcNow;
        var fresh = critical.Where(a => !_activeCritical.Contains(a.Id)
            && (!_alertNotifiedUtc.TryGetValue(a.Id, out var last) || now - last > _alertCooldown)).ToList();
        _activeCritical = [.. critical.Select(a => a.Id)];
        if (fresh.Count == 0 || !_settings.NotifyOnCritical) return;

        foreach (var alert in fresh) _alertNotifiedUtc[alert.Id] = now;
        var text = string.Join("\n", fresh.Select(a => a.Message));
        Post(() =>
        {
            if (ContainsFocus) return; // already looking at the dashboard
            ShowBalloon("CornWatch — critical alert", text, ToolTipIcon.Warning, ShowWindow);
        });
    }

    private void Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { /* form handle gone — closing */ }
    }
}
