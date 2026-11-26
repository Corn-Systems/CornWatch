using CornWatch.UI.Dashboard;

namespace CornWatch;

static class Program
{
    // One instance per signed-in user. Everything CornWatch writes is per-user (HKCU Run key,
    // %AppData%), so Local\ is enough — Global\ also blocked a second Windows user on the same
    // PC and told them to "check the tray" for an icon that was only in someone else's session.
    // A startup-registered hidden tray instance still blocks a second launch in the same session.
    private const string MutexName = @"Local\CornSystems.CornWatch.SingleInstance";

    [STAThread]
    static void Main(string[] args)
    {
        // First, before any window (including the "already running" box): applies PerMonitorV2 DPI
        // (ApplicationHighDpiMode in the .csproj, backing up app.manifest) and visual styles.
        ApplicationConfiguration.Initialize();

        using var mutex = TryCreateMutex(out var isNew);
        if (!isNew)
        {
            MessageBox.Show("CornWatch is already running.\nCheck the system tray.",
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
            var startMinimized = SettingsManager.Current.StartMinimized
                || args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

            _ = HealthHistory.Instance; // warm up the singleton so the first write is fast
            Application.Run(new MainForm(startMinimized));
        }
        finally
        {
            HealthHistory.Instance.Flush();
            mutex?.ReleaseMutex();
        }
    }

    private static Mutex? TryCreateMutex(out bool isNew)
    {
        try { return new Mutex(true, MutexName, out isNew); }
        catch (UnauthorizedAccessException)
        {
            // The mutex exists but was created by an elevated ("Run as administrator") instance,
            // which a normal launch can't open. That instance is still running, so treat it as such
            // instead of crashing before any handler is installed.
            isNew = false;
            return null;
        }
    }
}
