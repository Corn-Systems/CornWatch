using CornWatch.UI.Dashboard;

namespace CornWatch;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // First, before any window (including the "already running" box): applies PerMonitorV2 DPI
        // (ApplicationHighDpiMode in the .csproj, backing up app.manifest) and visual styles.
        ApplicationConfiguration.Initialize();

        var minimizedArg = args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            // Already running: a normal launch brings that window forward; the startup entry
            // (--minimized) has nothing to do. Only explain when the running copy can't be reached.
            if (!minimizedArg && !SingleInstance.SignalExisting())
                MessageBox.Show("CornWatch is already running (as administrator).\nUse its tray icon to open it.",
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            SessionLog.SessionHeader();

            Application.ThreadException += (_, e) => SessionLog.Write("UNHANDLED_THREAD", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex) SessionLog.Write("UNHANDLED_DOMAIN", ex);
            };

            // --minimized overrides the stored preference for this session only (used by the startup registry entry).
            var startMinimized = SettingsManager.Current.StartMinimized || minimizedArg;

            _ = HealthHistory.Instance; // warm up the singleton so the first write is fast
            var form = new MainForm(startMinimized);
            instance.ListenForShowRequests(form.ShowFromAnotherLaunch);
            Application.Run(form);
        }
        finally
        {
            HealthHistory.Instance.Flush();
        }
    }
}
