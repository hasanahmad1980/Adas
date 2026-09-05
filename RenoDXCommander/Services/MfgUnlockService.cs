using System.Net.Http;
using System.Text.Json;

namespace RenoDXCommander.Services;

/// <summary>
/// The <c>[RenoDX.MFGUnlock]</c> settings the add-on reads from ReShade.ini.
/// Values are ints; defaults match the upstream add-on's own defaults.
/// </summary>
public sealed class MfgUnlockConfig
{
    /// <summary>Master switch (1 = on).</summary>
    public int Enabled { get; set; } = 1;
    /// <summary>Maximum frame-generation multiplier (2–6).</summary>
    public int MaxCount { get; set; } = 4;
    /// <summary>Force flip metering off — required for 3x+ on Ada (1 = on).</summary>
    public int ForceFlipMeteringOff { get; set; } = 1;
    /// <summary>Temporal midpoint / interpolation correction (1 = on).</summary>
    public int TemporalFix { get; set; } = 1;
    /// <summary>Force a specific multiplier (0 = respect the game's choice, 2–6 = force).</summary>
    public int ForceMultiplier { get; set; }
    /// <summary>Raise older plugin frame-count limits up to 6x (1 = on).</summary>
    public int RaiseFrameCeiling { get; set; }
    /// <summary>Load the driver OTA plugin set (1 = on).</summary>
    public int ForceOTAPlugins { get; set; }
}

/// <summary>
/// Manages the MFG Ada Unlock add-on — download, staging, install, uninstall,
/// update detection, and ReShade.ini configuration. Hosted on the
/// <c>mavismmg/MFGAdaUnlock-RenoDx</c> GitHub releases (MIT). See
/// <see cref="IMfgUnlockService"/> for the redistribution posture.
/// </summary>
public class MfgUnlockService : IMfgUnlockService
{
    private const string AddonFileName = "renodx-mfgunlock.addon64";
    private const string ConfigSection = "RenoDX.MFGUnlock";
    private const string GitHubApiUrl = "https://api.github.com/repos/mavismmg/MFGAdaUnlock-RenoDx/releases";
    private const string DefaultDownloadBaseUrl = "https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases/download";
    private const string ReleaseTagBaseUrl = "https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases/tag";

    private readonly HttpClient _http;
    private readonly ICrashReporter _crashReporter;
    private readonly string _stagingDir;
    private readonly string _versionFile;

    public MfgUnlockService(HttpClient http, ICrashReporter crashReporter)
    {
        _http = http;
        _crashReporter = crashReporter;
        _stagingDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "mfg-unlock");
        _versionFile = Path.Combine(_stagingDir, "version.txt");
    }

    /// <summary>The add-on filename deployed to game folders.</summary>
    public static string FileName => AddonFileName;

    /// <summary>The <c>[RenoDX.MFGUnlock]</c> section name written to ReShade.ini.</summary>
    public static string SectionName => ConfigSection;

    public string? StagedVersion => File.Exists(_versionFile) ? File.ReadAllText(_versionFile).Trim() : null;

    public bool IsStagingReady => File.Exists(Path.Combine(_stagingDir, AddonFileName));

    public bool HasUpdate { get; private set; }

    public string? LatestVersion { get; private set; }

    public string? ReleaseNotes { get; private set; }

    public string? ManifestUrlOverride { get; set; }

    public async Task EnsureStagingAsync(IProgress<(string message, double percent)>? progress = null)
    {
        if (IsStagingReady && !HasUpdate)
        {
            _crashReporter.Log("[MfgUnlockService.EnsureStagingAsync] Staging already valid — skipping download");
            return;
        }

        Directory.CreateDirectory(_stagingDir);
        progress?.Report(("Downloading MFG Ada Unlock...", 10));

        var (version, downloadUrl, body) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(downloadUrl))
        {
            _crashReporter.Log("[MfgUnlockService.EnsureStagingAsync] Could not resolve latest release");
            return;
        }

        progress?.Report(("Downloading MFG Ada Unlock...", 30));

        try
        {
            var bytes = await _http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
            var destPath = Path.Combine(_stagingDir, AddonFileName);
            await File.WriteAllBytesAsync(destPath, bytes).ConfigureAwait(false);
            File.WriteAllText(_versionFile, version);
            ReleaseNotes = body;
            HasUpdate = false;
            _crashReporter.Log($"[MfgUnlockService.EnsureStagingAsync] Downloaded v{version} ({bytes.Length} bytes)");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MfgUnlockService.EnsureStagingAsync] Download failed ({downloadUrl}) — {ex.Message}");
        }

        progress?.Report(("MFG Ada Unlock ready", 100));
    }

    public async Task CheckForUpdateAsync()
    {
        var (version, _, body) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version))
        {
            _crashReporter.Log("[MfgUnlockService.CheckForUpdateAsync] Could not resolve latest version");
            return;
        }

        LatestVersion = version;
        ReleaseNotes = body;
        var current = StagedVersion;
        HasUpdate = !string.Equals(current, version, StringComparison.OrdinalIgnoreCase);
        _crashReporter.Log($"[MfgUnlockService.CheckForUpdateAsync] Cached={current ?? "(none)"}, Remote={version}, HasUpdate={HasUpdate}");
    }

    public async Task<bool> InstallAsync(string installPath, IProgress<(string message, double percent)>? progress = null)
    {
        if (string.IsNullOrEmpty(installPath)) return false;

        await EnsureStagingAsync(progress).ConfigureAwait(false);
        if (!IsStagingReady) return false;

        progress?.Report(("Deploying MFG Ada Unlock...", 70));

        var src = Path.Combine(_stagingDir, AddonFileName);
        var dest = Path.Combine(ModInstallService.GetAddonDeployPath(installPath), AddonFileName);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest, overwrite: true);
            EnsureConfigDefaults(installPath);
            _crashReporter.Log($"[MfgUnlockService.InstallAsync] Deployed to {Path.GetDirectoryName(dest)}");
            progress?.Report(("MFG Ada Unlock installed!", 100));
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MfgUnlockService.InstallAsync] Deploy failed — {ex.Message}");
            progress?.Report(($"❌ {ex.Message}", 0));
            return false;
        }
    }

    public bool Uninstall(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return false;
        var filePath = Path.Combine(ModInstallService.GetAddonDeployPath(installPath), AddonFileName);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _crashReporter.Log($"[MfgUnlockService.Uninstall] Removed from {Path.GetDirectoryName(filePath)}");
            }
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MfgUnlockService.Uninstall] Failed — {ex.Message}");
            return false;
        }
    }

    public bool IsInstalledIn(string installPath)
        => IsInstalled(installPath);

    /// <summary>Detects whether the add-on is deployed in a game folder (static helper).</summary>
    public static bool IsInstalled(string installPath)
        => !string.IsNullOrEmpty(installPath)
           && File.Exists(Path.Combine(ModInstallService.GetAddonDeployPath(installPath), AddonFileName));

    public string GetReleaseUrl(string version)
        => $"{ReleaseTagBaseUrl}/{version}";

    // ── Configuration (ReShade.ini [RenoDX.MFGUnlock]) ────────────────────────

    /// <summary>Path to the ReShade.ini that the add-on reads (next to the game's ReShade DLL).</summary>
    private static string ReshadeIniPath(string installPath)
        => Path.Combine(installPath, "reshade.ini");

    public MfgUnlockConfig ReadConfig(string installPath)
    {
        var config = new MfgUnlockConfig();
        try
        {
            var iniPath = ReshadeIniPath(installPath);
            if (!File.Exists(iniPath)) return config;

            var doc = IniTextDocument.Load(iniPath);
            config.Enabled = ReadInt(doc, "Enabled", config.Enabled);
            config.MaxCount = ReadInt(doc, "MaxCount", config.MaxCount);
            config.ForceFlipMeteringOff = ReadInt(doc, "ForceFlipMeteringOff", config.ForceFlipMeteringOff);
            config.TemporalFix = ReadInt(doc, "TemporalFix", config.TemporalFix);
            config.ForceMultiplier = ReadInt(doc, "ForceMultiplier", config.ForceMultiplier);
            config.RaiseFrameCeiling = ReadInt(doc, "RaiseFrameCeiling", config.RaiseFrameCeiling);
            config.ForceOTAPlugins = ReadInt(doc, "ForceOTAPlugins", config.ForceOTAPlugins);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MfgUnlockService.ReadConfig] Failed — {ex.Message}");
        }
        return config;
    }

    public void WriteConfig(string installPath, MfgUnlockConfig config)
    {
        try
        {
            var iniPath = ReshadeIniPath(installPath);
            var doc = IniTextDocument.Load(iniPath);
            doc.SetValue(ConfigSection, "Enabled", config.Enabled.ToString());
            doc.SetValue(ConfigSection, "MaxCount", config.MaxCount.ToString());
            doc.SetValue(ConfigSection, "ForceFlipMeteringOff", config.ForceFlipMeteringOff.ToString());
            doc.SetValue(ConfigSection, "TemporalFix", config.TemporalFix.ToString());
            doc.SetValue(ConfigSection, "ForceMultiplier", config.ForceMultiplier.ToString());
            doc.SetValue(ConfigSection, "RaiseFrameCeiling", config.RaiseFrameCeiling.ToString());
            doc.SetValue(ConfigSection, "ForceOTAPlugins", config.ForceOTAPlugins.ToString());
            doc.Save(iniPath);
            _crashReporter.Log($"[MfgUnlockService.WriteConfig] Wrote [{ConfigSection}] to {iniPath}");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MfgUnlockService.WriteConfig] Failed — {ex.Message}");
        }
    }

    /// <summary>Writes default config only if the section is not already present (never clobbers user edits).</summary>
    private void EnsureConfigDefaults(string installPath)
    {
        var iniPath = ReshadeIniPath(installPath);
        if (File.Exists(iniPath) && IniTextDocument.Load(iniPath).TryGetValue(ConfigSection, "Enabled", out _))
            return;
        WriteConfig(installPath, new MfgUnlockConfig());
    }

    private static int ReadInt(IniTextDocument doc, string key, int fallback)
        => doc.TryGetValue(ConfigSection, key, out var value)
           && int.TryParse(value.Text.Trim(), out var parsed)
            ? parsed
            : fallback;

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<(string? version, string? downloadUrl, string? body)> FetchLatestReleaseInfoAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
            request.Headers.Add("User-Agent", "RHI");
            request.Headers.Add("Accept", "application/vnd.github+json");

            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _crashReporter.Log($"[MfgUnlockService] GitHub API returned {response.StatusCode}");
                return (null, null, null);
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var candidates = new List<(string version, string? downloadUrl, string? body, Version parsed)>();

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) continue;
                if (!release.TryGetProperty("tag_name", out var tagEl)) continue;
                var tag = tagEl.GetString();
                if (string.IsNullOrEmpty(tag)) continue;

                // Fork tags are plain versions ("0.5.1"); tolerate a leading "v".
                var version = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;
                var body = release.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;

                string? downloadUrl = null;
                if (release.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var nameEl)
                            && string.Equals(nameEl.GetString(), AddonFileName, StringComparison.OrdinalIgnoreCase)
                            && asset.TryGetProperty("browser_download_url", out var urlEl))
                        {
                            downloadUrl = urlEl.GetString();
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(ManifestUrlOverride))
                    downloadUrl = ManifestUrlOverride.Replace("{version}", version);
                else if (downloadUrl == null)
                    downloadUrl = $"{DefaultDownloadBaseUrl}/{tag}/{AddonFileName}";

                Version.TryParse(version, out var parsed);
                candidates.Add((version, downloadUrl, body, parsed ?? new Version(0, 0)));
            }

            if (candidates.Count == 0)
            {
                _crashReporter.Log("[MfgUnlockService] No release with a renodx-mfgunlock.addon64 asset");
                return (null, null, null);
            }

            var best = candidates.OrderByDescending(c => c.parsed).First();
            return (best.version, best.downloadUrl, best.body);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MfgUnlockService] FetchLatestReleaseInfo failed — {ex.Message}");
            return (null, null, null);
        }
    }
}
