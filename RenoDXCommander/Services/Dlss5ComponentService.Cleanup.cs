using System.Diagnostics;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public sealed record Dlss5ConflictFile(string Path, string SourcePath, string Sha256);
public sealed record Dlss5CleanupPlan(string Root, Dlss5DeploymentMode Mode, Dlss5InstallProfile Profile,
    string? RecordHash, bool RemoveRecordedInstall, bool SharedLayerReset, IReadOnlyList<Dlss5ConflictFile> Files)
{
    public bool RequiresConfirmation => SharedLayerReset || Files.Count > 0;
}

public sealed partial class Dlss5ComponentService
{
    /// <summary>Read-only preview, including conflicting originals that tracked removal would restore.</summary>
    public static Dlss5CleanupPlan GetCleanupPlan(string root, Dlss5DeploymentMode mode, Dlss5InstallProfile profile)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var record = LoadRecord(root);
        var addonRoot = ModInstallService.GetAddonDeployPath(root);
        var candidates = new[] { root, addonRoot }.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists).SelectMany(directory => Directory.EnumerateFiles(directory))
            .Concat(record?.OriginalBackups.Keys ?? Enumerable.Empty<string>())
            .Concat(new[] { Path.Combine(root, "reshade-shaders", "Shaders", FeederShader),
                Path.Combine(root, "reshade-shaders", "Shaders", AioShader) })
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var removeRecorded = record != null && (record.Profile != profile || record.Mode != mode
            || candidates.Any(path => File.Exists(path) && IsConflictingPipelineFile(path, profile)));
        string? AfterRemovalSource(string path)
        {
            if (!removeRecorded || record == null) return path;
            if (record.OriginalBackups.TryGetValue(path, out var backup)) return backup;
            return record.InstalledHashes.ContainsKey(path) ? null : path;
        }
        var files = new List<Dlss5ConflictFile>();
        foreach (var path in candidates)
        {
            var source = AfterRemovalSource(path);
            if (source == null || !File.Exists(source) || !IsConflictingPipelineFile(source, profile, Path.GetFileName(path))) continue;
            EnsureNoReparsePoints(root, path);
            EnsureNoReparsePoints(root, source);
            files.Add(new(path, source, FileHelper.ComputeSha256(source)));
        }
        var recordPath = Path.Combine(root, RecordRelativePath);
        return new(root, mode, profile, File.Exists(recordPath) ? FileHelper.ComputeSha256(recordPath) : null,
            removeRecorded, (removeRecorded || files.Count > 0) && (UsesSharedLayer(mode) || record != null && UsesSharedLayer(record.Mode)),
            files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static bool IsConflictingPipelineFile(string source, Dlss5InstallProfile selected, string? name = null)
    {
        name ??= Path.GetFileName(source);
        if (!IsOptiScalerNrProfile(selected))
        {
            if (name.Equals("nvngx.dll_dlssnr.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("OptiScaler.ini", StringComparison.OrdinalIgnoreCase)) return true;
            if (DllOverrideConstants.CommonDllNames.Contains(name, StringComparer.OrdinalIgnoreCase)
                || name.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase))
            {
                var version = FileVersionInfo.GetVersionInfo(source);
                if (version.ProductName?.Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) == true
                    || version.FileDescription?.Contains("OptiScaler", StringComparison.OrdinalIgnoreCase) == true) return true;
            }
        }
        if (selected != Dlss5InstallProfile.StandaloneAio)
        {
            if (name.Equals(AioAddon, StringComparison.OrdinalIgnoreCase) || name.Equals(AioShader, StringComparison.OrdinalIgnoreCase)) return true;
            // nvngx.dll can also be a legitimate game runtime. Only retire our verified AIO helper.
            if (name.Equals("nvngx.dll", StringComparison.OrdinalIgnoreCase)
                && FileHelper.ComputeSha256(source).Equals(AioAssetHashes["nvngx.dll"], StringComparison.OrdinalIgnoreCase)) return true;
        }
        if (IsOptiScalerNrProfile(selected) || selected == Dlss5InstallProfile.StandaloneAio)
            return (IsManagedDlssAddonReference(name) && Path.GetExtension(name).ToLowerInvariant() is ".addon64" or ".addon32")
                || name.Equals("deep-fried-chicken.addon64", StringComparison.OrdinalIgnoreCase)
                || name.Equals(FeederConfig, StringComparison.OrdinalIgnoreCase)
                || name.Equals(BridgeConfig, StringComparison.OrdinalIgnoreCase)
                || name.Equals(ObsoleteBridgeConfig, StringComparison.OrdinalIgnoreCase)
                || name.Equals(FeederShader, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    internal static void ValidateCleanupApproval(Dlss5CleanupPlan current, Dlss5CleanupPlan? approved)
    {
        if (!current.RequiresConfirmation) return;
        if (approved == null || current.Root != approved.Root || current.Mode != approved.Mode
            || current.Profile != approved.Profile || current.RecordHash != approved.RecordHash
            || current.SharedLayerReset != approved.SharedLayerReset || current.RemoveRecordedInstall != approved.RemoveRecordedInstall
            || !current.Files.SequenceEqual(approved.Files))
            throw new InvalidOperationException("Conflicting components need approval, or changed since review. Use Review / Install to confirm automatic cleanup. No files were changed.");
    }

    internal static IReadOnlyList<string> ArchiveConfirmedConflicts(Dlss5CleanupPlan plan)
    {
        // After tracked removal, originals from its backups are now at their reviewed destinations.
        // Validate the entire set before moving anything; never replace consent with a wildcard delete.
        foreach (var file in plan.Files)
        {
            EnsureNoReparsePoints(plan.Root, file.Path);
            if (!File.Exists(file.Path) || !FileHelper.ComputeSha256(file.Path).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("A conflicting file changed after review. Close the game and review the setup again.");
            using var access = new FileStream(file.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        return plan.Files.Select(file => PreserveModifiedFile(plan.Root, file.Path)).ToArray();
    }
}
