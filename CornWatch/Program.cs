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
        using var mutex = new Mutex(true, MutexName, out var isNew);
        if (!isNew)
        {
            MessageBox.Show("CornWatch is already running.\nCheck the system tray.",
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize(); // DPI mode comes from ApplicationHighDpiMode in the .csproj
            SessionLog.SessionHeader();

            Application.ThreadException += (_, e) => SessionLog.Write("UNHANDLED_THREAD", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex) SessionLog.Write("UNHANDLED_DOMAIN", ex);
            };

            // --minimized overrides the stored preference for this session only (used by the startup registry entry).
            var startMinimized = SettingsManager.Current.StartMinimized
                || args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

            _ = HealthHistory.Instance; // warm up the singleton so the first write is fast
            Application.Run(new MainForm(startMinimized));
        }
        finally
        {
            HealthHistory.Instance.Flush();
            mutex.ReleaseMutex();
        }
    }
}
