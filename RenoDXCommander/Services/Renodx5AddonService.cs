using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages the unified RenoDX DLSS add-on — staging, install, and auto-update.
/// The unified build replaces both renodx-dlss5 and the standalone DX11 bridge.
/// Auto-redeploys to any game folder where the addon is already present.
/// </summary>
public class Renodx5AddonService
{
    public const string AddonFileName = "renodx-dlss.addon64";
    public static readonly string[] ObsoleteAddonFileNames =
    {
        "renodx-dlss5.addon64",
        "renodx-dlss5(2).addon64",
        "dlss5-bridge.addon64",
        "dlss5-dx11-bridge.addon64",
        // No x86 builds exist for these names. They are listed so releases that
        // incorrectly relabelled the x64 payload as .addon32 can be retired.
        "renodx-dlss.addon32",
        "renodx-dlss5.addon32",
        "dlss5-dx11-bridge.addon32",
    };

    public static bool IsManagedAddonFileName(string? fileName)
        => !string.IsNullOrWhiteSpace(fileName)
           && (fileName.Equals(AddonFileName, StringComparison.OrdinalIgnoreCase)
               || ObsoleteAddonFileNames.Any(name => fileName.Equals(name, StringComparison.OrdinalIgnoreCase)));
    private const string TagPrefix = "renodx-dlss-";
    private static readonly string GitHubApiUrl =
        "https://api.github.com/repos/RankFTW/rhi-repo/releases?per_page=100";

    private readonly HttpClient _http;
    private readonly ICrashReporter _crashReporter;
    private readonly IGameLibraryService _gameLibraryService;
    private readonly IDlssStreamlineService _dlssStreamlineService;

    private readonly string _stagingDir;
    private readonly string _versionFile;

    public Renodx5AddonService(
        HttpClient http,
        ICrashReporter crashReporter,
        IGameLibraryService gameLibraryService,
        IDlssStreamlineService dlssStreamlineService)
    {
        _http                  = http;
        _crashReporter         = crashReporter;
        _gameLibraryService    = gameLibraryService;
        _dlssStreamlineService = dlssStreamlineService;

        _stagingDir  = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "rdx5");
        _versionFile = Path.Combine(_stagingDir, "version.txt");
    }

    // ── Properties ────────────────────────────────────────────────────────────

    /// <summary>Whether the unified RenoDX DLSS add-on is ready in staging.</summary>
    public bool IsStagingReady => File.Exists(Path.Combine(_stagingDir, AddonFileName));

    /// <summary>Full path to the staged unified RenoDX DLSS add-on.</summary>
    public string StagedFilePath => Path.Combine(_stagingDir, AddonFileName);

    /// <summary>The currently staged version string (tag suffix), or null if not staged.</summary>
    public string? StagedVersion
        => File.Exists(_versionFile) ? File.ReadAllText(_versionFile).Trim() : null;

    /// <summary>Whether an update is available (set after CheckForUpdateAsync).</summary>
    public bool HasUpdate { get; private set; }

    /// <summary>The latest remote version string (set after CheckForUpdateAsync).</summary>
    public string? LatestVersion { get; private set; }

    /// <summary>Stages a user-supplied or installer-bundled add-on under its canonical filename.</summary>
    public void StageLocalAddon(string sourcePath, string version)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The RenoDX DLSS 5 add-on was not found.", sourcePath);
        if (!AddonPackService.IsAddonArchitectureCompatible(sourcePath, is32Bit: false))
            throw new InvalidDataException("The unified RenoDX DLSS add-on is not a valid 64-bit ReShade add-on.");
        Directory.CreateDirectory(_stagingDir);
        var temporary = StagedFilePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(sourcePath, temporary, overwrite: false);
            File.Move(temporary, StagedFilePath, overwrite: true);
            File.WriteAllText(_versionFile, version);
            HasUpdate = false;
            LatestVersion = version;
            _crashReporter.Log($"[Renodx5AddonService.StageLocalAddon] Staged v{version} as '{AddonFileName}'");
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    // A supplied build date cannot be ordered against the mirror's unrelated semantic versions.
    // Keep it pinned until another explicit import or packaged update replaces it.
    internal static bool IsPinnedLocalBuild(string? version)
        => version != null && version.StartsWith("SF-", StringComparison.OrdinalIgnoreCase)
            && DateTime.TryParseExact(version[3..], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _);

    /// <summary>Checks GitHub for a newer version than what's currently staged.</summary>
    public async Task<bool> CheckForUpdateAsync()
    {
        if (IsStagingReady && IsPinnedLocalBuild(StagedVersion))
        {
            LatestVersion = StagedVersion;
            HasUpdate = false;
            return false;
        }
        var (version, _) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version))
        {
            _crashReporter.Log("[Renodx5AddonService.CheckForUpdateAsync] Could not resolve latest version");
            return false;
        }

        LatestVersion = version;
        var current = StagedVersion;
        HasUpdate = !string.Equals(current, version, StringComparison.OrdinalIgnoreCase);
        _crashReporter.Log($"[Renodx5AddonService.CheckForUpdateAsync] Cached={current ?? "(none)"}, Remote={version}, HasUpdate={HasUpdate}");
        return HasUpdate;
    }

    /// <summary>
    /// Ensures renodx-dlss.addon64 is staged. Downloads if not present or if an update is available.
    /// After a successful download, auto-redeploys to all game folders that already have the addon.
    /// </summary>
    public async Task EnsureStagingAsync(
        IProgress<(string message, double percent)>? progress = null,
        bool autoRedeploy = true)
    {
        if (IsStagingReady && (!HasUpdate || IsPinnedLocalBuild(StagedVersion)))
        {
            _crashReporter.Log("[Renodx5AddonService.EnsureStagingAsync] Staging already valid — skipping download");
            return;
        }

        Directory.CreateDirectory(_stagingDir);
        progress?.Report(("Preparing unified RenoDX DLSS add-on...", 10));

        var (version, downloadUrl) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(downloadUrl))
        {
            _crashReporter.Log("[Renodx5AddonService.EnsureStagingAsync] Could not resolve latest release");
            return;
        }

        progress?.Report(("Downloading unified RenoDX DLSS add-on...", 30));

        try
        {
            var destPath = Path.Combine(_stagingDir, AddonFileName);
            var bytes    = await _http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);

            // If the download is a zip, extract the addon64 from it
            if (downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var tempZip = Path.Combine(_stagingDir, "_download.zip.tmp");
                await File.WriteAllBytesAsync(tempZip, bytes).ConfigureAwait(false);
                using (var zip = System.IO.Compression.ZipFile.OpenRead(tempZip))
                {
                    var entry = zip.Entries.FirstOrDefault(e =>
                        string.Equals(e.Name, AddonFileName, StringComparison.OrdinalIgnoreCase));
                    if (entry == null)
                    {
                        _crashReporter.Log($"[Renodx5AddonService.EnsureStagingAsync] '{AddonFileName}' not found in zip");
                        File.Delete(tempZip);
                        return;
                    }
                    using var entryStream = entry.Open();
                    using var outStream   = File.Create(destPath);
                    await entryStream.CopyToAsync(outStream).ConfigureAwait(false);
                }
                File.Delete(tempZip);
            }
            else
            {
                await File.WriteAllBytesAsync(destPath, bytes).ConfigureAwait(false);
            }

            File.WriteAllText(_versionFile, version);
            HasUpdate = false;
            _crashReporter.Log($"[Renodx5AddonService.EnsureStagingAsync] Staged v{version} ({new FileInfo(destPath).Length} bytes)");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Renodx5AddonService.EnsureStagingAsync] Download failed ({downloadUrl}) — {ex.Message}");
            progress?.Report(($"RenoDX DLSS add-on download failed: {ex.Message}", 0));
            return;
        }

        progress?.Report(("RenoDX DLSS add-on ready", 90));

        // ── Auto-redeploy to all games where the addon is already present ─────
        if (autoRedeploy)
            await AutoRedeployAsync().ConfigureAwait(false);

        progress?.Report(("RenoDX DLSS add-on ready", 100));
    }

    /// <summary>
    /// Copies nvngx_dlssnr.dll to the install path root if not already present.
    /// No-op if the file already exists (preserves any version the user or Nvidia Profile section deployed).
    /// </summary>
    public async Task DeployNrDllIfAbsentAsync(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return;
        var nrDllDest = Path.Combine(installPath, "nvngx_dlssnr.dll");
        if (File.Exists(nrDllDest))
        {
            _crashReporter.Log($"[Renodx5AddonService.DeployNrDllIfAbsentAsync] nvngx_dlssnr.dll already present at '{installPath}' — skipping");
            return;
        }
        try
        {
            var cachedNr = await _dlssStreamlineService.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
            if (cachedNr != null)
            {
                File.Copy(cachedNr, nrDllDest, overwrite: false);
                _crashReporter.Log($"[Renodx5AddonService.DeployNrDllIfAbsentAsync] Deployed nvngx_dlssnr.dll to '{installPath}'");
            }
            else
            {
                _crashReporter.Log("[Renodx5AddonService.DeployNrDllIfAbsentAsync] nvngx_dlssnr.dll not available — skipping");
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Renodx5AddonService.DeployNrDllIfAbsentAsync] Failed for '{installPath}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Copies the staged addon64 to the given game install path (respects reshade.ini AddonPath).
    /// Also deploys nvngx_dlssnr.dll to the install path root if not already present.
    /// </summary>
    public async Task InstallAsync(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return;

        if (new PeHeaderService().DetectGameArchitecture(installPath) == MachineType.I386)
            throw new InvalidOperationException(
                "RenoDX DLSS has no 32-bit add-on build. Use the DLSS 5 Feeder suite, which runs RenoDX inside its 64-bit host.");

        await EnsureStagingAsync().ConfigureAwait(false);
        if (!IsStagingReady)
        {
            _crashReporter.Log("[Renodx5AddonService.InstallAsync] Staging not ready — cannot install");
            return;
        }

        try
        {
            var deployDir = ModInstallService.GetAddonDeployPath(installPath);
            Directory.CreateDirectory(deployDir);
            var src  = Path.Combine(_stagingDir, AddonFileName);
            var dest = Path.Combine(deployDir, AddonFileName);
            File.Copy(src, dest, overwrite: true);
            RetireObsoleteAddons(installPath, deployDir);
            _crashReporter.Log($"[Renodx5AddonService.InstallAsync] Deployed addon to '{deployDir}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Renodx5AddonService.InstallAsync] Addon deploy failed for '{installPath}' — {ex.Message}");
        }

        // Deploy nvngx_dlssnr.dll alongside the addon if not already present
        await DeployNrDllIfAbsentAsync(installPath).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the unified add-on from the given game install path.
    /// </summary>
    public void Uninstall(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return;
        var deployDir = ModInstallService.GetAddonDeployPath(installPath);
        var filePath  = Path.Combine(deployDir, AddonFileName);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _crashReporter.Log($"[Renodx5AddonService.Uninstall] Removed from '{deployDir}'");
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Renodx5AddonService.Uninstall] Failed for '{installPath}' — {ex.Message}");
        }
    }

    /// <summary>Returns true if the unified add-on exists at the game's deploy path.</summary>
    public bool IsInstalledIn(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return false;
        var deployDir = ModInstallService.GetAddonDeployPath(installPath);
        return File.Exists(Path.Combine(deployDir, AddonFileName));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Copies the staged file to every game folder that already has the addon present.
    /// Only overwrites — never fresh-installs to a game that doesn't have it yet.
    /// </summary>
    private async Task AutoRedeployAsync()
    {
        try
        {
            var staged = Path.Combine(_stagingDir, AddonFileName);
            if (!File.Exists(staged)) return;

            var lib = _gameLibraryService.Load();
            if (lib == null) return;

            var allGames = lib.Games
                .Concat(lib.ManualGames)
                .Where(g => !string.IsNullOrEmpty(g.InstallPath))
                .ToList();

            foreach (var game in allGames)
            {
                try
                {
                    if (new PeHeaderService().DetectGameArchitecture(game.InstallPath!) == MachineType.I386)
                    {
                        _crashReporter.Log($"[Renodx5AddonService.AutoRedeployAsync] Skipped 32-bit game '{game.Name}' — the unified add-on is 64-bit only");
                        continue;
                    }
                    var deployDir = ModInstallService.GetAddonDeployPath(game.InstallPath!);
                    var suiteRecord = Dlss5ComponentService.LoadRecord(deployDir)
                        ?? (!deployDir.Equals(game.InstallPath, StringComparison.OrdinalIgnoreCase)
                            ? Dlss5ComponentService.LoadRecord(game.InstallPath!)
                            : null);
                    if (suiteRecord != null
                        && !Dlss5ComponentService.GetCompatibilityPlan(
                                suiteRecord.Mode,
                                is64Bit: true,
                                suiteRecord.Profile)
                            .UsesExperimentalUnified)
                    {
                        _crashReporter.Log($"[Renodx5AddonService.AutoRedeployAsync] Kept compatibility-pinned RenoDX build for '{game.Name}'");
                        continue;
                    }
                    var dest      = Path.Combine(deployDir, AddonFileName);

                    // Also check the install path root in case addon was deployed there directly
                    var destRoot = Path.Combine(game.InstallPath!, AddonFileName);
                    bool existsInDeployDir = File.Exists(dest)
                        || ObsoleteAddonFileNames.Any(name => File.Exists(Path.Combine(deployDir, name)));
                    bool existsInRoot = !dest.Equals(destRoot, StringComparison.OrdinalIgnoreCase)
                        && (File.Exists(destRoot)
                            || ObsoleteAddonFileNames.Any(name => File.Exists(Path.Combine(game.InstallPath!, name))));

                    if (!existsInDeployDir && !existsInRoot) continue; // only update if already present

                    var src = Path.Combine(_stagingDir, AddonFileName);
                    if (existsInDeployDir)
                    {
                        File.Copy(src, dest, overwrite: true);
                        RetireObsoleteAddons(game.InstallPath!, deployDir);
                        _crashReporter.Log($"[Renodx5AddonService.AutoRedeployAsync] Updated '{game.Name}' at '{deployDir}'");
                    }
                    if (existsInRoot)
                    {
                        File.Copy(src, destRoot, overwrite: true);
                        RetireObsoleteAddons(game.InstallPath!, deployDir);
                        _crashReporter.Log($"[Renodx5AddonService.AutoRedeployAsync] Updated '{game.Name}' at root '{game.InstallPath}'");
                    }
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[Renodx5AddonService.AutoRedeployAsync] Failed for '{game.Name}' — {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Renodx5AddonService.AutoRedeployAsync] Loop failed — {ex.Message}");
        }

        await Task.CompletedTask;
    }

    public void RetireObsoleteAddons(string installPath, string deployDir)
    {
        var backupDirectory = Path.Combine(installPath, ".adas", "backups", "obsolete-addons");
        foreach (var obsoleteName in ObsoleteAddonFileNames)
        {
            foreach (var obsoletePath in new[]
                     {
                         Path.Combine(deployDir, obsoleteName),
                         Path.Combine(installPath, obsoleteName),
                     }.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
            {
                try
                {
                    Directory.CreateDirectory(backupDirectory);
                    var backup = Path.Combine(
                        backupDirectory,
                        $"{Path.GetFileName(obsoletePath)}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.bak");
                    File.Move(obsoletePath, backup);
                    _crashReporter.Log($"[Renodx5AddonService] Retired obsolete add-on '{obsoletePath}' to '{backup}'");
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[Renodx5AddonService] Could not retire obsolete add-on '{obsoletePath}' — {ex.Message}");
                }
            }
        }
    }

    private async Task<(string? version, string? downloadUrl)> FetchLatestReleaseInfoAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
            request.Headers.Add("User-Agent", "RHI");
            request.Headers.Add("Accept", "application/vnd.github+json");

            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _crashReporter.Log($"[Renodx5AddonService] GitHub API returned {response.StatusCode}");
                return (null, null);
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var candidates = new List<(string version, string downloadUrl, Version parsed, bool shortFuse, DateTime published)>();

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (!release.TryGetProperty("tag_name", out var tagEl)) continue;
                var tag = tagEl.GetString();
                if (tag == null || !tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                var version = tag.Substring(TagPrefix.Length);
                var shortFuse = version.StartsWith("SF-", StringComparison.OrdinalIgnoreCase);
                var numericVersion = shortFuse ? version[3..] : version;
                var published = release.TryGetProperty("published_at", out var publishedElement)
                                && DateTime.TryParse(publishedElement.GetString(), out var parsedPublished)
                    ? parsedPublished
                    : DateTime.MinValue;

                string? downloadUrl = null;
                if (release.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var assetName = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                        if (assetName == null) continue;

                        // Accept the .addon64 directly, or a zip named with the tag prefix
                        bool isAddon = string.Equals(assetName, AddonFileName, StringComparison.OrdinalIgnoreCase);
                        bool isZip   = assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                    && assetName.StartsWith("renodx-dlss", StringComparison.OrdinalIgnoreCase);

                        if ((isAddon || isZip) && asset.TryGetProperty("browser_download_url", out var urlEl))
                        {
                            downloadUrl = urlEl.GetString();
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl)) continue;

                candidates.Add(Version.TryParse(numericVersion, out var parsed)
                    ? (version, downloadUrl!, parsed, shortFuse, published)
                    : (version, downloadUrl!, new Version(0, 0), shortFuse, published));
            }

            if (candidates.Count == 0)
            {
                _crashReporter.Log("[Renodx5AddonService] No release found with renodx-dlss- tag");
                return (null, null);
            }

            // The unified package in RHI is the explicitly labelled ShortFuse
            // line. Its tags are "renodx-dlss-SF-x.y", which Version.TryParse
            // cannot read directly; without this normalization an older generic
            // snapshot could incorrectly win over the current SF release.
            var best = candidates
                .OrderByDescending(candidate => candidate.shortFuse)
                .ThenByDescending(candidate => candidate.parsed)
                .ThenByDescending(candidate => candidate.published)
                .First();
            return (best.version, best.downloadUrl);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Renodx5AddonService] FetchLatestReleaseInfo failed — {ex.Message}");
            return (null, null);
        }
    }
}
