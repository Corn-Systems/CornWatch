using CornWatch.UI.Dashboard;

// ⚠️ Namespaces and Main stay as-is: the generated ApplicationConfiguration lives in RootNamespace and the CLR requires "Main".
namespace CornWatch;

static class programEntry
{
    // Global\ prefix makes the mutex cross-session so a startup-registered
    // hidden tray instance blocks a second foreground launch.
    private const string mutexName = @"Global\CornSystems.CornWatch.SingleInstance";

    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, mutexName, out var isNew);
        if (!isNew)
        {
            MessageBox.Show("CornWatch is already running.\nCheck the system tray.",
                appInfo.name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize(); // DPI mode comes from ApplicationHighDpiMode in the .csproj
            sessionLog.sessionHeader();

            Application.ThreadException += (_, e) => sessionLog.write("UNHANDLED_THREAD", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex) sessionLog.write("UNHANDLED_DOMAIN", ex);
            };

            // --minimized overrides the stored preference for this session only (used by the startup registry entry).
            var startMinimized = settingsManager.current.startMinimized
                || args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

            _ = healthHistory.instance; // warm up the singleton so the first write is fast
            Application.Run(new mainForm(startMinimized));
        }
        finally
        {
            healthHistory.instance.flush();
            mutex.ReleaseMutex();
        }
    }
}
