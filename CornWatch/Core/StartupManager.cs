using Microsoft.Win32;

namespace CornWatch.Core;

/// <summary>
/// Manages Windows startup registration via the Run registry key.
/// Uses HKCU (current user) so it does NOT require admin rights
/// and appears in Task Manager → Startup tab.
///
/// Always writes the current exe path so a reinstall to a new location
/// automatically heals a stale entry.
/// </summary>
public static class StartupManager
{
    private const string RunKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "CornWatch";

    // The value we write is always the current exe path so reinstalling to a
    // new location heals any stale entry automatically.
    private static string EntryValue =>
        $"\"{AppContext.BaseDirectory}CornWatch.exe\" --minimized";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(AppName) is not null;
            }
            catch (Exception ex) { SessionLog.Write("STARTUP_READ", ex); return false; }
        }
    }

    public static void Enable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                ?? throw new InvalidOperationException("Could not open Run registry key.");
            key.SetValue(AppName, EntryValue);
            SessionLog.Write("[STARTUP] enabled: " + EntryValue);
        }
        catch (Exception ex) { SessionLog.Write("STARTUP_ENABLE", ex); throw; }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
            SessionLog.Write("[STARTUP] disabled");
        }
        catch (Exception ex) { SessionLog.Write("STARTUP_DISABLE", ex); }
    }

    public static void Toggle() { if (IsEnabled) Disable(); else Enable(); }
}
