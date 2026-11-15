using Microsoft.Win32;

namespace CornWatch.Core;

/// <summary>
/// Manages Windows startup registration via the HKCU Run registry key —
/// no admin rights needed, and it appears in Task Manager → Startup.
/// The value written is always the current exe path, so reinstalling to a new
/// location heals any stale entry.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private static string EntryValue => $"\"{AppContext.BaseDirectory}{AppInfo.Name}.exe\" --minimized";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(AppInfo.Name) is not null;
            }
            catch (Exception ex)
            {
                SessionLog.Write("STARTUP_READ", ex);
                return false;
            }
        }
    }

    /// <summary>Registers or removes the startup entry; logs and rethrows on failure.</summary>
    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                ?? throw new InvalidOperationException("Could not open Run registry key.");
            if (enabled) key.SetValue(AppInfo.Name, EntryValue);
            else key.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
            SessionLog.Write(enabled ? "[STARTUP] enabled: " + EntryValue : "[STARTUP] disabled");
        }
        catch (Exception ex)
        {
            SessionLog.Write("STARTUP_SET", ex);
            throw;
        }
    }
}
