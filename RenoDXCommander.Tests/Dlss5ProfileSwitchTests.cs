using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class Dlss5ProfileSwitchTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("adas-profile-test-").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void OrphanConflictsArePreviewedBeforeAnyChangesAndNeedApproval()
    {
        var marker = Path.Combine(_root, "nvngx.dll_dlssnr.dll");
        File.WriteAllText(marker, "leftover");
        File.WriteAllText(Path.Combine(_root, "nvngx_dlss.dll"), "native runtime");
        File.WriteAllText(Path.Combine(_root, "dxgi.dll"), "unknown wrapper");
        var plan = Dlss5ComponentService.GetCleanupPlan(_root, Dlss5DeploymentMode.NativeDirectX12, Dlss5InstallProfile.MaximumQuality);
        Assert.True(plan.RequiresConfirmation);
        Assert.False(plan.RemoveRecordedInstall);
        Assert.Equal(marker, Assert.Single(plan.Files).Path);
        Assert.Throws<InvalidOperationException>(() => Dlss5ComponentService.ValidateCleanupApproval(plan, null));
        Assert.Equal("leftover", File.ReadAllText(marker));
        Dlss5ComponentService.ValidateCleanupApproval(plan, plan);
    }

    [Fact]
    public void ChangedOrNewConflictsRequireFreshApproval()
    {
        var marker = Path.Combine(_root, "nvngx.dll_dlssnr.dll");
        File.WriteAllText(marker, "before");
        var approved = Dlss5ComponentService.GetCleanupPlan(_root, Dlss5DeploymentMode.NativeDirectX12, Dlss5InstallProfile.MaximumQuality);
        File.WriteAllText(marker, "changed");
        var changed = Dlss5ComponentService.GetCleanupPlan(_root, approved.Mode, approved.Profile);
        Assert.Throws<InvalidOperationException>(() => Dlss5ComponentService.ValidateCleanupApproval(changed, approved));
        File.WriteAllText(Path.Combine(_root, "OptiScaler.ini"), "[DlssNr]");
        var added = Dlss5ComponentService.GetCleanupPlan(_root, approved.Mode, approved.Profile);
        Assert.Throws<InvalidOperationException>(() => Dlss5ComponentService.ValidateCleanupApproval(added, changed));
        Assert.Throws<IOException>(() => Dlss5ComponentService.ArchiveConfirmedConflicts(approved));
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public void ConfirmedOrphanCleanupIsReversibleIfInstallationFails()
    {
        var marker = Path.Combine(_root, "nvngx.dll_dlssnr.dll");
        File.WriteAllText(marker, "old NR hook");
        var plan = Dlss5ComponentService.GetCleanupPlan(_root, Dlss5DeploymentMode.NativeDirectX12, Dlss5InstallProfile.MaximumQuality);
        using var journal = new Dlss5SwitchJournal(_root);
        var backup = Assert.Single(Dlss5ComponentService.ArchiveConfirmedConflicts(plan));
        Assert.False(File.Exists(marker));
        Assert.Equal("old NR hook", File.ReadAllText(backup));
        journal.Rollback();
        Assert.Equal("old NR hook", File.ReadAllText(marker));
        Assert.False(File.Exists(backup));
    }

    [Fact]
    public void CleanupPreviewIncludesConflictingOriginalThatRemovalWillRestore()
    {
        var marker = Path.Combine(_root, "nvngx.dll_dlssnr.dll");
        var incoming = Path.Combine(_root, "incoming.bin");
        File.WriteAllText(marker, "original hook");
        File.WriteAllText(incoming, "installed hook");
        var record = new Dlss5InstallRecord { Mode = Dlss5DeploymentMode.NativeDirectX12, Profile = Dlss5InstallProfile.OptiScalerNeuralRendering };
        Dlss5ComponentService.InstallTrackedFile(incoming, marker, _root, record);
        var plan = Dlss5ComponentService.GetCleanupPlan(_root, record.Mode, Dlss5InstallProfile.MaximumQuality);
        Assert.True(plan.RemoveRecordedInstall);
        Assert.Equal(record.OriginalBackups[marker], Assert.Single(plan.Files).SourcePath);
        Assert.Empty(Dlss5ComponentService.UninstallTrackedFiles(_root, new Reporter()));
        var archived = Assert.Single(Dlss5ComponentService.ArchiveConfirmedConflicts(plan));
        Assert.Equal("original hook", File.ReadAllText(archived));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void OrphanVulkanCleanupAlsoDisclosesSharedLayerChanges()
    {
        File.WriteAllText(Path.Combine(_root, "nvngx.dll_dlssnr.dll"), "old hook");
        var plan = Dlss5ComponentService.GetCleanupPlan(_root, Dlss5DeploymentMode.VulkanFeeder, Dlss5InstallProfile.MaximumQuality);
        Assert.True(plan.SharedLayerReset);
        Assert.True(plan.RequiresConfirmation);
    }

    [Theory]
    [InlineData(Dlss5InstallProfile.StandaloneAio)]
    [InlineData(Dlss5InstallProfile.OptiScalerNeuralRendering)]
    [InlineData(Dlss5InstallProfile.OptiScalerNrBeforeSr)]
    public void AlternativePipelinesPreviewFeederRemovalButPreserveUnrelatedGameMod(Dlss5InstallProfile profile)
    {
        var feeder = Path.Combine(_root, Dlss5ComponentService.FeederAddon);
        File.WriteAllText(feeder, "feeder");
        File.WriteAllText(Path.Combine(_root, "renodx-upgrade.addon64"), "game HDR mod");
        var plan = Dlss5ComponentService.GetCleanupPlan(_root, Dlss5DeploymentMode.NativeDirectX12, profile);
        Assert.Equal(feeder, Assert.Single(plan.Files).Path);
        Assert.True(plan.RequiresConfirmation);
    }

    [Fact]
    public void SharedVulkanRemovalCanBeApprovedInsteadOfRequiringManualRemoval()
    {
        var record = new Dlss5InstallRecord { Mode = Dlss5DeploymentMode.VulkanFeeder };
        Dlss5ComponentService.SaveRecord(_root, record);
        var plan = Dlss5ComponentService.GetCleanupPlan(_root, record.Mode, Dlss5InstallProfile.LatestFeederBeta);
        Assert.True(plan.SharedLayerReset);
        Assert.True(plan.RemoveRecordedInstall);
        Assert.True(plan.RequiresConfirmation);
        Assert.Throws<InvalidOperationException>(() => Dlss5ComponentService.ValidateCleanupApproval(plan, null));
        Dlss5ComponentService.ValidateCleanupApproval(plan, plan);
    }

    [Fact]
    public void UninstallClearsOrphanOptiNrMarkerWithoutRemovingNativeDlssOrGameMods()
    {
        var marker = Path.Combine(_root, "nvngx.dll_dlssnr.dll");
        var native = Path.Combine(_root, "nvngx_dlss.dll");
        var gameMod = Path.Combine(_root, "renodx-upgrade.addon64");
        File.WriteAllText(marker, "leftover NR hook");
        File.WriteAllText(native, "game runtime");
        File.WriteAllText(gameMod, "game HDR mod");
        var service = new Dlss5ComponentService(null!, new Reporter(), null!, null!, null!, null!);
        Assert.Null(Dlss5ComponentService.LoadRecord(_root));
        Assert.Empty(service.Uninstall(_root));
        Assert.False(File.Exists(marker));
        Assert.Equal("game runtime", File.ReadAllText(native));
        Assert.Equal("game HDR mod", File.ReadAllText(gameMod));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, ".adas", "preserved"), "nvngx.dll_dlssnr.dll.*.modified"));
    }

    [Fact]
    public void FailedSwitchRestoresFilesConsumedBackupsSettingsAndOwnership()
    {
        var original = Path.Combine(_root, "dxgi.dll");
        var staging = Path.Combine(_root, "incoming.dll");
        File.WriteAllText(original, "original wrapper");
        File.WriteAllText(staging, "first profile");
        var record = new Dlss5InstallRecord { Mode = Dlss5DeploymentMode.NativeDirectX12 };
        Dlss5ComponentService.InstallTrackedFile(staging, original, _root, record);
        var backup = record.OriginalBackups[original]!;
        var config = Path.Combine(_root, "ReShade.ini");
        File.WriteAllText(config, "[RenoDX.DLSS5]\nNeuralUplift=1\nNRIntensity=0.6\n");
        using (var journal = new Dlss5SwitchJournal(_root))
        {
            journal.Capture(Path.Combine(_root, ".adas", "dlss5-install.json"));
            journal.Capture(original);
            journal.Capture(backup);
            Assert.Empty(Dlss5ComponentService.UninstallTrackedFiles(_root, new Reporter()));
            File.WriteAllText(staging, "second profile");
            Dlss5ComponentService.InstallTrackedFile(staging, original, _root, new());
            Dlss5ComponentService.InstallTrackedFile(staging, Path.Combine(_root, "new.addon64"), _root, new());
            var ini = IniTextDocument.Load(config);
            ini.SetValue("RenoDX.DLSS5", "NRIntensity", "0.2");
            ini.Save(config);
            journal.Rollback();
        }
        Assert.Equal("first profile", File.ReadAllText(original));
        Assert.Equal("original wrapper", File.ReadAllText(backup));
        Assert.Contains("NRIntensity=0.6", File.ReadAllText(config));
        Assert.False(File.Exists(Path.Combine(_root, "new.addon64")));
        Assert.Equal(record.InstalledHashes[original], Dlss5ComponentService.LoadRecord(_root)!.InstalledHashes[original]);
        Assert.False(Dlss5SwitchJournal.Recover(_root));
    }

    [Fact]
    public void CommittedSwitchKeepsNewFilesAndDoesNotRecoverOldProfile()
    {
        var file = Path.Combine(_root, "dxgi.dll");
        File.WriteAllText(file, "before");
        using (var journal = new Dlss5SwitchJournal(_root))
        {
            journal.Capture(file);
            File.WriteAllText(file, "after");
            journal.Commit();
        }
        Assert.False(Dlss5SwitchJournal.Recover(_root));
        Assert.Equal("after", File.ReadAllText(file));
    }

    [Fact]
    public void InterruptedSwitchRecoversOnNextRequestAndRejectsEscapingPaths()
    {
        var file = Path.Combine(_root, "dxgi.dll");
        File.WriteAllText(file, "before");
        using (var journal = new Dlss5SwitchJournal(_root))
        {
            journal.Capture(file);
            Assert.Throws<InvalidDataException>(() => journal.Capture(Path.Combine(_root, "..", "outside.dll")));
            File.WriteAllText(file, "after");
        }
        Assert.True(Dlss5SwitchJournal.Recover(_root));
        Assert.Equal("before", File.ReadAllText(file));
    }

    [Fact]
    public void MissingSnapshotStopsRecoveryBeforeAnyWritesAndKeepsJournal()
    {
        var file = Path.Combine(_root, "dxgi.dll");
        File.WriteAllText(file, "before");
        using (var journal = new Dlss5SwitchJournal(_root)) { journal.Capture(file); File.WriteAllText(file, "after"); }
        File.Delete(Path.Combine(_root, ".adas", "switch-recovery", "0.bin"));
        Assert.Throws<InvalidDataException>(() => Dlss5SwitchJournal.Recover(_root));
        Assert.Equal("after", File.ReadAllText(file));
        Assert.True(File.Exists(Path.Combine(_root, ".adas", "switch-recovery", "journal.json")));
    }

    [Fact]
    public void DirectoryMoveRecoveryRestoresLegacyShaderFolderWithoutOccupiedEmptyFolder()
    {
        var source = Path.Combine(_root, "legacy");
        var backup = Path.Combine(_root, "backup");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "old.fx"), "shader");
        using (var journal = new Dlss5SwitchJournal(_root))
        {
            journal.CaptureMove(source, backup);
            Directory.Move(source, backup);
            journal.Rollback();
        }
        Assert.True(File.Exists(Path.Combine(source, "old.fx")));
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void ProfilesKeepTheirOwnTuningWithoutRestoringLoaderPathsOrProviderDefinitions()
    {
        var ini = Path.Combine(_root, "ReShade.ini");
        var preset = Path.Combine(_root, "ReShadePreset.ini");
        var stable = new Dlss5InstallRecord { Mode = Dlss5DeploymentMode.Dx11Feeder };
        File.WriteAllText(ini, "[GENERAL]\nEffectSearchPaths=bad\\**\\**\n[RenoDX.DLSS5]\nNRIntensity=0.6\nEnableHooks=9\n");
        File.WriteAllText(preset, "[DLSS5_Feed.fx]\nPreprocessorDefinitions=DLSS5_MV_PROVIDER=0\nMVScale=2\n");
        Dlss5ComponentService.SaveProfileSettings(_root, stable);
        File.WriteAllText(ini, "[GENERAL]\nEffectSearchPaths=correct\n[RenoDX.DLSS5]\nNRIntensity=0.2\nEnableHooks=2\n");
        var beta = new Dlss5InstallRecord { Mode = stable.Mode, Profile = Dlss5InstallProfile.LatestFeederBeta };
        Dlss5ComponentService.SaveProfileSettings(_root, beta);
        File.WriteAllText(preset, "[DLSS5_Feed.fx]\nPreprocessorDefinitions=DLSS5_MV_PROVIDER=3\nMVScale=1\n");
        Dlss5ComponentService.SaveRecord(_root, stable);
        Dlss5ComponentService.RestoreProfileSettings(_root);
        Assert.Contains("NRIntensity=0.6", File.ReadAllText(ini));
        Assert.Contains("EffectSearchPaths=correct", File.ReadAllText(ini));
        Assert.Contains("EnableHooks=2", File.ReadAllText(ini));
        Assert.Contains("DLSS5_MV_PROVIDER=3", File.ReadAllText(preset));
        Assert.Contains("MVScale=2", File.ReadAllText(preset));
        Dlss5ComponentService.SaveRecord(_root, beta);
        Dlss5ComponentService.RestoreProfileSettings(_root);
        Assert.Contains("NRIntensity=0.2", File.ReadAllText(ini));
    }

    [Theory]
    [InlineData(Dlss5DeploymentMode.Dx8Feeder, false)]
    [InlineData(Dlss5DeploymentMode.NativeDirectX12, false)]
    [InlineData(Dlss5DeploymentMode.VulkanFeeder, true)]
    [InlineData(Dlss5DeploymentMode.NativeVulkan, true)]
    public void OnlySharedLayerProfileSwitchesRequireRestoreFirst(Dlss5DeploymentMode mode, bool expected)
        => Assert.Equal(expected, Dlss5ComponentService.RequiresRestoreFirst(mode, mode,
            Dlss5InstallProfile.MaximumQuality, Dlss5InstallProfile.LatestFeederBeta));

    private sealed class Reporter : ICrashReporter
    {
        public bool VerboseLogging { get; set; }
        public void Log(string message) { }
        public void WriteCrashReport(string source, Exception? ex, bool isTerminating = false, string? note = null) { }
    }
}
