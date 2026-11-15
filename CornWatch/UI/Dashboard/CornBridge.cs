using System.Runtime.InteropServices;
using CornWatch.Core;

namespace CornWatch.UI.Dashboard;

// JS ↔ C# bridge, exposed to the dashboard as window.chrome.webview.hostObjects.cornBridge.
// Structured data crosses as JSON strings.
[ComVisible(true)]
public class CornBridge(MainForm form, SystemMonitor monitor)
{
    public string GetVersion() => AppInfo.Version;

    public void OpenProcessManager() => MainForm.Launch("taskmgr.exe");

    public void OpenResourceMonitor() => MainForm.Launch("resmon.exe");

    public string GetGpuSensorDump() => monitor.GpuSensorDump;

    public bool GetStartupEnabled() => StartupManager.IsEnabled;

    public void SetStartupEnabled(bool enabled) => StartupManager.SetEnabled(enabled);

    public string GetSettings() => SettingsManager.ToJson();

    /// <summary>Validates and saves settings edited in the dashboard; returns the stored (clamped) values.</summary>
    public string SaveSettings(string json)
    {
        SettingsManager.Update(json);
        form.ApplySettings();
        return SettingsManager.ToJson();
    }

    /// <summary>Last 24 hours of per-minute health scores.</summary>
    public string GetHealthHistory() => HealthHistory.Instance.ToJson();

    /// <summary>Captures the dashboard as a PNG (fire-and-forget; runs on the UI thread).</summary>
    public void ExportPng() => form.BeginInvoke((Action)form.ExportPngFromUi);

    /// <summary>Exports the last snapshot to the Snapshots folder and returns the file path.</summary>
    public string ExportSnapshot()
    {
        if (form.LastSnapshot is not { } snap) return "No snapshot available yet.";
        try
        {
            var path = SnapshotExporter.Export(snap);
            MainForm.OpenInExplorer(path);
            return path;
        }
        catch (Exception ex)
        {
            SessionLog.Write("BRIDGE_EXPORT", ex);
            return "Export failed: " + ex.Message;
        }
    }
}
