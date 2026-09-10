using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages dashdogy's <c>RTX40MFG-Unlock</c> (Universal RTXMFG) — an alternative
/// DLSS Multi Frame Generation unlock for RTX 40-series (experimental RTX 30) that
/// does <b>not</b> use ReShade. It ships a single <c>RTXMFG.dll</c> that must be
/// renamed to a proxy filename the game loads early (e.g. <c>dxgi.dll</c>) and
/// placed beside the game executable; the in-game menu opens with <b>Backspace</b>
/// and offers fixed multipliers up to 6x and Dynamic MFG. MIT-licensed. Adas never
/// bundles it: the latest release archive is downloaded at run time, and the DLL it
/// replaces (if any) is backed up for exact restoration.
///
/// This is a separate route from <see cref="MfgUnlockService"/> (the mavismmg RenoDX
/// add-on); the two are never installed into the same folder together.
/// </summary>
public sealed class RtxMfgUnlockService
{
    private const string DllName = "RTXMFG.dll";
    private const string GitHubApiUrl = "https://api.github.com/repos/dashdogy/RTX40MFG-Unlock/releases/latest";
    private const string ReleaseTagBaseUrl = "https://github.com/dashdogy/RTX40MFG-Unlock/releases/tag";
    private const string MarkerRelativePath = ".adas/rtxmfg-install.json";
    private const string BackupRelativeDir = ".adas/backups/rtxmfg";

    /// <summary>
    /// Proxy filenames the game may load early, from the RTX40MFG-Unlock README.
    /// The correct name depends on the game; <c>dxgi.dll</c> is the common default
    /// for DirectX 10/11/12 titles. Only one copy is ever installed.
    /// </summary>
    public static IReadOnlyList<string> ProxyFilenames { get; } = new[]
    {
        "dxgi.dll", "version.dll", "dinput8.dll", "winmm.dll",
        "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll",
        "dsound.dll", "wininet.dll", "winhttp.dll",
        "xinput1_3.dll", "xinput1_4.dll", "xinput9_1_0.dll",
    };

    public const string DefaultProxyFilename = "dxgi.dll";

    private readonly HttpClient _http;
    private readonly ICrashReporter _crashReporter;
    private readonly GitHubETagCache _etagCache;
    private readonly string _stagingDir;
    private readonly string _versionFile;

    public RtxMfgUnlockService(HttpClient http, ICrashReporter crashReporter, GitHubETagCache etagCache)
    {
        _http = http;
        _crashReporter = crashReporter;
        _etagCache = etagCache;
        _stagingDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "rtx40mfg-unlock");
        _versionFile = Path.Combine(_stagingDir, "version.txt");
    }

    public string? StagedVersion => File.Exists(_versionFile) ? File.ReadAllText(_versionFile).Trim() : null;

    public bool IsStagingReady => File.Exists(Path.Combine(_stagingDir, DllName));

    public string GetReleaseUrl(string version) => $"{ReleaseTagBaseUrl}/{version}";

    /// <summary>Whether an RTXMFG proxy DLL deployed by Adas is present in the folder.</summary>
    public static bool IsInstalledIn(string gameFolder)
        => !string.IsNullOrEmpty(gameFolder)
           && File.Exists(Path.Combine(gameFolder, MarkerRelativePath));

    /// <summary>Downloads and extracts RTXMFG.dll into the per-user staging cache if needed.</summary>
    public async Task<string?> EnsureStagingAsync(
        IProgress<(string message, double percent)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_stagingDir);
        progress?.Report(("Resolving RTX40MFG-Unlock release…", 10));

        var (version, downloadUrl) = await FetchLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(downloadUrl))
        {
            _crashReporter.Log("[RtxMfgUnlockService.EnsureStagingAsync] Could not resolve latest release");
            return null;
        }

        if (IsStagingReady && string.Equals(StagedVersion, version, StringComparison.OrdinalIgnoreCase))
            return version;

        progress?.Report(($"Downloading Universal RTXMFG v{version}…", 40));
        var archive = Path.Combine(_stagingDir, "rtxmfg.zip");
        try
        {
            var bytes = await _http.GetByteArrayAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(archive, bytes, cancellationToken).ConfigureAwait(false);

            var stagedDll = Path.Combine(_stagingDir, DllName);
            if (File.Exists(stagedDll)) File.Delete(stagedDll);
            ExtractSingleEntry(archive, DllName, stagedDll);
            if (!File.Exists(stagedDll))
                throw new FileNotFoundException($"{DllName} was not found in {Path.GetFileName(downloadUrl)}.");

            await File.WriteAllTextAsync(_versionFile, version, cancellationToken).ConfigureAwait(false);
            _crashReporter.Log($"[RtxMfgUnlockService.EnsureStagingAsync] Staged v{version} ({bytes.Length} bytes)");
            progress?.Report(("Universal RTXMFG ready", 100));
            return version;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[RtxMfgUnlockService.EnsureStagingAsync] Failed — {ex.Message}");
            return null;
        }
        finally
        {
            try { if (File.Exists(archive)) File.Delete(archive); } catch { }
        }
    }

    /// <summary>
    /// Deploys RTXMFG.dll into <paramref name="gameFolder"/> renamed to
    /// <paramref name="proxyFilename"/>, backing up any existing DLL of that name for
    /// exact restore. Records the choice so <see cref="Uninstall"/> can undo it.
    /// </summary>
    public async Task<bool> InstallAsync(
        string gameFolder,
        string proxyFilename,
        IProgress<(string message, double percent)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        gameFolder = Path.GetFullPath(gameFolder);
        if (!Directory.Exists(gameFolder))
            throw new DirectoryNotFoundException($"Game folder not found: {gameFolder}");
        if (!ProxyFilenames.Contains(proxyFilename, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unsupported proxy filename: {proxyFilename}", nameof(proxyFilename));

        var version = await EnsureStagingAsync(progress, cancellationToken).ConfigureAwait(false);
        if (!IsStagingReady) return false;

        if (IsInstalledIn(gameFolder))
            Uninstall(gameFolder); // never stack two proxy DLLs

        progress?.Report(("Deploying Universal RTXMFG…", 80));

        var dest = Path.Combine(gameFolder, proxyFilename);
        var backupDir = Path.Combine(gameFolder, BackupRelativeDir);
        var backupPath = Path.Combine(backupDir, proxyFilename);
        var hadBackup = false;
        try
        {
            if (File.Exists(dest))
            {
                Directory.CreateDirectory(backupDir);
                File.Copy(dest, backupPath, overwrite: true);
                hadBackup = true;
            }

            File.Copy(Path.Combine(_stagingDir, DllName), dest, overwrite: true);

            var marker = new RtxMfgInstallRecord
            {
                ProxyFilename = proxyFilename,
                HadBackup = hadBackup,
                Version = version,
                InstalledUtc = DateTime.UtcNow,
            };
            var markerPath = Path.Combine(gameFolder, MarkerRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            await File.WriteAllTextAsync(markerPath,
                JsonSerializer.Serialize(marker, MarkerJson), cancellationToken).ConfigureAwait(false);

            _crashReporter.Log($"[RtxMfgUnlockService.InstallAsync] Deployed {proxyFilename} v{version} to {gameFolder} (backup={hadBackup})");
            progress?.Report(("Universal RTXMFG installed!", 100));
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[RtxMfgUnlockService.InstallAsync] Deploy failed — {ex.Message}");
            progress?.Report(($"❌ {ex.Message}", 0));
            return false;
        }
    }

    /// <summary>Removes the deployed proxy DLL and restores the original file if one was backed up.</summary>
    public bool Uninstall(string gameFolder)
    {
        if (string.IsNullOrEmpty(gameFolder)) return false;
        var markerPath = Path.Combine(gameFolder, MarkerRelativePath);
        try
        {
            if (!File.Exists(markerPath)) return true;
            var marker = JsonSerializer.Deserialize<RtxMfgInstallRecord>(File.ReadAllText(markerPath));
            var proxyName = marker?.ProxyFilename;
            if (string.IsNullOrEmpty(proxyName)) { File.Delete(markerPath); return true; }

            var deployed = Path.Combine(gameFolder, proxyName);
            var backupPath = Path.Combine(gameFolder, BackupRelativeDir, proxyName);

            if (marker!.HadBackup && File.Exists(backupPath))
            {
                File.Copy(backupPath, deployed, overwrite: true);
                File.Delete(backupPath);
            }
            else if (File.Exists(deployed))
            {
                File.Delete(deployed);
            }

            File.Delete(markerPath);
            _crashReporter.Log($"[RtxMfgUnlockService.Uninstall] Removed {proxyName} from {gameFolder} (restored={marker.HadBackup})");
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[RtxMfgUnlockService.Uninstall] Failed — {ex.Message}");
            return false;
        }
    }

    private static void ExtractSingleEntry(string archivePath, string entryFileName, string destPath)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        var entry = zip.Entries.FirstOrDefault(e =>
            string.Equals(Path.GetFileName(e.FullName), entryFileName, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            throw new FileNotFoundException($"{entryFileName} not present in archive.");
        entry.ExtractToFile(destPath, overwrite: true);
    }

    private async Task<(string? version, string? downloadUrl)> FetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = await _etagCache.GetWithETagAsync(_http, GitHubApiUrl).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json))
            {
                _crashReporter.Log(_etagCache.IsRateLimited
                    ? "[RtxMfgUnlockService] GitHub API rate limited — could not fetch the latest RTXMFG release."
                    : "[RtxMfgUnlockService] GitHub API returned no release data.");
                return (null, null);
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagEl)) return (null, null);
            var tag = tagEl.GetString();
            if (string.IsNullOrEmpty(tag)) return (null, null);
            var version = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;

            string? downloadUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var nameEl)) continue;
                    var name = nameEl.GetString();
                    // The universal single-DLL bundle is shipped as RTXMFG-v*.zip.
                    if (name != null
                        && name.StartsWith("RTXMFG", StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        && asset.TryGetProperty("browser_download_url", out var urlEl))
                    {
                        downloadUrl = urlEl.GetString();
                        break;
                    }
                }
            }

            return (version, downloadUrl);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[RtxMfgUnlockService] FetchLatestRelease failed — {ex.Message}");
            return (null, null);
        }
    }

    private static readonly JsonSerializerOptions MarkerJson = new() { WriteIndented = true };

    private sealed class RtxMfgInstallRecord
    {
        [JsonPropertyName("proxyFilename")] public string? ProxyFilename { get; set; }
        [JsonPropertyName("hadBackup")] public bool HadBackup { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("installedUtc")] public DateTime InstalledUtc { get; set; }
    }
}
