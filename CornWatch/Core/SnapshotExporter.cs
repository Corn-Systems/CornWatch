using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CornWatch.Models;

namespace CornWatch.Core;

/// <summary>
/// Exports the current SystemSnapshot as formatted JSON.
/// Saved to the user's Documents folder with a timestamp filename.
/// </summary>
public static class SnapshotExporter
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Export(SystemSnapshot snap)
    {
        var dir  = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "CornWatch", "Snapshots");
        Directory.CreateDirectory(dir);

        var filename = $"snapshot_{snap.Timestamp:yyyy-MM-dd_HH-mm-ss}.json";
        var path     = Path.Combine(dir, filename);

        var json = JsonSerializer.Serialize(snap, _opts);
        File.WriteAllText(path, json, Encoding.UTF8);
        return path;
    }
}
