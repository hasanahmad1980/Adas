using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Resolves cover art for a game by name. Uses <see cref="ISteamAppIdResolver"/> to turn the game
/// name/install into a Steam AppID, then fetches the Steam library portrait (falling back to the
/// header image) from the Steam CDN and caches it under <c>%LocalAppData%\RHI\artwork</c>. Misses
/// are cached with a short TTL so non-Steam titles don't hit the network on every launch.
/// </summary>
public interface IGameArtworkService
{
    /// <summary>
    /// Returns a local file path to the cached cover art for the game, or null when none is found.
    /// </summary>
    Task<string?> GetArtworkPathAsync(string gameName, int? steamAppId, string installPath, RemoteManifest? manifest);
}

/// <inheritdoc />
public sealed class GameArtworkService : IGameArtworkService
{
    private readonly HttpClient _http;
    private readonly ISteamAppIdResolver _appIdResolver;

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "artwork");

    /// <summary>How long a "no art" result is trusted before we retry the network.</summary>
    private static readonly TimeSpan MissTtl = TimeSpan.FromDays(7);

    /// <summary>Caps concurrent artwork fetches so a large library doesn't flood the CDN.</summary>
    private static readonly SemaphoreSlim _gate = new(3, 3);

    public GameArtworkService(HttpClient http, ISteamAppIdResolver appIdResolver)
    {
        _http = http;
        _appIdResolver = appIdResolver;
    }

    /// <inheritdoc />
    public async Task<string?> GetArtworkPathAsync(string gameName, int? steamAppId, string installPath, RemoteManifest? manifest)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var key = Slug(gameName);
            var jpg = Path.Combine(CacheDir, key + ".jpg");
            if (IsUsable(jpg)) return jpg;

            var miss = Path.Combine(CacheDir, key + ".miss");
            if (File.Exists(miss) && DateTime.UtcNow - File.GetLastWriteTimeUtc(miss) < MissTtl)
                return null;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                // Another card for the same title may have filled the cache while we waited.
                if (IsUsable(jpg)) return jpg;

                // Emulators are never on the Steam store under a game name — map them to a logo first.
                if (GameNameCleaner.TryGetEmulatorArtUrl(gameName, out var emuUrl)
                    && await TryDownloadAsync(emuUrl, jpg).ConfigureAwait(false))
                {
                    return jpg;
                }

                var appId = await _appIdResolver
                    .ResolveAsync(gameName, steamAppId, installPath, manifest)
                    .ConfigureAwait(false);
                if (appId is null or <= 0)
                {
                    TouchMiss(miss);
                    return null;
                }

                foreach (var url in ArtUrls(appId.Value))
                {
                    if (await TryDownloadAsync(url, jpg).ConfigureAwait(false))
                        return jpg;
                }

                TouchMiss(miss);
                return null;
            }
            finally { _gate.Release(); }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[GameArtworkService] '{gameName}' — {ex.Message}");
            return null;
        }
    }

    private static bool IsUsable(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 0; }
        catch { return false; }
    }

    // Portrait first (the library "capsule"), then header as a landscape fallback. Two CDN hosts
    // are tried because the akamai host is occasionally stale/blocked on some networks.
    private static IEnumerable<string> ArtUrls(int appId) => new[]
    {
        $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
        $"https://steamcdn-a.akamaihd.net/steam/apps/{appId}/library_600x900.jpg",
        $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
    };

    private async Task<bool> TryDownloadAsync(string url, string dest)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return false;

            var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length == 0) return false;

            var tmp = dest + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes).ConfigureAwait(false);
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
            return true;
        }
        catch { return false; }
    }

    private static void TouchMiss(string missPath)
    {
        try { File.WriteAllText(missPath, DateTime.UtcNow.ToString("o")); }
        catch { /* negative cache is best-effort */ }
    }

    /// <summary>Filesystem-safe cache key derived from the game name (letters/digits only).</summary>
    private static string Slug(string name)
    {
        var slug = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return string.IsNullOrEmpty(slug) ? "game" : slug;
    }
}
