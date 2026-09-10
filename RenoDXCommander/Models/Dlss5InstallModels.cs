namespace RenoDXCommander.Models;

public sealed record Dlss5InstallResult(
    bool Succeeded,
    Dlss5DeploymentMode Mode,
    string DeploymentPath,
    IReadOnlyList<string> InstalledFiles,
    IReadOnlyList<string> Warnings,
    string Message);

internal sealed class Dlss5InstallRecord
{
    public Dlss5DeploymentMode Mode { get; set; }
    public Dlss5InstallProfile Profile { get; set; } = Dlss5InstallProfile.MaximumQuality;

    /// <summary>
    /// True when Deep Fried Chicken was deployed as the neural consumer in place of RenoDX.
    /// Verification uses this to require the DFC files instead of renodx-dlss5.addon64.
    /// </summary>
    public bool DeepFriedChicken { get; set; }
    public string? ComponentVersion { get; set; }
    public DateTime InstalledAtUtc { get; set; }
    public Dictionary<string, string> InstalledHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> OriginalBackups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> LegacyLaunchPadBackups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Dlss5IniSettingBackup> IniSettingBackups { get; set; } = new();
    public bool UnifiedRenoDxSettingsMigrated { get; set; }
    public bool PreferDxvkForDirectX9 { get; set; }
    public DateTime? Dx9FallbackDetectedAtUtc { get; set; }

    /// <summary>
    /// Full paths of the executables Adas pinned to the high-performance GPU via
    /// <see cref="RenoDXCommander.Services.GpuPreferenceService"/> during install (the game exe, plus
    /// the host64 Feeder helper on 32-bit routes). Uninstall clears exactly these entries so a
    /// hybrid-GPU preference the user set by hand is never removed.
    /// </summary>
    public List<string> GpuPreferenceExes { get; set; } = new();
}

internal sealed class Dlss5IniSettingBackup
{
    public string Path { get; set; } = "";
    public string Section { get; set; } = "";
    public string Key { get; set; } = "";
    public bool Existed { get; set; }
    public string? OriginalKey { get; set; }
    public string? OriginalValue { get; set; }
    public string InstalledValue { get; set; } = "";
}
