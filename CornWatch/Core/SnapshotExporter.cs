using System.Text.Json;
using System.Text.Json.Serialization;
using CornWatch.Models;

namespace CornWatch.Core;

/// <summary>
/// Serialises a SystemSnapshot (compact for the JS bridge, indented for export)
/// and writes exports to the Snapshots folder resolved by AppPaths.
/// </summary>
public static class SnapshotExporter
{
    private static readonly JsonSerializerOptions _compact = CreateOptions(false);
    private static readonly JsonSerializerOptions _indented = CreateOptions(true);

    public static string ToJson(SystemSnapshot snap, bool indent = false) =>
        JsonSerializer.Serialize(snap, indent ? _indented : _compact);

    public static string Export(SystemSnapshot snap)
    {
        var path = AppPaths.SnapshotFile(snap.Timestamp, "json");
        File.WriteAllText(path, ToJson(snap, indent: true));
        SessionLog.Write($"[EXPORT] JSON snapshot written to {path}");
        return path;
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented) => new()
    {
        WriteIndented = writeIndented,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
