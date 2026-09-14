using System.Text.Json;

namespace CornWatch;

// Persists a rolling 24-hour ring of health-score + alert-count snapshots.
// Loaded at startup, appended every poll tick, written periodically (every 60
// ticks by default) so disk I/O stays negligible.
internal sealed class HealthHistory
{
    // ── Serialisable entry ────────────────────────────────────────────────────
    public sealed class Entry
    {
        public DateTime Timestamp  { get; set; }
        public int      Score      { get; set; }
        public int      AlertCount { get; set; }
    }

    // ── Config ────────────────────────────────────────────────────────────────
    private const int MaxEntries    = 1440;   // 24 h at 1 s polling
    private const int FlushEvery    = 60;     // flush to disk once per minute

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly List<Entry>         _ring    = new(MaxEntries + 1);
    private readonly JsonSerializerOptions _json  = new() { WriteIndented = false };
    private          int                 _unflushed;
    private static   HealthHistory?      _instance;
    private static readonly object       _initLock = new();

    private HealthHistory() { }

    public static HealthHistory Instance
    {
        get
        {
            if (_instance is not null) return _instance;
            lock (_initLock)
            {
                if (_instance is not null) return _instance;
                _instance = new HealthHistory();
                _instance.Load();
                return _instance;
            }
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Append one poll result. Flushes to disk every FlushEvery calls.</summary>
    public void Append(int score, int alertCount)
    {
        lock (_ring)
        {
            _ring.Add(new Entry { Timestamp = DateTime.Now, Score = score, AlertCount = alertCount });
            if (_ring.Count > MaxEntries)
                _ring.RemoveAt(0);

            if (++_unflushed >= FlushEvery)
            {
                _unflushed = 0;
                Save();
            }
        }
    }

    /// <summary>Returns a copy of the current history (newest last).</summary>
    public List<Entry> Snapshot()
    {
        lock (_ring) return [.. _ring];
    }

    /// <summary>Force an immediate flush (call on app exit).</summary>
    public void Flush()
    {
        lock (_ring) Save();
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (!File.Exists(AppPaths.HistoryFile)) return;
            var entries = JsonSerializer.Deserialize<List<Entry>>(
                File.ReadAllText(AppPaths.HistoryFile), _json);
            if (entries is null) return;

            // Keep only last MaxEntries entries to bound memory after a long run.
            int skip = Math.Max(0, entries.Count - MaxEntries);
            _ring.AddRange(entries.Skip(skip));
            SessionLog.Write($"[HISTORY] loaded {_ring.Count} entries");
        }
        catch (Exception ex)
        {
            SessionLog.Write("HISTORY", ex);
        }
    }

    private void Save()
    {
        try
        {
            AppPaths.EnsureDataDir();
            string tmp = AppPaths.HistoryFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_ring, _json));
            File.Move(tmp, AppPaths.HistoryFile, overwrite: true);
        }
        catch (Exception ex)
        {
            SessionLog.Write("HISTORY", ex);
        }
    }
}
