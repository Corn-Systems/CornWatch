using System.Text.Json;
using System.Text.Json.Serialization;
using CornWatch.Models;

namespace CornWatch.Core;

/// <summary>
/// Serialises a systemSnapshot (compact for the JS bridge, indented for export)
/// and writes exports to the Snapshots folder resolved by appPaths.
/// </summary>
public static class snapshotExporter
{
    private static readonly JsonSerializerOptions compact = createOptions(false);
    private static readonly JsonSerializerOptions indented = createOptions(true);

    public static string toJson(systemSnapshot snap, bool indent = false) =>
        JsonSerializer.Serialize(snap, indent ? indented : compact);

    public static string export(systemSnapshot snap)
    {
        var path = appPaths.snapshotFile(snap.timestamp, "json");
        File.WriteAllText(path, toJson(snap, indent: true));
        sessionLog.write($"[EXPORT] JSON snapshot written to {path}");
        return path;
    }

    private static JsonSerializerOptions createOptions(bool writeIndented) => new()
    {
        WriteIndented = writeIndented,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
