namespace RenoDXCommander.Services;

/// <summary>
/// Interface for the MFG Ada Unlock component service. Handles staging, install,
/// uninstall, update detection, and ReShade.ini configuration for the
/// <c>renodx-mfgunlock.addon64</c> ReShade add-on, which unlocks DLSS Multi Frame
/// Generation (3x/4x/6x) on GeForce RTX 40-series (Ada) GPUs.
///
/// The add-on is a MIT-licensed fork of clshortfuse/renodx by mavismmg; Adas
/// downloads the prebuilt release asset at runtime and never redistributes it or
/// any NVIDIA runtime file.
/// </summary>
public interface IMfgUnlockService
{
    /// <summary>The add-on filename deployed to game folders.</summary>
    static abstract string FileName { get; }

    /// <summary>The currently staged version (from version.txt), or null if not staged.</summary>
    string? StagedVersion { get; }

    /// <summary>Whether the staging directory has the add-on ready for deployment.</summary>
    bool IsStagingReady { get; }

    /// <summary>Whether an update is available (set after CheckForUpdateAsync).</summary>
    bool HasUpdate { get; }

    /// <summary>The latest remote version tag (set after CheckForUpdateAsync).</summary>
    string? LatestVersion { get; }

    /// <summary>The release body/notes from the latest version.</summary>
    string? ReleaseNotes { get; }

    /// <summary>Optional manifest URL override (supports a {version} placeholder).</summary>
    string? ManifestUrlOverride { get; set; }

    /// <summary>Ensures the add-on is staged (downloaded). Downloads if missing or if an update is available.</summary>
    Task EnsureStagingAsync(IProgress<(string message, double percent)>? progress = null);

    /// <summary>Checks GitHub for a newer version than what's currently staged.</summary>
    Task CheckForUpdateAsync();

    /// <summary>Installs the add-on to a game folder (and writes default config if absent).</summary>
    Task<bool> InstallAsync(string installPath, IProgress<(string message, double percent)>? progress = null);

    /// <summary>Uninstalls the add-on from a game folder.</summary>
    bool Uninstall(string installPath);

    /// <summary>Detects whether the add-on is deployed in a game folder.</summary>
    bool IsInstalledIn(string installPath);

    /// <summary>Returns the release page URL for a given version tag.</summary>
    string GetReleaseUrl(string version);

    /// <summary>Reads the game's <c>[RenoDX.MFGUnlock]</c> configuration from its reshade.ini.</summary>
    MfgUnlockConfig ReadConfig(string installPath);

    /// <summary>Writes the <c>[RenoDX.MFGUnlock]</c> configuration into the game's reshade.ini.</summary>
    void WriteConfig(string installPath, MfgUnlockConfig config);
}
