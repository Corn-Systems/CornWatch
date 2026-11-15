using System.Text.Json;

namespace CornWatch;

// Persists a rolling 24-hour history of the health score, one entry per minute
// (average score, peak alert count). Samples arrive every poll tick; a minute is
// written to disk when it completes, so disk I/O stays negligible at any poll rate.
internal sealed class HealthHistory
{
    public sealed class HistoryEntry
    {
        public DateTime Timestamp { get; set; }
        public int Score { get; set; }
        public int AlertCount { get; set; }
    }

    private static readonly TimeSpan _window = TimeSpan.FromHours(24);

    // On-disk keys are camelCase; case-insensitive read keeps older PascalCase files loading.
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Lazy<HealthHistory> _lazy = new(() =>
    {
        var history = new HealthHistory();
        history.Load();
        return history;
    });

    private readonly List<HistoryEntry> _ring = [];
    private DateTime _bucketMinute;
    private int _bucketScoreSum, _bucketSamples, _bucketMaxAlerts;

    private HealthHistory() { }

    public static HealthHistory Instance => _lazy.Value;

    public void Append(int score, int alertCount)
    {
        var now = DateTime.Now;
        var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind);
        lock (_ring)
        {
            if (_bucketSamples > 0 && minute != _bucketMinute)
            {
                CloseBucket();
                Save();
            }
            _bucketMinute = minute;
            _bucketScoreSum += score;
            _bucketSamples++;
            _bucketMaxAlerts = Math.Max(_bucketMaxAlerts, alertCount);
        }
    }

    // Completed minutes plus the one in progress, oldest first.
    public string ToJson()
    {
        lock (_ring)
            return JsonSerializer.Serialize(_bucketSamples > 0 ? [.. _ring, CurrentBucket()] : _ring, _json);
    }

    // Writes everything including the in-progress minute (call on app exit).
    public void Flush()
    {
        lock (_ring)
        {
            if (_bucketSamples > 0) CloseBucket();
            Save();
        }
    }

    private HistoryEntry CurrentBucket() => new()
    {
        Timestamp = _bucketMinute,
        Score = (int)Math.Round((double)_bucketScoreSum / _bucketSamples),
        AlertCount = _bucketMaxAlerts,
    };

    private void CloseBucket()
    {
        _ring.Add(CurrentBucket());
        _bucketScoreSum = _bucketSamples = _bucketMaxAlerts = 0;
        Trim();
    }

    private void Trim()
    {
        var cutoff = DateTime.Now - _window;
        _ring.RemoveAll(e => e.Timestamp < cutoff);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(AppPaths.HistoryFile)) return;
            var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(AppPaths.HistoryFile), _json);
            if (entries is null) return;
            _ring.AddRange(entries.OrderBy(e => e.Timestamp));
            Trim();
            SessionLog.Write($"[HISTORY] loaded {_ring.Count} entries");
        }
        catch (Exception ex) { SessionLog.Write("HISTORY", ex); }
    }

    private void Save()
    {
        try { AppPaths.WriteAtomic(AppPaths.HistoryFile, JsonSerializer.Serialize(_ring, _json)); }
        catch (Exception ex) { SessionLog.Write("HISTORY", ex); }
    }
}
