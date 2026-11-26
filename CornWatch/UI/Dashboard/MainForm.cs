using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using CornSystems;
using CornWatch.Core;
using CornWatch.Models;
using CornWatch.UI.Overlay;
using CornWatch.UI.Tray;

namespace CornWatch.UI.Dashboard;

public class MainForm : Form
{
    private const int TrayPollIntervalMs = 5000;
    private const int BalloonMaxChars = 250;             // Shell limit is 255 including the terminator
    private const float FallbackFontPx = 40f / 3f;       // 10 pt at 96 DPI, scaled by hand (AutoScaleMode.None)
    private static readonly TimeSpan _alertCooldown = TimeSpan.FromMinutes(5);
    // 96-DPI ("logical") pixels; scaled with Dpi.S() for the monitor the window is on.
    private static readonly Size _minLogicalSize = new(960, 640);

    private readonly AppSettings _settings = SettingsManager.Current;
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _trayBaseIcon;
    private readonly ToolStripMenuItem _overlayItem = new("Show overlay") { ShortcutKeyDisplayString = OverlayForm.HotkeyText };
    private readonly HotkeyWindow _overlayHotkey;
    private readonly CancellationTokenSource _cts = new();

    // Built on a background thread (see StartMonitorAsync); null for the first moment after launch.
    private SystemMonitor? _monitor;
    private OverlayForm? _overlay;
    private Icon? _trayStatusIcon;                       // owned; null while the plain icon is shown
    private TrayStatusIcon.Level _trayLevel = TrayStatusIcon.Level.Ok;
    private bool _closing;

    // Critical-alert notification state; only touched on the poll thread.
    private readonly Dictionary<string, DateTime> _alertNotifiedUtc = [];
    private HashSet<string> _activeCritical = [];

    private WebView2? _webView;
    private Label? _fallbackLabel;
    private Action? _balloonAction;
    private FormWindowState _restoreState = FormWindowState.Normal;
    private volatile bool _webViewReady, _inTray; // read on the poll thread
    private bool _startHidden, _trayHintShown, _webViewStarted;

    internal SystemSnapshot? LastSnapshot { get; private set; }
    internal SystemMonitor? Monitor => _monitor;
    private bool OverlayVisible => _overlay is { Visible: true };

    public MainForm(bool startMinimized = false)
    {
        Dpi.Update(this);
        AutoScaleMode = AutoScaleMode.None; // every pixel value goes through Dpi.S(); never let WinForms double-scale
        Text = "🌽 CornWatch — System Health Dashboard";
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ApplyWindowSize();
        if (_settings.WindowState == "Maximized") WindowState = _restoreState = FormWindowState.Maximized;
        DpiChanged += OnFormDpiChanged;

        // Load each icon at the size it is drawn at, so the multi-size .ico stays sharp at any scaling.
        Icon? trayImage = null;
        try
        {
            Icon = AppResources.LoadIcon(SystemInformation.IconSize);
            trayImage = AppResources.LoadIcon(SystemInformation.SmallIconSize);
        }
        catch (Exception ex) { SessionLog.Write("MAINFORM_ICON", ex); }

        _trayBaseIcon = trayImage ?? Icon ?? SystemIcons.Application;
        _trayIcon = CreateTrayIcon(_trayBaseIcon);
        _startHidden = _inTray = startMinimized;

        _overlayHotkey = new HotkeyWindow(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Keys.O);
        _overlayHotkey.Pressed += () => SetOverlayVisible(!OverlayVisible);
        if (!_overlayHotkey.IsRegistered) _overlayItem.ShortcutKeyDisplayString = null;

        _ = StartMonitorAsync(PollInterval);
        if (_settings.CheckForUpdates) _ = CheckForUpdateAsync();
    }

    // Polls slowly from the tray, unless the overlay is up and needs live numbers.
    private int PollInterval => _inTray && !OverlayVisible
        ? Math.Max(TrayPollIntervalMs, _settings.PollIntervalMs)
        : _settings.PollIntervalMs;

    // LibreHardwareMonitor, WMI and the performance counters take a second or two to open.
    // Doing that off the UI thread lets the window and the dashboard appear straight away.
    private async Task StartMonitorAsync(int intervalMs)
    {
        try
        {
            var monitor = await Task.Run(() => new SystemMonitor(intervalMs));
            if (_closing)
            {
                monitor.Dispose();
                return;
            }
            _monitor = monitor;
            monitor.SnapshotReady += OnSnapshotReady;
            monitor.SetPollInterval(PollInterval); // the window may have moved to or from the tray meanwhile
        }
        catch (Exception ex) { SessionLog.Write("MONITOR_START", ex); }
    }

    // Another launch of CornWatch asked for the window (called on a thread-pool thread).
    internal void ShowFromAnotherLaunch() => Post(ShowWindow);

    internal static void Launch(string file, string args = "")
    {
        try { using var proc = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }); }
        catch (Exception ex) { SessionLog.Write($"[SHELL] {file}", ex); }
    }

    internal static void OpenInExplorer(string path) => Launch("explorer.exe", $"/select,\"{path}\"");

    // Called by the bridge after the dashboard saved new settings.
    internal void ApplySettings()
    {
        if (_settings.OverlayEnabled != OverlayVisible) SetOverlayVisible(_settings.OverlayEnabled);
        else _overlay?.ApplySettings(_settings);
        _monitor?.SetPollInterval(PollInterval);
    }

    // Overlay on/off from the hotkey, tray menu or dashboard; remembered for next launch.
    internal void SetOverlayVisible(bool visible)
    {
        if (_closing) return;
        if (visible)
        {
            _overlay ??= new OverlayForm();
            _overlay.ApplySettings(_settings);
            if (LastSnapshot is { } snap) _overlay.UpdateSnapshot(snap);
            _overlay.Show();
        }
        else _overlay?.Hide();

        _overlayItem.Checked = visible;
        if (_settings.OverlayEnabled != visible)
        {
            _settings.OverlayEnabled = visible;
            SettingsManager.Save();
        }
        _monitor?.SetPollInterval(PollInterval);
    }

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

        // The constructor could only see the system DPI. If the window was created on a monitor
        // with a different scale, size it for that monitor before it is first shown.
        if (DeviceDpi != (int)Dpi.Current)
        {
            Dpi.Update(DeviceDpi);
            ApplyWindowSize();
            if (WindowState == FormWindowState.Normal) CenterToScreen();
        }
        _ = InitWebViewAsync();
        if (_settings.OverlayEnabled) BeginInvoke(() => SetOverlayVisible(true));
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

        // Stored in 96-DPI pixels so the window comes back the same apparent size on any monitor.
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (bounds.Width >= MinimumSize.Width && bounds.Height >= MinimumSize.Height)
        {
            _settings.WindowWidth = (int)Math.Round(bounds.Width / Dpi.Scale);
            _settings.WindowHeight = (int)Math.Round(bounds.Height / Dpi.Scale);
        }
        _settings.WindowState = _restoreState == FormWindowState.Maximized ? "Maximized" : "Normal";
        SettingsManager.Save();

        _closing = true;
        _cts.Cancel();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayStatusIcon?.Dispose();
        _overlayHotkey.Dispose();
        _overlay?.Dispose();
        _monitor?.Dispose();
        base.OnFormClosing(e);
    }

    // Settings size is in 96-DPI pixels. Clamped to the screen so a size saved on a larger
    // monitor (or by 0.3.0 and earlier, which stored physical pixels) can't open off-screen.
    private void ApplyWindowSize()
    {
        var min = Dpi.S(_minLogicalSize);
        var wanted = Dpi.S(new Size(_settings.WindowWidth, _settings.WindowHeight));
        var area = (IsHandleCreated ? Screen.FromHandle(Handle) : Screen.FromPoint(Cursor.Position)).WorkingArea;
        MinimumSize = min;
        Size = new Size(
            Math.Clamp(wanted.Width, min.Width, Math.Max(min.Width, area.Width)),
            Math.Clamp(wanted.Height, min.Height, Math.Max(min.Height, area.Height)));
    }

    // Window dragged to a monitor with a different scale (or the scale changed in Settings).
    private void OnFormDpiChanged(object? sender, DpiChangedEventArgs e)
    {
        Dpi.Update(e.DeviceDpiNew);
        // Drop the old minimum first: sized for the old DPI, it would stop the window
        // shrinking to the suggested bounds when moving to a lower-scale monitor.
        MinimumSize = Size.Empty;
        if (WindowState == FormWindowState.Normal && e.SuggestedRectangle != Rectangle.Empty)
            SetBounds(e.SuggestedRectangle.X, e.SuggestedRectangle.Y, e.SuggestedRectangle.Width, e.SuggestedRectangle.Height);
        MinimumSize = Dpi.S(_minLogicalSize);
        if (_fallbackLabel is not null) SetFallbackFont(_fallbackLabel);
        // The WebView2 control follows the monitor scale on its own (RasterizationScale).
    }

    private static void SetFallbackFont(Label label)
    {
        var old = label.Font;
        label.Font = new Font("Courier New", Dpi.S(FallbackFontPx), FontStyle.Regular, GraphicsUnit.Pixel);
        old.Dispose();
    }

    private NotifyIcon CreateTrayIcon(Icon image)
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

        _overlayItem.Click += (_, _) => SetOverlayVisible(!OverlayVisible);

        var menu = new ContextMenuStrip
        {
            Renderer = new TrayMenuRenderer(),
            Font = new Font("Courier New", 9f),
            ShowImageMargin = true,
        };
        menu.Items.Add(new ToolStripMenuItem($"{AppInfo.Name} v{AppInfo.Version}") { Enabled = false, Tag = "header" });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open dashboard", null, (_, _) => ShowWindow());
        menu.Items.Add(_overlayItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        // The dashboard has its own startup toggle, so re-read the real state each time the menu opens.
        menu.Opening += (_, _) =>
        {
            startupItem.Checked = StartupManager.IsEnabled;
            _overlayItem.Checked = OverlayVisible;
        };

        var icon = new NotifyIcon
        {
            Text = AppInfo.Name,
            Icon = image,
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
        // Several critical alerts at once can exceed what the shell accepts.
        if (text.Length > BalloonMaxChars) text = text[..(BalloonMaxChars - 1)] + "…";
        _trayIcon.ShowBalloonTip(6000, title, text, icon);
    }

    private void ShowWindow()
    {
        _inTray = false;
        ShowInTaskbar = true;
        Show();
        WindowState = _restoreState;
        Activate();
        _monitor?.SetPollInterval(PollInterval);
    }

    private void HideToTray()
    {
        _inTray = true;
        ShowInTaskbar = false;
        Hide();
        _monitor?.SetPollInterval(PollInterval);
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

            var htmlPath = AppResources.ExtractDashboard();

            var core = view.CoreWebView2;
            core.AddHostObjectToScript("cornBridge", new CornBridge(this));
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
            _fallbackLabel = new Label
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
            };
            SetFallbackFont(_fallbackLabel);
            Controls.Add(_fallbackLabel);
        }
    }

    // Runs on the poll thread; posts to the UI thread without blocking it.
    private void OnSnapshotReady(SystemSnapshot snap)
    {
        LastSnapshot = snap;
        NotifyNewCriticalAlerts(snap);
        Post(() =>
        {
            UpdateTrayStatus(snap);
            if (OverlayVisible) _overlay!.UpdateSnapshot(snap);
        });
        // Nobody can see the dashboard from the tray, so don't make it re-render every tick.
        // ShowWindow() resets the poll timer, which pushes a fresh snapshot straight away.
        if (!_webViewReady || _inTray) return;

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

    // Tooltip shows the score; the icon gains an amber/red dot while warnings/critical alerts are active.
    private void UpdateTrayStatus(SystemSnapshot snap)
    {
        if (_closing) return;
        var tip = TrayStatusIcon.Tooltip(snap);
        if (_trayIcon.Text != tip) _trayIcon.Text = tip;

        var level = TrayStatusIcon.LevelOf(snap);
        if (level == _trayLevel) return;
        _trayLevel = level;
        var old = _trayStatusIcon;
        try
        {
            _trayStatusIcon = level == TrayStatusIcon.Level.Ok ? null : TrayStatusIcon.Render(_trayBaseIcon, level);
            _trayIcon.Icon = _trayStatusIcon ?? _trayBaseIcon;
        }
        catch (Exception ex) { SessionLog.Write("TRAY_STATUS_ICON", ex); }
        old?.Dispose();
    }

    private void Post(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { /* form handle gone — closing */ }
    }
}
