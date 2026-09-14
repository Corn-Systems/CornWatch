using CornWatch.UI.Dashboard;

namespace CornWatch;

static class Program
{
    // Global\ prefix makes the mutex cross-session so a startup-registered
    // hidden tray instance blocks a second foreground launch.
    private const string MutexName = @"Global\CornSystems.CornWatch.SingleInstance";

    [STAThread]
    static void Main(string[] args)
    {
        // ── Single-instance guard ─────────────────────────────────────────────
        using var mutex = new System.Threading.Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "CornWatch is already running.\nCheck the system tray.",
                "CornWatch", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // ── DPI (belt-and-suspenders; manifest also declares PerMonitorV2) ────
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();

        // ── Logging ───────────────────────────────────────────────────────────
        AppPaths.EnsureDataDir();
        SessionLog.SessionHeader();

        // ── Unhandled exception safety net ────────────────────────────────────
        Application.ThreadException += (_, e) =>
            SessionLog.Write("UNHANDLED_THREAD", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                SessionLog.Write("UNHANDLED_DOMAIN", ex);
        };

        // ── Settings ──────────────────────────────────────────────────────────
        var settings = SettingsManager.Current;

        // --minimized on the command line overrides the stored preference for
        // this session only (used by the startup registry entry).
        bool startMinimized = settings.StartMinimized ||
            args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

        // ── History (warm up the singleton so first write is fast) ────────────
        _ = HealthHistory.Instance;

        Application.Run(new MainForm(startMinimized));

        // ── Flush on clean exit ───────────────────────────────────────────────
        HealthHistory.Instance.Flush();
    }
}
