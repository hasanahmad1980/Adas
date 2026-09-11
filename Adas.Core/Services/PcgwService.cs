using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Resolves PCGamingWiki URLs via Steam AppID (using appid.php redirect)
/// or OpenSearch fallback. Maintains a persistent AppID cache on disk.
/// </summary>
public class PcgwService : IPcgwService
{
    private readonly HttpClient _http;
    private readonly ISteamAppIdResolver _steamAppIdResolver;
    private readonly IGameDetectionService _gameDetection;

    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "steam_appid_cache.json");

    private static readonly string UrlCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "pcgw_url_cache.json");

    /// <summary>Marker file — if absent on first 2.4.2 launch, wipes the stale URL cache built with broken appid.php URLs.</summary>
    private static readonly string UrlCacheMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "pcgw_cache_v2.txt");

    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    /// <summary>Normalized game name → Steam AppID.</summary>
    private Dictionary<string, int> _appIdCache = new(StringComparer.Ordinal);

    /// <summary>Normalized game name → resolved PCGW wiki URL.</summary>
    private System.Collections.Concurrent.ConcurrentDictionary<string, string> _urlCache = new(StringComparer.Ordinal);

    /// <summary>Debounce timer — resets on every <see cref="SaveCacheAsync"/> call.</summary>
    private Timer? _saveDebounceTimer;

    /// <summary>Guards <see cref="_saveDebounceTimer"/> creation/reset.</summary>
    private readonly object _saveLock = new();

    /// <summary>
    /// Circuit breaker: once PCGW returns an error or times out, skip all further
    /// lookups for the rest of the session to avoid blocking card builds.
    /// </summary>
    private volatile bool _pcgwDown;

    /// <summary>
    /// Shared cancellation source — cancelled when the circuit breaker trips so
    /// all in-flight PCGW requests abort immediately instead of each waiting
    /// their own 5-second timeout.
    /// </summary>
    private readonly CancellationTokenSource _pcgwCts = new();

    public PcgwService(HttpClient http, ISteamAppIdResolver steamAppIdResolver, IGameDetectionService gameDetection)
    {
        _http = http;
        _steamAppIdResolver = steamAppIdResolver;
        _gameDetection = gameDetection;
    }

    /// <inheritdoc />
    public async Task LoadCacheAsync()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                CrashReporter.Log("[PcgwService.LoadCacheAsync] No cache file found — starting with empty cache");
                return;
            }

            var json = await File.ReadAllTextAsync(CachePath).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            if (loaded != null)
            {
                _appIdCache = new Dictionary<string, int>(loaded, StringComparer.Ordinal);
                CrashReporter.Log($"[PcgwService.LoadCacheAsync] Loaded {_appIdCache.Count} cached AppIDs");
            }
        }
        catch (JsonException ex)
        {
            CrashReporter.Log($"[PcgwService.LoadCacheAsync] Malformed cache JSON — {ex.Message}");
            _appIdCache = new Dictionary<string, int>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.LoadCacheAsync] Cache load failed — {ex.Message}");
            _appIdCache = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        // Versioned migration: wipe URL cache if it was built with an older resolution method.
        // Version 1 = OpenSearch (appid.php was broken). Version 2 = back to appid.php (when restored).
        // The required version can also be driven remotely via manifest.PcgwUrlCacheVersion.
        const int UrlCacheVersion = 1;
        int existingVersion = 0;
        if (File.Exists(UrlCacheMarkerPath))
            int.TryParse(File.ReadAllText(UrlCacheMarkerPath).Trim(), out existingVersion);

        if (existingVersion < UrlCacheVersion)
        {
            try
            {
                if (File.Exists(UrlCachePath)) File.Delete(UrlCachePath);
                File.WriteAllText(UrlCacheMarkerPath, UrlCacheVersion.ToString());
                CrashReporter.Log($"[PcgwService.LoadCacheAsync] URL cache wiped (version {existingVersion} → {UrlCacheVersion})");
            }
            catch { /* non-critical */ }
        }

        // Load URL cache (wiki URLs resolved via OpenSearch)
        try
        {
            if (File.Exists(UrlCachePath))
            {
                var urlJson = await File.ReadAllTextAsync(UrlCachePath).ConfigureAwait(false);
                var loadedUrls = JsonSerializer.Deserialize<Dictionary<string, string>>(urlJson);
                if (loadedUrls != null)
                    _urlCache = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(loadedUrls, StringComparer.Ordinal);
            }
        }
        catch { /* non-critical — start with empty URL cache */ }
    }

    public async Task<string?> ResolveUrlAsync(string gameName, int? steamAppId, string installPath, RemoteManifest? manifest)
    {
        // 1. Manifest pcgwUrlOverrides (highest priority).
        if (manifest?.PcgwUrlOverrides != null
            && manifest.PcgwUrlOverrides.TryGetValue(gameName, out var overrideUrl)
            && !string.IsNullOrEmpty(overrideUrl))
        {
            return overrideUrl;
        }

        var normalized = _gameDetection.NormalizeName(gameName);

        // 2. Cached wiki URL — avoids HTTP calls every session.
        if (!string.IsNullOrEmpty(normalized) && _urlCache.TryGetValue(normalized, out var cachedUrl))
            return cachedUrl;

        // 3. Check for cached negative result — avoids HTTP calls for non-PCGW games.
        if (!string.IsNullOrEmpty(normalized) && _appIdCache.TryGetValue(normalized, out var cachedId) && cachedId == -1)
            return null;

        // 4. Resolve Steam AppID via the priority chain (passing our cache).
        var appId = await _steamAppIdResolver.ResolveAsync(
            gameName, steamAppId, installPath, manifest, _appIdCache).ConfigureAwait(false);

        if (appId.HasValue)
        {
            if (!string.IsNullOrEmpty(normalized))
            {
                _appIdCache[normalized] = appId.Value;
                await SaveCacheAsync().ConfigureAwait(false);
            }

            // Use appid.php when the manifest flag is on (endpoint restored), otherwise OpenSearch.
            if (manifest?.PcgwUseAppId == true)
                return BuildAppIdUrl(appId.Value);

            // appid.php currently unreliable — use OpenSearch for the actual wiki URL.
            var wikiUrl = await OpenSearchFallbackAsync(gameName).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(normalized) && wikiUrl != null)
            {
                _urlCache[normalized] = wikiUrl;
                SaveUrlCacheToDisk();
            }

            return wikiUrl;
        }

        // 5. OpenSearch fallback (no AppID resolved).
        var result = await OpenSearchFallbackAsync(gameName).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(normalized))
        {
            if (result != null)
            {
                _urlCache[normalized] = result;
                SaveUrlCacheToDisk();
            }
            else
            {
                // Cache negative result so we don't retry HTTP calls next session.
                _appIdCache[normalized] = -1;
                await SaveCacheAsync().ConfigureAwait(false);
            }
        }

        return result;
    }

    /// <summary>
    /// Constructs the PCGW appid.php redirect URL for a given Steam AppID.
    /// Exposed as static for testability (Property 6).
    /// </summary>
    internal static string BuildAppIdUrl(int appId)
        => $"https://www.pcgamingwiki.com/api/appid.php?appid={appId}";

    /// <summary>
    /// Constructs a PCGW wiki page URL from a page title, replacing spaces with underscores.
    /// Exposed as static for testability (Property 6).
    /// </summary>
    internal static string BuildWikiUrl(string pageTitle)
        => $"https://www.pcgamingwiki.com/wiki/{pageTitle.Replace(' ', '_')}";

    /// <summary>
    /// Queries the PCGW OpenSearch API and returns the wiki URL for the first result,
    /// or null if no results or an error occurs.
    /// </summary>
    private async Task<string?> OpenSearchFallbackAsync(string gameName)
    {
        if (_pcgwDown) return null;

        try
        {
            var encodedName = Uri.EscapeDataString(gameName);
            var url = $"https://www.pcgamingwiki.com/w/api.php?action=opensearch&search={encodedName}&limit=5&format=json";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_pcgwCts.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var response = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[PcgwService.OpenSearchFallback] OpenSearch returned {(int)response.StatusCode} for '{gameName}'");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // OpenSearch returns: ["search term", ["Title1", "Title2"], ["Desc1", "Desc2"], ["URL1", "URL2"]]
            JsonElement[]? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<JsonElement[]>(json);
            }
            catch (JsonException ex)
            {
                CrashReporter.Log($"[PcgwService.OpenSearchFallback] Malformed JSON — {ex.Message}");
                return null;
            }

            if (parsed == null || parsed.Length < 2)
                return null;

            var titles = parsed[1];
            if (titles.ValueKind != JsonValueKind.Array || titles.GetArrayLength() == 0)
                return null;

            var firstTitle = titles[0].GetString();
            if (string.IsNullOrEmpty(firstTitle))
                return null;

            return BuildWikiUrl(firstTitle);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.OpenSearchFallback] Failed — {ex.Message} — disabling PCGW for this session");
            _pcgwDown = true;
            try { _pcgwCts.Cancel(); } catch { }
            return null;
        }
    }

    /// <inheritdoc />
    public void ClearNegativeCache()
    {
        var negativeKeys = _appIdCache.Where(kv => kv.Value == -1).Select(kv => kv.Key).ToList();
        foreach (var key in negativeKeys)
            _appIdCache.Remove(key);
        if (negativeKeys.Count > 0)
        {
            WriteCacheToDisk();
            CrashReporter.Log($"[PcgwService.ClearNegativeCache] Cleared {negativeKeys.Count} negative sentinel(s)");
        }
    }

    /// <summary>
    /// Schedules a debounced cache write. Resets a 500 ms timer on each call;
    /// the actual disk write happens only once the timer fires (i.e. 500 ms after
    /// the last call). This avoids ~45 concurrent writes during startup.
    /// </summary>
    private Task SaveCacheAsync()
    {
        lock (_saveLock)
        {
            if (_saveDebounceTimer != null)
            {
                _saveDebounceTimer.Change(500, Timeout.Infinite);
            }
            else
            {
                _saveDebounceTimer = new Timer(_ => WriteCacheToDisk(), null, 500, Timeout.Infinite);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FlushCacheAsync()
    {
        Timer? timer;
        lock (_saveLock)
        {
            timer = _saveDebounceTimer;
            _saveDebounceTimer = null;
        }

        if (timer != null)
        {
            timer.Dispose();
            WriteCacheToDisk();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs the actual disk write with retry logic via <see cref="FileHelper"/>.
    /// </summary>
    private void WriteCacheToDisk()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            var json = JsonSerializer.Serialize(_appIdCache, s_writeOptions);
            FileHelper.WriteAllTextWithRetry(CachePath, json, "PcgwService.SaveCache");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.SaveCacheAsync] Cache write failed — {ex.Message}");
        }
    }

    private Timer? _urlSaveDebounceTimer;

    private void SaveUrlCacheToDisk()
    {
        lock (_saveLock)
        {
            if (_urlSaveDebounceTimer != null)
                _urlSaveDebounceTimer.Change(500, Timeout.Infinite);
            else
                _urlSaveDebounceTimer = new Timer(_ => WriteUrlCacheToDisk(), null, 500, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Called after the manifest is fetched. If the manifest requests a higher cache version
    /// than what's stored locally, wipes the URL cache so links re-resolve on next BuildCards.
    /// This allows a remote manifest change to force a clean re-resolve without a new app build.
    /// </summary>
    public void CheckManifestCacheVersion(RemoteManifest? manifest)
    {
        if (manifest == null || manifest.PcgwUrlCacheVersion <= 0) return;

        int existingVersion = 0;
        if (File.Exists(UrlCacheMarkerPath))
            int.TryParse(File.ReadAllText(UrlCacheMarkerPath).Trim(), out existingVersion);

        if (manifest.PcgwUrlCacheVersion > existingVersion)
        {
            try
            {
                _urlCache.Clear();
                if (File.Exists(UrlCachePath)) File.Delete(UrlCachePath);
                File.WriteAllText(UrlCacheMarkerPath, manifest.PcgwUrlCacheVersion.ToString());
                CrashReporter.Log($"[PcgwService] URL cache wiped via manifest (version {existingVersion} → {manifest.PcgwUrlCacheVersion})");
            }
            catch { /* non-critical */ }
        }
    }

    private void WriteUrlCacheToDisk()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UrlCachePath)!);
            var snapshot = new Dictionary<string, string>(_urlCache, StringComparer.Ordinal);
            var json = JsonSerializer.Serialize(snapshot, s_writeOptions);
            FileHelper.WriteAllTextWithRetry(UrlCachePath, json, "PcgwService.SaveUrlCache");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.SaveUrlCache] Write failed — {ex.Message}");
        }
    }
}
