using System.Text.Json;

namespace CornWatch;

// Persists a rolling 24-hour ring of health-score + alert-count snapshots.
// Loaded at startup, appended every poll tick, written once per minute so disk I/O stays negligible.
internal sealed class healthHistory
{
    public sealed class historyEntry
    {
        public DateTime timestamp { get; set; }
        public int score { get; set; }
        public int alertCount { get; set; }
    }

    private const int maxEntries = 1440;   // 24 h at 1 s polling
    private const int flushEvery = 60;

    // ⚠️ On-disk keys are now camelCase; case-insensitive read keeps existing PascalCase files loading.
    private static readonly JsonSerializerOptions json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Lazy<healthHistory> lazy = new(() =>
    {
        var history = new healthHistory();
        history.load();
        return history;
    });

    private readonly List<historyEntry> ring = new(maxEntries + 1);
    private int unflushed;

    private healthHistory() { }

    public static healthHistory instance => lazy.Value;

    /// <summary>Append one poll result. Flushes to disk every flushEvery calls.</summary>
    public void append(int score, int alertCount)
    {
        lock (ring)
        {
            ring.Add(new historyEntry { timestamp = DateTime.Now, score = score, alertCount = alertCount });
            if (ring.Count > maxEntries) ring.RemoveAt(0);
            if (++unflushed < flushEvery) return;
            unflushed = 0;
            save();
        }
    }

    /// <summary>Force an immediate flush (call on app exit).</summary>
    public void flush()
    {
        lock (ring) save();
    }

    private void load()
    {
        try
        {
            if (!File.Exists(appPaths.historyFile)) return;
            var entries = JsonSerializer.Deserialize<List<historyEntry>>(File.ReadAllText(appPaths.historyFile), json);
            if (entries is null) return;
            ring.AddRange(entries.TakeLast(maxEntries));
            sessionLog.write($"[HISTORY] loaded {ring.Count} entries");
        }
        catch (Exception ex) { sessionLog.write("HISTORY", ex); }
    }

    private void save()
    {
        try { appPaths.writeAtomic(appPaths.historyFile, JsonSerializer.Serialize(ring, json)); }
        catch (Exception ex) { sessionLog.write("HISTORY", ex); }
    }
}
