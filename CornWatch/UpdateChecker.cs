using System.Net.Http;
using System.Text.Json;

namespace CornWatch;

internal sealed class updateInfo
{
    public bool isNewer { get; init; }
    public string? latestTag { get; init; }
    public string? releaseUrl { get; init; }
}

// Non-blocking check against GitHub Releases.
// Never throws; returns null when the check couldn't complete
// (offline, rate-limited, API shape changed).
internal static class updateChecker
{
    private static readonly HttpClient http = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
        DefaultRequestHeaders = { { "User-Agent", appInfo.userAgent }, { "Accept", "application/vnd.github+json" } },
    };

    public static async Task<updateInfo?> checkAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await http.GetAsync(
                $"https://api.github.com/repos/{appInfo.repoOwner}/{appInfo.repoName}/releases/latest", ct);
            if (!resp.IsSuccessStatusCode)
            {
                sessionLog.write($"[UPDATE] GitHub returned {(int)resp.StatusCode}");
                return null;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            string? get(string prop) =>
                doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var tag = get("tag_name");
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var newer = isNewer(tag, appInfo.version);
            sessionLog.write($"[UPDATE] latest={tag} current={appInfo.version} newer={newer}");
            return new updateInfo { isNewer = newer, latestTag = tag, releaseUrl = get("html_url") ?? appInfo.releasesUrl };
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            sessionLog.write("UPDATE", ex);
            return null;
        }
    }

    internal static bool isNewer(string remoteTag, string local) =>
        Version.TryParse(normalize(remoteTag), out var remote)
        && Version.TryParse(normalize(local), out var current)
        && remote > current;

    private static string normalize(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "0.0.0";
        v = v.Trim();
        if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];
        var cut = v.IndexOfAny(['-', '+', ' ']);
        if (cut > 0) v = v[..cut];
        return v.Contains('.') ? v : v + ".0";
    }
}
