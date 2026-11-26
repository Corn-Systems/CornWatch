using System.Diagnostics;

namespace CornWatch.Core;

// Actions offered on a row of the Top Processes table. Each returns a short message for
// the dashboard (empty string = success) and never throws.
internal static class ProcessActions
{
    // Ending these logs the user out, blue-screens the PC, or breaks Windows until reboot.
    private static readonly HashSet<string> _protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Secure System", "Memory Compression", "MemCompression",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "svchost",
        "fontdrvhost", "dwm", "Ntoskrnl", "audiodg", "sihost", "spoolsv",
    };

    public static bool IsProtected(string name) =>
        _protected.Contains(name) || name.Equals(AppInfo.Name, StringComparison.OrdinalIgnoreCase);

    public static string Kill(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (IsProtected(p.ProcessName)) return $"{p.ProcessName} is a Windows system process and can't be ended from CornWatch.";
            p.Kill(entireProcessTree: false);
            SessionLog.Write($"[PROCESS] ended {p.ProcessName} (PID {pid})");
            return string.Empty;
        }
        catch (ArgumentException) { return "That process has already exited."; }
        catch (Exception ex) { return Describe(ex, "end the process"); }
    }

    // Ends every process with this name (a grouped row such as "chrome (20)").
    public static string KillAll(string name)
    {
        if (IsProtected(name)) return $"{name} is a Windows system process and can't be ended from CornWatch.";
        int ended = 0, failed = 0;
        string? firstError = null;
        foreach (var p in Process.GetProcessesByName(name))
            using (p)
                try
                {
                    p.Kill(entireProcessTree: false);
                    ended++;
                }
                catch (InvalidOperationException) { /* exited on its own meanwhile */ }
                catch (Exception ex)
                {
                    failed++;
                    firstError ??= Describe(ex, "end it");
                }

        SessionLog.Write($"[PROCESS] ended {ended} × {name}, {failed} failed");
        if (failed == 0) return ended == 0 ? "No matching processes are running." : string.Empty;
        return $"Ended {ended}, but {failed} could not be ended: {firstError}";
    }

    public static string OpenLocation(int pid)
    {
        var path = NativeMethods.ProcessImagePath(pid);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return "Windows won't reveal where this process lives (it may belong to the system or another user).";
        UI.Dashboard.MainForm.OpenInExplorer(path);
        return string.Empty;
    }

    public static void SearchOnline(string name) =>
        UI.Dashboard.MainForm.Launch("https://duckduckgo.com/?q=" + Uri.EscapeDataString($"\"{name}.exe\" process"));

    private static string Describe(Exception ex, string action) => ex switch
    {
        System.ComponentModel.Win32Exception { NativeErrorCode: 5 } =>
            $"Access denied — Windows won't let CornWatch {action}. Running CornWatch as administrator may help.",
        _ => $"Couldn't {action}: {ex.Message}",
    };
}
