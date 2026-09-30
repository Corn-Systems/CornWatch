using Microsoft.Win32;

namespace CornWatch.Core;

/// <summary>
/// Manages Windows startup registration via the HKCU Run registry key —
/// no admin rights needed, and it appears in Task Manager → Startup.
/// The value written is always the current exe path, so reinstalling to a new
/// location heals any stale entry.
/// </summary>
public static class startupManager
{
    private const string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private static string entryValue => $"\"{AppContext.BaseDirectory}{appInfo.name}.exe\" --minimized";

    public static bool isEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(runKey, false);
                return key?.GetValue(appInfo.name) is not null;
            }
            catch (Exception ex)
            {
                sessionLog.write("STARTUP_READ", ex);
                return false;
            }
        }
    }

    /// <summary>Registers or removes the startup entry; logs and rethrows on failure.</summary>
    public static void setEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKey, true)
                ?? throw new InvalidOperationException("Could not open Run registry key.");
            if (enabled) key.SetValue(appInfo.name, entryValue);
            else key.DeleteValue(appInfo.name, throwOnMissingValue: false);
            sessionLog.write(enabled ? "[STARTUP] enabled: " + entryValue : "[STARTUP] disabled");
        }
        catch (Exception ex)
        {
            sessionLog.write("STARTUP_SET", ex);
            throw;
        }
    }
}
