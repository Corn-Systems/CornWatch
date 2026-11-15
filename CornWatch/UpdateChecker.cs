using System.Net.Http;
using System.Text.Json;

namespace CornWatch;

internal sealed class UpdateInfo
{
    public bool IsNewer { get; init; }
    public string? LatestTag { get; init; }
    public string? ReleaseUrl { get; init; }
}

// Non-blocking check against GitHub Releases.
// Never throws; returns null when the check couldn't complete
// (offline, rate-limited, API shape changed).
internal static class UpdateChecker
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
        DefaultRequestHeaders = { { "User-Agent", AppInfo.UserAgent }, { "Accept", "application/vnd.github+json" } },
    };

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(
                $"https://api.github.com/repos/{AppInfo.RepoOwner}/{AppInfo.RepoName}/releases/latest", ct);
            if (!resp.IsSuccessStatusCode)
            {
                SessionLog.Write($"[UPDATE] GitHub returned {(int)resp.StatusCode}");
                return null;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            string? Get(string prop) =>
                doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var tag = Get("tag_name");
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var newer = IsNewer(tag, AppInfo.Version);
            SessionLog.Write($"[UPDATE] latest={tag} current={AppInfo.Version} newer={newer}");
            return new UpdateInfo { IsNewer = newer, LatestTag = tag, ReleaseUrl = Get("html_url") ?? AppInfo.ReleasesUrl };
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            SessionLog.Write("UPDATE", ex);
            return null;
        }
    }

    internal static bool IsNewer(string remoteTag, string local) =>
        Version.TryParse(Normalize(remoteTag), out var remote)
        && Version.TryParse(Normalize(local), out var current)
        && remote > current;

    private static string Normalize(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "0.0.0";
        v = v.Trim();
        if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];
        var cut = v.IndexOfAny(['-', '+', ' ']);
        if (cut > 0) v = v[..cut];
        return v.Contains('.') ? v : v + ".0";
    }
}
