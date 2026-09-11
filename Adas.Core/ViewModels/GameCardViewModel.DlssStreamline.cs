using CommunityToolkit.Mvvm.ComponentModel;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

// DLSS & Streamline detection state stored per-game card
public partial class GameCardViewModel
{
    // ── DLSS/Streamline detection result (set during scan) ────────────────────

    /// <summary>Full detection result from the last scan. Null if not yet scanned.</summary>
    public DlssDetectionResult? DlssDetection { get; set; }

    // ── Installed versions (display) ──────────────────────────────────────────

    [ObservableProperty] private string? _dlssInstalledVersion;
    [ObservableProperty] private string? _dlssdInstalledVersion;
    [ObservableProperty] private string? _dlssgInstalledVersion;
    [ObservableProperty] private string? _dlssnrInstalledVersion;
    [ObservableProperty] private string? _streamlineInstalledVersion;

    // ── Whether each component is present in the game ─────────────────────────

    public bool HasDlss => DlssDetection?.DlssPath != null;
    public bool HasDlssd => DlssDetection?.DlssdPath != null;
    public bool HasDlssg => DlssDetection?.DlssgPath != null;
    public bool HasDlssnr => DlssDetection?.DlssnrPath != null;
    public bool HasStreamline => DlssDetection?.StreamlineInterposerPath != null;
    public bool HasAnyDlssStreamline => DlssDetection?.HasAny ?? false;

    // ── Whether backups exist (indicates a swap was done) ─────────────────────

    public bool DlssHasBackup => DlssDetection?.DlssPath != null && File.Exists(DlssDetection.DlssPath + ".original");
    public bool DlssdHasBackup => DlssDetection?.DlssdPath != null && File.Exists(DlssDetection.DlssdPath + ".original");
    public bool DlssgHasBackup => DlssDetection?.DlssgPath != null && File.Exists(DlssDetection.DlssgPath + ".original");
    public bool DlssnrHasBackup => DlssDetection?.DlssnrPath != null && File.Exists(DlssDetection.DlssnrPath + ".original");
    public bool StreamlineHasBackup => DlssDetection?.StreamlineFolder != null
        && Directory.Exists(DlssDetection.StreamlineFolder)
        && Directory.EnumerateFiles(DlssDetection.StreamlineFolder, "*.original").Any();

    public bool HasAnyDlssBackup => DlssHasBackup || DlssdHasBackup || DlssgHasBackup || DlssnrHasBackup || StreamlineHasBackup;

    // ── Refresh detection state ───────────────────────────────────────────────

    /// <summary>
    /// Updates the card's DLSS/Streamline properties from a fresh detection result.
    /// </summary>
    public void ApplyDlssDetection(DlssDetectionResult detection)
    {
        DlssDetection = detection;

        DlssInstalledVersion = detection.DlssVersion != null
            ? DlssStreamlineService.FormatVersion(detection.DlssVersion) : null;
        DlssdInstalledVersion = detection.DlssdVersion != null
            ? DlssStreamlineService.FormatVersion(detection.DlssdVersion) : null;
        DlssgInstalledVersion = detection.DlssgVersion != null
            ? DlssStreamlineService.FormatVersion(detection.DlssgVersion) : null;
        DlssnrInstalledVersion = detection.DlssnrVersion != null
            ? DlssStreamlineService.FormatVersion(detection.DlssnrVersion) : null;

        // Streamline: when custom marker is active, prefer sl.common.dll version
        // (custom folder may only update sl.common.dll, leaving sl.interposer.dll at old version)
        if (detection.StreamlineVersion != null && detection.StreamlineFolder != null
            && DlssStreamlineService.IsCustomStreamlineActive(detection.StreamlineFolder))
        {
            var commonPath = Path.Combine(detection.StreamlineFolder, "sl.common.dll");
            if (File.Exists(commonPath))
            {
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(commonPath);
                var commonVer = DlssStreamlineService.FormatVersion(
                    $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}");
                StreamlineInstalledVersion = commonVer;
                detection.StreamlineInterposerPath = commonPath; // update for future reads
            }
            else
                StreamlineInstalledVersion = DlssStreamlineService.FormatVersion(detection.StreamlineVersion);
        }
        else
        {
            StreamlineInstalledVersion = detection.StreamlineVersion != null
                ? DlssStreamlineService.FormatVersion(detection.StreamlineVersion) : null;
        }

        NotifyDlssStreamlineDependents();
    }

    /// <summary>
    /// Re-reads versions from disk and updates the card properties.
    /// Call after a swap or restore operation.
    /// </summary>
    public void RefreshDlssVersions(IDlssStreamlineService service)
    {
        if (DlssDetection == null) return;

        if (DlssDetection.DlssPath != null)
            DlssInstalledVersion = DlssStreamlineService.FormatVersion(service.GetFileVersion(DlssDetection.DlssPath));
        if (DlssDetection.DlssdPath != null)
            DlssdInstalledVersion = DlssStreamlineService.FormatVersion(service.GetFileVersion(DlssDetection.DlssdPath));
        if (DlssDetection.DlssgPath != null)
            DlssgInstalledVersion = DlssStreamlineService.FormatVersion(service.GetFileVersion(DlssDetection.DlssgPath));
        if (DlssDetection.DlssnrPath != null)
            DlssnrInstalledVersion = DlssStreamlineService.FormatVersion(service.GetFileVersion(DlssDetection.DlssnrPath));
        if (DlssDetection.StreamlineInterposerPath != null || DlssDetection.StreamlineFolder != null)
        {
            var folder = DlssDetection.StreamlineFolder
                ?? System.IO.Path.GetDirectoryName(DlssDetection.StreamlineInterposerPath);

            string? versionFromPath = null;

            if (folder != null && DlssStreamlineService.IsCustomStreamlineActive(folder))
            {
                // Custom swap active — prefer sl.common.dll since it's most reliably updated
                // (custom folder may not include sl.interposer.dll)
                var commonPath = System.IO.Path.Combine(folder, "sl.common.dll");
                if (System.IO.File.Exists(commonPath))
                {
                    versionFromPath = DlssStreamlineService.FormatVersion(service.GetFileVersion(commonPath));
                    DlssDetection.StreamlineInterposerPath = commonPath;
                }
            }

            // Fallback: read from interposer path (normal versioned installs)
            if (string.IsNullOrEmpty(versionFromPath) || versionFromPath == "Unknown")
            {
                if (DlssDetection.StreamlineInterposerPath != null)
                    versionFromPath = DlssStreamlineService.FormatVersion(service.GetFileVersion(DlssDetection.StreamlineInterposerPath));
            }

            // If the interposer is older than another DLL in the same folder
            // (e.g. 2.12.128 interposer in a 2.12.129 release), use the highest-versioned DLL.
            if (!string.IsNullOrEmpty(versionFromPath) && versionFromPath != "Unknown" && folder != null)
            {
                foreach (var knownDll in DlssStreamlineService.KnownStreamlineDlls)
                {
                    if (string.Equals(knownDll, "sl.interposer.dll", StringComparison.OrdinalIgnoreCase)) continue;
                    var candidatePath = System.IO.Path.Combine(folder, knownDll);
                    if (!System.IO.File.Exists(candidatePath)) continue;
                    var candidateVersion = DlssStreamlineService.FormatVersion(service.GetFileVersion(candidatePath));
                    if (!string.IsNullOrEmpty(candidateVersion) && candidateVersion != "Unknown"
                        && System.Version.TryParse(candidateVersion, out var cv)
                        && System.Version.TryParse(versionFromPath, out var iv) && cv > iv)
                    {
                        versionFromPath = candidateVersion;
                        DlssDetection.StreamlineInterposerPath = candidatePath;
                    }
                }
            }

            // Last resort: try sl.common.dll even without custom marker
            if ((string.IsNullOrEmpty(versionFromPath) || versionFromPath == "Unknown") && folder != null)
            {
                var commonPath = System.IO.Path.Combine(folder, "sl.common.dll");
                if (System.IO.File.Exists(commonPath))
                {
                    versionFromPath = DlssStreamlineService.FormatVersion(service.GetFileVersion(commonPath));
                    DlssDetection.StreamlineInterposerPath = commonPath;
                }
            }

            StreamlineInstalledVersion = versionFromPath;
        }

        NotifyDlssStreamlineDependents();
    }

    private void NotifyDlssStreamlineDependents()
    {
        OnPropertyChanged(nameof(HasDlss));
        OnPropertyChanged(nameof(HasDlssd));
        OnPropertyChanged(nameof(HasDlssg));
        OnPropertyChanged(nameof(HasDlssnr));
        OnPropertyChanged(nameof(HasStreamline));
        OnPropertyChanged(nameof(HasAnyDlssStreamline));
        OnPropertyChanged(nameof(DlssHasBackup));
        OnPropertyChanged(nameof(DlssdHasBackup));
        OnPropertyChanged(nameof(DlssgHasBackup));
        OnPropertyChanged(nameof(DlssnrHasBackup));
        OnPropertyChanged(nameof(StreamlineHasBackup));
        OnPropertyChanged(nameof(HasAnyDlssBackup));
    }
}
