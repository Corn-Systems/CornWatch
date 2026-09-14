using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CornWatch.Models;

namespace CornWatch.Core;

/// <summary>
/// Exports the current SystemSnapshot as formatted JSON to the Snapshots folder.
/// Path is centralised in AppPaths — nothing else calls GetFolderPath for this.
/// </summary>
public static class SnapshotExporter
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented            = true,
        PropertyNamingPolicy     = JsonNamingPolicy.CamelCase,
        Converters               = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Export(SystemSnapshot snap)
    {
        AppPaths.EnsureSnapshotsDir();

        var filename = $"snapshot_{snap.Timestamp:yyyy-MM-dd_HH-mm-ss}.json";
        var path     = Path.Combine(AppPaths.SnapshotsDir, filename);

        var json = JsonSerializer.Serialize(snap, _opts);
        File.WriteAllText(path, json, Encoding.UTF8);
        SessionLog.Write($"[EXPORT] JSON snapshot written to {path}");
        return path;
    }
}
