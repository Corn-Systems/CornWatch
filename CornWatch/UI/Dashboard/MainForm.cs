using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using CornWatch.Core;
using CornWatch.Models;

namespace CornWatch.UI.Dashboard;

public class mainForm : Form
{
    private readonly systemMonitor monitor;
    private readonly appSettings settings = settingsManager.current;
    private readonly NotifyIcon trayIcon;
    private readonly CancellationTokenSource cts = new();
    private WebView2? webView;
    private volatile bool webViewReady;
    private bool balloonShown;

    internal systemSnapshot? lastSnapshot { get; private set; }

    public mainForm(bool startMinimized = false)
    {
        Text = "🌽 CornWatch — System Health Dashboard";
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(10, 10, 10);
        Size = new Size(settings.windowWidth, settings.windowHeight);
        if (settings.windowState == "Maximized") WindowState = FormWindowState.Maximized;

        var icoPath = Path.Combine(AppContext.BaseDirectory, "assets", "cornwatch.ico");
        if (File.Exists(icoPath))
            try { Icon = new Icon(icoPath); }
            catch (Exception ex) { sessionLog.write("MAINFORM_ICON", ex); }

        trayIcon = createTrayIcon();

        monitor = new systemMonitor(settings.pollIntervalMs);
        monitor.snapshotReady += onSnapshotReady;

        if (startMinimized)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            Visible = false;
        }

        _ = initWebViewAsync();
        if (settings.checkForUpdates) _ = checkForUpdateAsync();
    }

    internal static void launch(string file, string args = "")
    {
        try { using var proc = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }); }
        catch (Exception ex) { sessionLog.write($"[SHELL] {file}", ex); }
    }

    internal static void openInExplorer(string path) => launch("explorer.exe", $"/select,\"{path}\"");

    internal async void exportPngFromUi()
    {
        try
        {
            if (webView?.CoreWebView2 is not { } core) return;
            var path = appPaths.snapshotFile(DateTime.Now, "png");
            await using (var fs = File.Create(path))
                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);

            sessionLog.write("[EXPORT] PNG written to " + path);
            openInExplorer(path);
        }
        catch (Exception ex)
        {
            sessionLog.write("EXPORT_PNG", ex);
            MessageBox.Show("PNG export failed: " + ex.Message, appInfo.name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState != FormWindowState.Minimized) return;

        hideToTray();
        if (balloonShown) return;
        balloonShown = true;
        trayIcon.ShowBalloonTip(2000, appInfo.name, "Minimised to tray — double-click to restore", ToolTipIcon.Info);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // X button → minimise to tray; tray Exit actually quits.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            hideToTray();
            return;
        }

        if (WindowState == FormWindowState.Normal)
        {
            settings.windowWidth = Width;
            settings.windowHeight = Height;
        }
        settings.windowState = WindowState == FormWindowState.Maximized ? "Maximized" : "Normal";
        settingsManager.save(settings);

        cts.Cancel();
        cts.Dispose();
        trayIcon.Dispose();
        monitor.Dispose();
        base.OnFormClosing(e);
    }

    private NotifyIcon createTrayIcon()
    {
        var startupItem = new ToolStripMenuItem("Launch at startup") { Checked = startupManager.isEnabled, CheckOnClick = true };
        startupItem.Click += (_, _) =>
        {
            try { startupManager.setEnabled(startupItem.Checked); }
            catch (Exception ex)
            {
                startupItem.Checked = startupManager.isEnabled;
                MessageBox.Show("Could not update startup: " + ex.Message, appInfo.name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open CornWatch", null, (_, _) => showWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            trayIcon.Visible = false;
            Application.Exit();
        });

        var icon = new NotifyIcon
        {
            Text = appInfo.name,
            Icon = Icon ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
        };
        icon.DoubleClick += (_, _) => showWindow();
        return icon;
    }

    private void showWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        monitor.setPollInterval(settings.pollIntervalMs);
    }

    private void hideToTray()
    {
        ShowInTaskbar = false;
        Hide();
        monitor.setPollInterval(5000);
    }

    private async Task checkForUpdateAsync()
    {
        var ct = cts.Token;
        try
        {
            await Task.Delay(3000, ct); // let the dashboard render first
            var info = await updateChecker.checkAsync(ct);
            if (info?.isNewer != true) return;

            // Non-blocking tray balloon; clicking it opens the releases page.
            trayIcon.BalloonTipClicked += (_, _) => launch(info.releaseUrl ?? appInfo.releasesUrl);
            trayIcon.ShowBalloonTip(6000, "CornWatch update available",
                $"Version {info.latestTag ?? "?"} is available — click to download.", ToolTipIcon.Info);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { sessionLog.write("UPDATE_CHECK", ex); }
    }

    private async Task initWebViewAsync()
    {
        try
        {
            var view = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(view);

            try { await view.EnsureCoreWebView2Async(); }
            catch (Exception ex)
            {
                sessionLog.write("WEBVIEW2_INIT", ex);
                Controls.Remove(view);
                view.Dispose();
                Controls.Add(new Label
                {
                    Text = "⚠  WebView2 Runtime is not installed.\n\n" +
                           "Download it from:\nhttps://developer.microsoft.com/en-us/microsoft-edge/webview2/\n\n" +
                           "Restart CornWatch after installing.",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.FromArgb(242, 176, 74),
                    BackColor = Color.FromArgb(10, 10, 10),
                    Font = new Font("Courier New", 10f),
                });
                return;
            }

            webView = view;
            var core = view.CoreWebView2;
            core.AddHostObjectToScript("cornBridge", new cornBridge(this, monitor));
#if !DEBUG
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
#endif
            core.NavigationCompleted += (_, _) => webViewReady = true;

            var htmlPath = Path.Combine(AppContext.BaseDirectory, "UI", "Dashboard", "Dashboard.html");
            core.Navigate(File.Exists(htmlPath) ? new Uri(htmlPath).AbsoluteUri : "about:blank");
        }
        catch (Exception ex) { sessionLog.write("WEBVIEW2_SETUP", ex); }
    }

    // Runs on the poll thread; posts to the UI thread without blocking it.
    private void onSnapshotReady(systemSnapshot snap)
    {
        lastSnapshot = snap;
        if (!webViewReady || IsDisposed) return;

        var script = $"window.cornWatch?.onSnapshot({snapshotExporter.toJson(snap)})";
        try { BeginInvoke(() => { _ = webView?.CoreWebView2?.ExecuteScriptAsync(script); }); }
        catch (InvalidOperationException) { /* form handle gone — closing */ }
    }
}

// JS ↔ C# bridge, exposed to the dashboard as window.chrome.webview.hostObjects.cornBridge.
[System.Runtime.InteropServices.ComVisible(true)]
public class cornBridge(mainForm form, systemMonitor monitor)
{
    public void openProcessManager() => mainForm.launch("taskmgr.exe");

    public void openResourceMonitor() => mainForm.launch("resmon.exe");

    public string getGpuSensorDump() => monitor.gpuSensorDump;

    public bool getStartupEnabled() => startupManager.isEnabled;

    public void setStartupEnabled(bool enabled) => startupManager.setEnabled(enabled);

    /// <summary>Captures the dashboard as a PNG (fire-and-forget; runs on the UI thread).</summary>
    public void exportPng() => form.BeginInvoke((Action)form.exportPngFromUi);

    /// <summary>Exports the last snapshot to the Snapshots folder and returns the file path.</summary>
    public string exportSnapshot()
    {
        if (form.lastSnapshot is not { } snap) return "No snapshot available yet.";
        try
        {
            var path = snapshotExporter.export(snap);
            mainForm.openInExplorer(path);
            return path;
        }
        catch (Exception ex)
        {
            sessionLog.write("BRIDGE_EXPORT", ex);
            return "Export failed: " + ex.Message;
        }
    }
}
