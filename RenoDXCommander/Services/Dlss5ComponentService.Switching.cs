using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public sealed partial class Dlss5ComponentService
{
    private static readonly SemaphoreSlim InstallationLock = new(1, 1);
    private sealed record ProfileSetting(string Target, string Section, string Key, string Text);

    internal static bool RequiresRestoreFirst(Dlss5DeploymentMode oldMode, Dlss5DeploymentMode newMode,
        Dlss5InstallProfile oldProfile, Dlss5InstallProfile newProfile)
        => (oldMode != newMode || oldProfile != newProfile) && (UsesSharedLayer(oldMode) || UsesSharedLayer(newMode));

    private static bool UsesSharedLayer(Dlss5DeploymentMode mode)
        => mode is Dlss5DeploymentMode.NativeVulkan or Dlss5DeploymentMode.VulkanFeeder
            or Dlss5DeploymentMode.Dx10ViaDxvkFeeder or Dlss5DeploymentMode.Dx9ViaDxvkFeeder;

    public async Task<Dlss5InstallResult> InstallAsync(string gameName, Dlss5Assessment assessment,
        IProgress<(string message, double percent)>? progress = null, CancellationToken cancellationToken = default,
        string? reShadeChannel = null, string? store = null, Dlss5InstallProfile profile = Dlss5InstallProfile.MaximumQuality,
        Dlss5CleanupPlan? cleanupApproval = null)
    {
        if (!assessment.CanInstall || string.IsNullOrWhiteSpace(assessment.DeploymentPath))
            throw new InvalidOperationException(string.Join(Environment.NewLine, assessment.BlockingReasons));
        await InstallationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = assessment.DeploymentPath;
            Dlss5RuntimePrerequisites.EnsureAvailable(root, assessment.Is64Bit);
            if (Dlss5SwitchJournal.Recover(root))
                throw new InvalidOperationException("Recovered the previous interrupted switch. Its files and settings are restored. Review the game again before applying another profile.");
            var previous = LoadRecord(root);
            var cleanup = GetCleanupPlan(root, assessment.Mode, profile);
            ValidateCleanupApproval(cleanup, cleanupApproval);
            var switching = cleanup.RemoveRecordedInstall || cleanup.Files.Count > 0;
            if (!switching)
            {
                var result = await InstallCoreAsync(gameName, assessment, progress, cancellationToken, reShadeChannel, store, profile).ConfigureAwait(false);
                if (previous == null) RestoreProfileSettings(root);
                return result;
            }

            progress?.Report(("Saving the current profile and preparing recovery...", 1));
            if (previous != null) SaveProfileSettings(root, previous);
            Dlss5SwitchJournal? journal = new(root);
            try
            {
                journal.Capture(Path.Combine(root, RecordRelativePath));
                foreach (var path in previous == null ? Enumerable.Empty<string>() : previous.InstalledHashes.Keys.Concat(previous.OriginalBackups.Keys)
                    .Concat(previous.OriginalBackups.Values.OfType<string>())
                    .Concat(previous.LegacyLaunchPadBackups.Keys).Concat(previous.LegacyLaunchPadBackups.Values)
                    .Concat(previous.IniSettingBackups.Select(setting => setting.Path)).Distinct(StringComparer.OrdinalIgnoreCase))
                    journal.Capture(path);
                cancellationToken.ThrowIfCancellationRequested();
                var errors = UninstallTrackedFiles(root, _crashReporter);
                if (errors.Count > 0) throw new IOException("The current profile could not be removed: " + string.Join("; ", errors));
                progress?.Report(("Removing confirmed conflicts and keeping recovery copies...", 4));
                var archived = ArchiveConfirmedConflicts(cleanup);
                foreach (var file in archived) _crashReporter.Log($"[DLSS cleanup] Preserved conflicting component: {file}");

                if (cleanup.SharedLayerReset || UsesSharedLayer(assessment.Mode))
                {
                    // Removal is transactional and complete before any shared-layer installation.
                    // Do not pretend that a game-local journal can undo machine-wide registration.
                    journal.Commit();
                    journal.Dispose();
                    journal = null;
                }

                // The pre-switch assessment saw the old profile's files. Recheck installation requirements
                // after removing them; never reuse their presence as proof that the new route is complete.
                var requirements = assessment.MissingRequirements.ToList();
                var runtimeRoot = assessment.Is64Bit ? root : Path.Combine(root, "host64");
                foreach (var name in new[] { "nvngx_dlss.dll", "nvngx_dlssnr.dll" })
                    if (!IsUsableRuntimeFile(Path.Combine(runtimeRoot, name))) requirements.Add(name);
                if (!File.Exists(Path.Combine(root, GetReShadeFileName(assessment.Mode, profile)))) requirements.Add("ReShade full add-on support");
                assessment = assessment with { MissingRequirements = requirements };
                var result = await InstallCoreAsync(gameName, assessment, progress, cancellationToken, reShadeChannel, store, profile).ConfigureAwait(false);
                RestoreProfileSettings(root);
                var issues = Dlss5DiagnosticService.VerifyInstallation(root, assessment.Mode, assessment.Is64Bit);
                if (issues.Count > 0) throw new IOException(string.Join("; ", issues));
                journal?.Commit();
                return result with { Message = "Previous components were removed automatically. Conflicting files are saved in .adas\\preserved. " + result.Message };
            }
            catch (Exception failure)
            {
                if (journal == null)
                    throw new IOException("The previous components were removed, but the new setup could not finish. Run Review / Repair to continue. Shared Vulkan-layer changes are not automatically rolled back. " + failure.Message, failure);
                try { journal.Rollback(); }
                catch (Exception recovery)
                {
                    throw new IOException("The switch failed and recovery needs attention. Close the game, then run Repair to retry recovery. Keep .adas\\switch-recovery intact. " + recovery.Message, failure);
                }
                throw new IOException("The switch failed. The previous profile's files and settings were restored. " + failure.Message, failure);
            }
            finally { journal?.Dispose(); }
        }
        finally { InstallationLock.Release(); }
    }

    private void SaveInstalledProfileSettings(string root)
    {
        try { if (LoadRecord(root) is { } record) SaveProfileSettings(root, record); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { _crashReporter.Log("[DLSS profiles] Could not save settings before removal: " + ex.Message); }
    }

    private static string ProfilePath(string root, Dlss5InstallRecord record)
        => Dlss5SwitchJournal.Resolve(root, $".adas/profiles/{record.Mode}-{record.Profile}.json");

    private static string SettingsTarget(string root, string target)
    {
        if (target == "preset")
        {
            var ini = IniTextDocument.Load(Path.Combine(root, "ReShade.ini"));
            if (ini.TryGetValue("GENERAL", "PresetPath", out var preset))
            {
                var path = preset.Text.Trim().Trim('"');
                if (path.Length > 0)
                {
                    var relative = Path.IsPathRooted(path) ? Path.GetRelativePath(root, path) : path;
                    return Dlss5SwitchJournal.Resolve(root, relative);
                }
            }
            return Path.Combine(root, "ReShadePreset.ini");
        }
        if (target is not ("ReShade.ini" or "host64/ReShade.ini" or FeederConfig or BridgeConfig or "OptiScaler.ini"))
            throw new InvalidDataException("Invalid profile settings target.");
        return Dlss5SwitchJournal.Resolve(root, target);
    }

    private static bool IsVisualSetting(ProfileSetting setting, Dlss5InstallRecord record)
    {
        if (setting.Key.Contains('\n') || setting.Key.Contains('\r') || setting.Text.Contains('\n') || setting.Text.Contains('\r')) return false;
        return setting.Target switch
        {
            "ReShade.ini" or "host64/ReShade.ini" => setting.Section is "RenoDX.DLSS5" or "RENODX-DLSS" or AioSection
                && setting.Key is not ("EnableHooks" or "HookPoint" or "ManuallyLoadDlssLibraries"),
            "preset" => setting.Section.EndsWith(".fx", StringComparison.OrdinalIgnoreCase)
                && setting.Key != "PreprocessorDefinitions",
            FeederConfig => GetDefaults(record.Mode, record.Profile).ContainsKey(setting.Key),
            BridgeConfig => NativeVulkanBridgeDefaults.ContainsKey(setting.Key),
            "OptiScaler.ini" => setting.Section == "DlssNr" && setting.Key is not ("SplitPipeline" or "AutoCapture"),
            _ => false,
        };
    }

    internal static void SaveProfileSettings(string root, Dlss5InstallRecord record)
    {
        var settings = new List<ProfileSetting>();
        foreach (var target in new[] { "ReShade.ini", "host64/ReShade.ini", "preset", FeederConfig, BridgeConfig, "OptiScaler.ini" })
        {
            string path;
            try { path = SettingsTarget(root, target); }
            catch (InvalidDataException) { continue; } // Never follow an external preset into another game.
            if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) continue;
            settings.AddRange(IniTextDocument.Load(path).Values().Select(value => new ProfileSetting(target, value.Section, value.Key, value.Text))
                .Where(setting => IsVisualSetting(setting, record)));
        }
        var destination = ProfilePath(root, record);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        WriteTextAtomically(destination, JsonSerializer.Serialize(settings));
    }

    internal static void RestoreProfileSettings(string root)
    {
        var record = LoadRecord(root);
        if (record == null) return;
        var path = ProfilePath(root, record);
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("The saved profile is too large.");
        var values = JsonSerializer.Deserialize<List<ProfileSetting>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The saved visual settings are unreadable.");
        foreach (var group in values.Where(setting => IsVisualSetting(setting, record)).GroupBy(setting => setting.Target))
        {
            var target = SettingsTarget(root, group.Key);
            if (!File.Exists(target)) continue; // Only settings for components present in this profile.
            if (group.Key == "ReShade.ini")
            {
                foreach (var value in group) SetTrackedIniValue(root, record, target, value.Section, value.Key, value.Text);
            }
            else
            {
                // These files already use whole-file ownership. Merge once, preserving the freshly
                // installed provider/hook settings, then use the normal tracked replacement path.
                var document = IniTextDocument.Load(target);
                foreach (var value in group) document.SetValue(value.Section, value.Key, value.Text);
                var temporary = Path.Combine(Path.GetTempPath(), $"adas-profile-{Guid.NewGuid():N}.ini");
                try { document.Save(temporary); InstallTrackedFile(temporary, target, root, record); }
                finally { DeleteIfExists(temporary); }
            }
            if (record.InstalledHashes.ContainsKey(target)) record.InstalledHashes[target] = FileHelper.ComputeSha256(target);
        }
        SaveRecord(root, record);
    }
}
