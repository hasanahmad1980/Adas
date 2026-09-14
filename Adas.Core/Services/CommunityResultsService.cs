using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

/// <summary>Counts of "it worked" / "it didn't" reports for one route on one game and GPU family.</summary>
public sealed record CommunityRouteResult(
    [property: JsonPropertyName("route")] string Route,
    [property: JsonPropertyName("gpu")] string Gpu,
    [property: JsonPropertyName("worked")] int Worked,
    [property: JsonPropertyName("failed")] int Failed);

/// <summary>A route's combined score for the setup page.</summary>
public sealed record CommunityRouteSummary(string Route, int Worked, int Failed)
{
    public int Total => Worked + Failed;
    public double SuccessRate => Total == 0 ? 0 : (double)Worked / Total;
}

/// <summary>What a user agrees to share in one report. Holds no paths, account names or hardware IDs.</summary>
public sealed record CommunityReport(
    string Game, string Route, string GraphicsApi, bool Is64Bit, string Gpu, string Driver, string AdasVersion, bool Worked);

/// <summary>
/// "Did it work?" results from other users, without a server: reports are GitHub issues filed through a
/// prefilled issue form (the user reviews and submits it themselves), a GitHub Action counts them into
/// <c>community/results.json</c>, and Adas reads that public file. Nothing is sent automatically.
/// </summary>
public sealed class CommunityResultsService
{
    public const string Repository = "hasanahmad1980/Adas";
    public const string IssueTemplate = "community-result.yml";
    public static readonly string ResultsUrl = $"https://raw.githubusercontent.com/{Repository}/main/community/results.json";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

    private readonly HttpClient _http;
    private Dictionary<string, List<CommunityRouteResult>>? _games;
    private DateTime _fetchedUtc;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CommunityResultsService(HttpClient http) => _http = http;

    /// <summary>Lowercase letters and digits only, so "DOOM Eternal™" and "doom eternal" match.</summary>
    public static string GameKey(string? gameName)
        => new string((gameName ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>"NVIDIA GeForce RTX 4070 Ti SUPER" → "RTX 4070 Ti SUPER"; unknown names → "".</summary>
    public static string GpuFamily(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return "";
        var match = Regex.Match(gpuName, @"\b(RTX|GTX)\s*(A?\d{3,4})(\s+(Ti|SUPER|Laptop GPU|Ti SUPER))*", RegexOptions.IgnoreCase);
        return match.Success ? Regex.Replace(match.Value.Replace("Laptop GPU", "Laptop"), @"\s+", " ").Trim() : "";
    }

    /// <summary>Route key for a report: the profile name, plus the variant for DFC and Bridge substitute.</summary>
    public static string RouteKey(RouteOption route)
        => route.DeepFriedChicken ? route.Profile + "+DeepFriedChicken"
            : route.BridgeSubstitute ? route.Profile + "+BridgeSubstitute"
            : route.Profile.ToString();

    /// <summary>Loads results (cached for 6 hours). Returns false when the file couldn't be read.</summary>
    public async Task<bool> RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!force && _games is not null && DateTime.UtcNow - _fetchedUtc < CacheLifetime) return true;
            using var response = await _http.GetAsync(ResultsUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return _games is not null;
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _games = Parse(json);
            _fetchedUtc = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[CommunityResults] Fetch failed: {ex.Message}");
            return _games is not null;
        }
        finally { _lock.Release(); }
    }

    internal static Dictionary<string, List<CommunityRouteResult>> Parse(string json)
    {
        var result = new Dictionary<string, List<CommunityRouteResult>>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Object) return result;
        foreach (var game in games.EnumerateObject())
        {
            if (game.Value.ValueKind != JsonValueKind.Array) continue;
            var rows = new List<CommunityRouteResult>();
            foreach (var row in game.Value.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                var route = row.TryGetProperty("route", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                if (string.IsNullOrWhiteSpace(route)) continue;
                var gpu = row.TryGetProperty("gpu", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() ?? "" : "";
                int Count(string name) => row.TryGetProperty(name, out var c) && c.TryGetInt32(out var n) ? Math.Max(n, 0) : 0;
                rows.Add(new CommunityRouteResult(route, gpu, Count("worked"), Count("failed")));
            }
            result[GameKey(game.Name)] = rows;
        }
        return result;
    }

    /// <summary>Per-route totals for a game, best first; <paramref name="gpuFamily"/> narrows to one card.</summary>
    public IReadOnlyList<CommunityRouteSummary> Summarize(string gameName, string? gpuFamily = null)
        => _games is null ? Array.Empty<CommunityRouteSummary>() : Summarize(_games, gameName, gpuFamily);

    internal static IReadOnlyList<CommunityRouteSummary> Summarize(
        Dictionary<string, List<CommunityRouteResult>> games, string gameName, string? gpuFamily)
    {
        if (!games.TryGetValue(GameKey(gameName), out var rows)) return Array.Empty<CommunityRouteSummary>();
        return rows
            .Where(r => string.IsNullOrWhiteSpace(gpuFamily) || string.Equals(r.Gpu, gpuFamily, StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.Route, StringComparer.Ordinal)
            .Select(g => new CommunityRouteSummary(g.Key, g.Sum(r => r.Worked), g.Sum(r => r.Failed)))
            .Where(s => s.Total > 0)
            .OrderByDescending(s => s.Worked - s.Failed)
            .ThenByDescending(s => s.Total)
            .ToList();
    }

    /// <summary>The prefilled GitHub issue-form URL for a report. The user reviews and submits it in the browser.</summary>
    public static string BuildReportUrl(CommunityReport report)
    {
        static string Clean(string? value, int max)
        {
            var text = Regex.Replace(value ?? "", @"[\r\n\t]+", " ").Trim();
            return text.Length > max ? text[..max] : text;
        }
        var fields = new List<(string, string)>
        {
            ("template", IssueTemplate),
            ("title", $"[Result] {Clean(report.Game, 80)} — {report.Route} — {(report.Worked ? "worked" : "didn't work")}"),
            ("game", Clean(report.Game, 120)),
            ("route", Clean(report.Route, 60)),
            ("result", report.Worked ? "It worked" : "It didn't work"),
            ("api", Clean(report.GraphicsApi, 30)),
            ("bitness", report.Is64Bit ? "64-bit" : "32-bit"),
            ("gpu", Clean(GpuFamily(report.Gpu), 40)),
            ("driver", Clean(report.Driver, 20)),
            ("adas", Clean(report.AdasVersion, 20)),
        };
        return $"https://github.com/{Repository}/issues/new?"
               + string.Join("&", fields.Select(f => $"{f.Item1}={Uri.EscapeDataString(f.Item2)}"));
    }

    /// <summary>Plain-text list of exactly what the report contains, for the confirmation prompt.</summary>
    public static string DescribeReport(CommunityReport report)
        => $"Game: {report.Game}\nRoute: {report.Route}\nResult: {(report.Worked ? "It worked" : "It didn't work")}\n"
           + $"Graphics API: {report.GraphicsApi} ({(report.Is64Bit ? "64-bit" : "32-bit")})\n"
           + $"GPU: {(GpuFamily(report.Gpu) is { Length: > 0 } gpu ? gpu : "not included")}\n"
           + $"Driver: {(string.IsNullOrWhiteSpace(report.Driver) ? "not included" : report.Driver)}\nAdas: {report.AdasVersion}";
}
