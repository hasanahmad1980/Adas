using RenoDXCommander.Models;
using RenoDXCommander.Services;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class Dlss5ComponentReviewTests
{
    [Fact]
    public void ExperimentalUnifiedFeederRequiresEarlyLoadHooks()
    {
        var assessment = new Dlss5Assessment(
            Dlss5DeploymentMode.Dx11Feeder,
            "C:\\Game",
            Array.Empty<string>(),
            Array.Empty<string>(),
            SinglePlayerConfirmed: true,
            Is64Bit: true);
        var plan = new Dlss5CompatibilityPlan(
            Dlss5RenoDxPackage.ExperimentalUnified,
            InstallFeeder: true,
            InstallDx11Bridge: false,
            PatchFeederForUnifiedName: false,
            ProfileName: "Experimental unified");

        Assert.True(Dlss5ComponentService.RequiresEarlyLoadSettings(assessment, plan));
    }

    [Fact]
    public void RelocateLegacyReShadeProxy_MovesDx9ReShadeToDxgiAndFreesTranslatorSlot()
    {
        var root = CreateTemporaryDirectory("adas-dx9-reshade-relocation");
        var source = FindRepositoryFile("RenoDXCommander", "Assets", "DLSS5", "ReShade-6.8.0-32.dll");
        var d3d9 = Path.Combine(root, "d3d9.dll");
        var dxgi = Path.Combine(root, "dxgi.dll");
        File.Copy(source, d3d9);
        var expectedHash = FileHelper.ComputeSha256(d3d9);
        var record = new Dlss5InstallRecord();
        try
        {
            var relocated = Dlss5ComponentService.RelocateLegacyReShadeProxy(root, d3d9, record);

            Assert.True(relocated);
            Assert.False(File.Exists(d3d9));
            Assert.True(File.Exists(dxgi));
            Assert.Equal(expectedHash, FileHelper.ComputeSha256(dxgi));
            Assert.True(record.OriginalBackups.TryGetValue(d3d9, out var backup));
            Assert.Null(backup);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Mislabelled64BitRenoDxAddon_DoesNotCountAsInstalledAndIsArchivedOnRemoval()
    {
        var root = CreateTemporaryDirectory("adas-mislabelled-x64-addon32");
        var source = FindRepositoryFile("RenoDXCommander", "Assets", "DLSS5", "renodx-dlss5-4.55.addon64");
        var invalid = Path.Combine(root, "renodx-dlss5.addon32");
        File.Copy(source, invalid);
        try
        {
            var service = new Dlss5ComponentService(null!, new NoopCrashReporter(), null!, null!, null!, null!);

            Assert.Equal(Dlss5DeploymentMode.None, service.GetInstalledMode(root));
            Assert.Empty(service.Uninstall(root));
            Assert.False(File.Exists(invalid));
            Assert.Single(Directory.GetFiles(Path.Combine(root, ".adas", "preserved"), "renodx-dlss5.addon32.*.modified"));
            Assert.Equal(Dlss5DeploymentMode.None, service.GetInstalledMode(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InstallReShadeFrameworkHeaders_TracksNewHeaderAndRestoresExistingHeader()
    {
        var root = CreateTemporaryDirectory("adas-reshade-headers");
        var staging = Path.Combine(root, "staging");
        var game = Path.Combine(root, "game");
        var shaders = Path.Combine(game, "reshade-shaders", "Shaders");
        Directory.CreateDirectory(Path.Combine(staging, "CrosireMaster"));
        Directory.CreateDirectory(shaders);
        File.WriteAllText(Path.Combine(staging, "CrosireMaster", "ReShade.fxh"), "current framework");
        File.WriteAllText(Path.Combine(staging, "CrosireMaster", "ReShadeUI.fxh"), "current UI framework");
        File.WriteAllText(Path.Combine(staging, "CrosireMaster", "DrawText.fxh"), "current text framework");
        File.WriteAllText(Path.Combine(shaders, "ReShade.fxh"), "user framework");
        var record = new Dlss5InstallRecord();
        try
        {
            var installed = Dlss5ComponentService.InstallReShadeFrameworkHeaders(staging, game, record);
            Dlss5ComponentService.SaveRecord(game, record);

            Assert.Equal(3, installed.Count);
            Assert.Equal("current framework", File.ReadAllText(Path.Combine(shaders, "ReShade.fxh")));
            Assert.Equal("current UI framework", File.ReadAllText(Path.Combine(shaders, "ReShadeUI.fxh")));
            Assert.Equal("current text framework", File.ReadAllText(Path.Combine(shaders, "DrawText.fxh")));

            Assert.Empty(Dlss5ComponentService.UninstallTrackedFiles(game, new NoopCrashReporter()));
            Assert.Equal("user framework", File.ReadAllText(Path.Combine(shaders, "ReShade.fxh")));
            Assert.False(File.Exists(Path.Combine(shaders, "ReShadeUI.fxh")));
            Assert.False(File.Exists(Path.Combine(shaders, "DrawText.fxh")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UninstallTrackedFiles_RestoresOriginalWhenDestinationDirectoryWasRemoved()
    {
        var root = CreateTemporaryDirectory("adas-removed-shader-directory");
        var shaders = Path.Combine(root, "reshade-shaders", "Shaders");
        var destination = Path.Combine(shaders, "ReShade.fxh");
        var incoming = Path.Combine(root, "incoming.fxh");
        Directory.CreateDirectory(shaders);
        File.WriteAllText(destination, "original framework");
        File.WriteAllText(incoming, "installed framework");
        var record = new Dlss5InstallRecord();
        try
        {
            Dlss5ComponentService.InstallTrackedFile(incoming, destination, root, record);
            Dlss5ComponentService.SaveRecord(root, record);
            Directory.Delete(Path.Combine(root, "reshade-shaders"), recursive: true);

            Assert.Empty(Dlss5ComponentService.UninstallTrackedFiles(root, new NoopCrashReporter()));
            Assert.Equal("original framework", File.ReadAllText(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UninstallTrackedFiles_RejectsRecordDestinationOutsideDeploymentBeforeMutation()
    {
        var parent = CreateTemporaryDirectory("adas-record-outside");
        var root = Path.Combine(parent, "game");
        var outside = Path.Combine(parent, "outside.addon64");
        Directory.CreateDirectory(root);
        File.WriteAllText(outside, "do not delete");
        try
        {
            var record = new Dlss5InstallRecord();
            record.InstalledHashes[outside] = FileHelper.ComputeSha256(outside);
            WriteTamperedRecord(root, record);

            Assert.Throws<InvalidDataException>(() =>
                Dlss5ComponentService.UninstallTrackedFiles(root, new NoopCrashReporter()));
            Assert.Equal("do not delete", File.ReadAllText(outside));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void LoadRecord_RejectsCanonicalDestinationAliases()
    {
        var root = CreateTemporaryDirectory("adas-record-alias");
        var destination = Path.Combine(root, "component.addon64");
        var alias = Path.Combine(root, "nested", "..", "component.addon64");
        try
        {
            var record = new Dlss5InstallRecord();
            record.InstalledHashes[destination] = new string('A', 64);
            record.InstalledHashes[alias] = new string('B', 64);
            WriteTamperedRecord(root, record);

            Assert.Throws<InvalidDataException>(() => Dlss5ComponentService.LoadRecord(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MigrateLegacyLaunchPad_UninstallRestoresOriginalFileAndDirectory()
    {
        var root = CreateTemporaryDirectory("adas-launchpad-roundtrip");
        var shaders = Path.Combine(root, "reshade-shaders", "Shaders");
        var launchPad = Path.Combine(shaders, "MartysMods_LAUNCHPAD.fx");
        var includes = Path.Combine(shaders, "MartysMods");
        Directory.CreateDirectory(includes);
        File.WriteAllText(launchPad, "legacy shader");
        File.WriteAllText(Path.Combine(includes, "legacy.fxh"), "legacy include");
        try
        {
            var preserved = Dlss5ComponentService.MigrateLegacyLaunchPad(root);

            Assert.Equal(2, preserved.Count);
            Assert.False(File.Exists(launchPad));
            Assert.False(Directory.Exists(includes));

            Assert.Empty(Dlss5ComponentService.UninstallTrackedFiles(root, new NoopCrashReporter()));
            Assert.Equal("legacy shader", File.ReadAllText(launchPad));
            Assert.Equal("legacy include", File.ReadAllText(Path.Combine(includes, "legacy.fxh")));
            Assert.Null(Dlss5ComponentService.LoadRecord(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UninstallTrackedFiles_RejectsBackupOutsideAdasBeforeMutation()
    {
        var parent = CreateTemporaryDirectory("adas-record-backup-escape");
        var root = Path.Combine(parent, "game");
        var destination = Path.Combine(root, "component.addon64");
        var outsideBackup = Path.Combine(parent, "outside.bak");
        Directory.CreateDirectory(root);
        File.WriteAllText(outsideBackup, "outside backup");
        try
        {
            var record = new Dlss5InstallRecord();
            record.OriginalBackups[destination] = outsideBackup;
            WriteTamperedRecord(root, record);

            Assert.Throws<InvalidDataException>(() =>
                Dlss5ComponentService.UninstallTrackedFiles(root, new NoopCrashReporter()));
            Assert.True(File.Exists(outsideBackup));
            Assert.False(File.Exists(destination));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void RollbackLegacyLaunchPad_RestoresFilesAfterLaterInstallFailure()
    {
        var root = CreateTemporaryDirectory("adas-launchpad-rollback");
        var shaders = Path.Combine(root, "reshade-shaders", "Shaders");
        var launchPad = Path.Combine(shaders, "MartysMods_LAUNCHPAD.fx");
        Directory.CreateDirectory(shaders);
        File.WriteAllText(launchPad, "legacy shader");
        try
        {
            Dlss5ComponentService.MigrateLegacyLaunchPad(root);
            var record = Dlss5ComponentService.LoadRecord(root)!;

            Dlss5ComponentService.RollbackLegacyLaunchPad(
                root,
                record,
                record.LegacyLaunchPadBackups.Keys.ToArray());

            Assert.Equal("legacy shader", File.ReadAllText(launchPad));
            Assert.Empty(record.LegacyLaunchPadBackups);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HostedFeederFiles_InstallAndUninstallCompleteOwnedHostLayout()
    {
        var root = CreateTemporaryDirectory("adas-hosted-feeder");
        var sources = Path.Combine(root, "sources");
        Directory.CreateDirectory(sources);
        var host = WriteSource(sources, Dlss5ComponentService.FeederHost64, "host renodx-dlss5.addon64\0 executable");
        var reshade = WriteSource(sources, "ReShade64.dll", "x64 reshade");
        var renodx = WriteSource(sources, Renodx5AddonService.AddonFileName, "renodx addon");
        var rootNr = WriteSource(root, "nvngx_dlssnr.dll", "nr runtime");
        var rootSr = WriteSource(root, "nvngx_dlss.dll", "sr runtime");
        var record = new Dlss5InstallRecord();
        record.OriginalBackups[rootNr] = null;
        record.OriginalBackups[rootSr] = null;
        record.InstalledHashes[rootNr] = FileHelper.ComputeSha256(rootNr);
        record.InstalledHashes[rootSr] = FileHelper.ComputeSha256(rootSr);
        var installed = new List<string>();
        var warnings = new List<string>();
        try
        {
            Dlss5ComponentService.InstallHostedFeederFiles(
                root,
                new Dictionary<string, string> { [Dlss5ComponentService.FeederHost64] = host },
                reshade,
                renodx,
                record,
                installed,
                warnings);

            Assert.Empty(warnings);
            Assert.Equal(5, installed.Count);
            Assert.All(installed, path => Assert.True(File.Exists(path)));
            Assert.False(File.Exists(rootNr));
            Assert.False(File.Exists(rootSr));
            Assert.NotNull(Dlss5ComponentService.LoadRecord(root));

            Assert.Empty(Dlss5ComponentService.UninstallTrackedFiles(root, new NoopCrashReporter()));
            Assert.All(installed, path => Assert.False(File.Exists(path)));
            Assert.Null(Dlss5ComponentService.LoadRecord(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HostedFeederFiles_MissingSuperResolutionRuntimeStopsInstallation()
    {
        var root = CreateTemporaryDirectory("adas-hosted-feeder-missing-runtime");
        var sources = Path.Combine(root, "sources");
        Directory.CreateDirectory(sources);
        var host = WriteSource(sources, Dlss5ComponentService.FeederHost64, "host renodx-dlss5.addon64\0 executable");
        var reshade = WriteSource(sources, "ReShade64.dll", "x64 reshade");
        var renodx = WriteSource(sources, Renodx5AddonService.AddonFileName, "renodx addon");
        WriteSource(root, "nvngx_dlssnr.dll", "nr runtime");
        try
        {
            var error = Assert.Throws<FileNotFoundException>(() =>
                Dlss5ComponentService.InstallHostedFeederFiles(
                    root,
                    new Dictionary<string, string> { [Dlss5ComponentService.FeederHost64] = host },
                    reshade,
                    renodx,
                    new Dlss5InstallRecord(),
                    new List<string>(),
                    new List<string>()));

            Assert.Contains("nvngx_dlss.dll", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(Dlss5DeploymentMode.Dx9Feeder, false)]      // 32-bit Feeder host route (e.g. F.E.A.R.)
    [InlineData(Dlss5DeploymentMode.Dx12Feeder, true)]      // 64-bit Feeder route
    [InlineData(Dlss5DeploymentMode.NativeDirectX12, true)] // 64-bit native / non-Feeder route
    public void VerifyInstallation_WhenDeepFriedChickenFilesPresent_DoesNotDemandRenoDxConsumer(
        Dlss5DeploymentMode mode, bool is64Bit)
    {
        var root = CreateTemporaryDirectory("adas-dfc-verify-present");
        try
        {
            // Deep Fried Chicken replaces the RenoDX consumer: its files sit where the consumer goes,
            // and renodx-dlss5.addon64 was deliberately retired by the installer.
            var consumerFolder = is64Bit
                ? ModInstallService.GetAddonDeployPath(root)
                : Path.Combine(root, "host64");
            Directory.CreateDirectory(consumerFolder);
            foreach (var name in new[]
            {
                DeepFriedChickenService.AddonFileName,
                DeepFriedChickenService.NvngxShim,
                DeepFriedChickenService.ConfigFileName,
            })
                WriteSource(consumerFolder, name, "dfc");

            // Record from an existing install predating the DeepFriedChicken flag (defaults to false).
            Dlss5ComponentService.SaveRecord(root, new Dlss5InstallRecord
            {
                Mode = mode,
                Profile = Dlss5InstallProfile.MaximumQuality,
                ComponentVersion = "Maximum Quality — Feeder-pinned RenoDX v4.55; Feeder local-user-import",
                InstalledAtUtc = DateTime.UtcNow,
            });

            var problems = Dlss5DiagnosticService.VerifyInstallation(root, mode, is64Bit);

            Assert.DoesNotContain(problems, p => p.Contains("renodx-dlss5.addon64", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(problems, p => p.Contains(DeepFriedChickenService.AddonFileName, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(Dlss5DeploymentMode.Dx9Feeder, false)]      // 32-bit Feeder host route (e.g. F.E.A.R.)
    [InlineData(Dlss5DeploymentMode.Dx12Feeder, true)]      // 64-bit Feeder route
    [InlineData(Dlss5DeploymentMode.NativeDirectX12, true)] // 64-bit native / non-Feeder route
    public void VerifyInstallation_WhenDeepFriedChickenRecordedButFilesMissing_FlagsDfcNotRenoDx(
        Dlss5DeploymentMode mode, bool is64Bit)
    {
        var root = CreateTemporaryDirectory("adas-dfc-verify-missing");
        try
        {
            Dlss5ComponentService.SaveRecord(root, new Dlss5InstallRecord
            {
                Mode = mode,
                Profile = Dlss5InstallProfile.MaximumQuality,
                DeepFriedChicken = true,
                ComponentVersion = "Maximum Quality — Feeder-pinned RenoDX v4.55; Feeder local-user-import",
                InstalledAtUtc = DateTime.UtcNow,
            });

            var problems = Dlss5DiagnosticService.VerifyInstallation(root, mode, is64Bit);

            // A quarantined DFC consumer must be reported as the missing DFC file, never as RenoDX.
            Assert.Contains(problems, p => p.Contains(DeepFriedChickenService.AddonFileName, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(problems, p => p.Contains("renodx-dlss5.addon64", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HostedFeederFiles_SwitchingToRenoDx_RetiresLingeringDeepFriedChickenFiles()
    {
        var root = CreateTemporaryDirectory("adas-hosted-feeder-dfc-switch");
        var sources = Path.Combine(root, "sources");
        Directory.CreateDirectory(sources);
        var host = WriteSource(sources, Dlss5ComponentService.FeederHost64, "host renodx-dlss5.addon64\0 executable");
        var reshade = WriteSource(sources, "ReShade64.dll", "x64 reshade");
        var renodx = WriteSource(sources, Renodx5AddonService.AddonFileName, "renodx addon");
        WriteSource(root, "nvngx_dlssnr.dll", "nr runtime");
        WriteSource(root, "nvngx_dlss.dll", "sr runtime");

        // A prior Deep Fried Chicken install left its files inside the Feeder host folder.
        var hostDir = Path.Combine(root, "host64");
        Directory.CreateDirectory(hostDir);
        foreach (var name in DeepFriedChickenService.RequiredFiles)
            WriteSource(hostDir, name, "stale dfc");

        var record = new Dlss5InstallRecord();
        var installed = new List<string>();
        var warnings = new List<string>();
        try
        {
            Dlss5ComponentService.InstallHostedFeederFiles(
                root,
                new Dictionary<string, string> { [Dlss5ComponentService.FeederHost64] = host },
                reshade,
                renodx,
                record,
                installed,
                warnings);

            // Switching back to the RenoDX consumer must not leave DFC stacked beside it.
            foreach (var name in DeepFriedChickenService.RequiredFiles)
                Assert.False(File.Exists(Path.Combine(hostDir, name)), $"{name} should have been retired");
            Assert.True(File.Exists(Path.Combine(hostDir, Renodx5AddonService.AddonFileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]   // installing DFC as the consumer must keep the DFC files
    [InlineData(false)]  // switching to RenoDX must retire lingering DFC files
    public void RemoveIncompatibleDlssAddons_RetiresDeepFriedChickenOnlyWhenRenoDxIsTheConsumer(bool useDeepFriedChicken)
    {
        var root = CreateTemporaryDirectory("adas-remove-incompatible-dfc");
        var addonPath = root;
        foreach (var name in DeepFriedChickenService.RequiredFiles)
            WriteSource(addonPath, name, "dfc");
        var record = new Dlss5InstallRecord();
        var plan = new Dlss5CompatibilityPlan(
            Dlss5RenoDxPackage.Feeder455,
            InstallFeeder: false,
            InstallDx11Bridge: false,
            PatchFeederForUnifiedName: false,
            ProfileName: "test");
        try
        {
            Dlss5ComponentService.RemoveIncompatibleDlssAddons(root, addonPath, plan, record, useDeepFriedChicken);

            foreach (var name in DeepFriedChickenService.RequiredFiles)
            {
                var path = Path.Combine(addonPath, name);
                if (useDeepFriedChicken)
                    Assert.True(File.Exists(path), $"{name} is the consumer and must be kept");
                else
                    Assert.False(File.Exists(path), $"{name} must be retired when RenoDX is the consumer");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeepFriedChicken_RealDeployThenVerify_AgreeOnConsumerForFeederHostRoute()
    {
        var root = CreateTemporaryDirectory("adas-dfc-e2e");
        var cache = CreateTemporaryDirectory("adas-dfc-cache");
        var sources = Path.Combine(root, "sources");
        Directory.CreateDirectory(sources);

        // A real imported Deep Fried Chicken release in the service's cache.
        foreach (var name in DeepFriedChickenService.RequiredFiles)
            WriteSource(cache, name, "dfc " + name);
        var dfc = new DeepFriedChickenService(new NoopCrashReporter(), cache);
        Assert.True(dfc.IsImported);

        var host = WriteSource(sources, Dlss5ComponentService.FeederHost64, "host executable");
        var reshade = WriteSource(sources, "ReShade64.dll", "x64 reshade");
        var renodx = WriteSource(sources, Renodx5AddonService.AddonFileName, "renodx addon");
        WriteSource(root, "nvngx_dlssnr.dll", "nr runtime");
        WriteSource(root, "nvngx_dlss.dll", "sr runtime");
        var plan = new Dlss5CompatibilityPlan(
            Dlss5RenoDxPackage.Feeder455,
            InstallFeeder: true,
            InstallDx11Bridge: false,
            PatchFeederForUnifiedName: false,
            ProfileName: "Maximum Quality — Feeder-pinned RenoDX v4.55");
        var record = new Dlss5InstallRecord
        {
            Mode = Dlss5DeploymentMode.Dx11Feeder,
            Profile = Dlss5InstallProfile.MaximumQuality,
            DeepFriedChicken = true,
            ComponentVersion = "Maximum Quality — Feeder-pinned RenoDX v4.55; Feeder local-user-import",
            InstalledAtUtc = DateTime.UtcNow,
        };
        try
        {
            // Deploy with the real installer helper and the real DFC service.
            Dlss5ComponentService.InstallHostedFeederFiles(
                root,
                new Dictionary<string, string> { [Dlss5ComponentService.FeederHost64] = host },
                reshade,
                renodx,
                plan,
                record,
                new List<string>(),
                new List<string>(),
                dfc);
            Dlss5ComponentService.SaveRecord(root, record);

            // The deploy landed DFC into the host folder, and RenoDX was not installed beside it.
            var hostDir = Path.Combine(root, "host64");
            foreach (var name in DeepFriedChickenService.RequiredFiles)
                Assert.True(File.Exists(Path.Combine(hostDir, name)), $"deploy should place {name}");
            Assert.False(File.Exists(Path.Combine(hostDir, Renodx5AddonService.AddonFileName)));

            // The real verifier agrees: it never demands RenoDX and never reports a DFC file missing.
            var problems = Dlss5DiagnosticService.VerifyInstallation(root, Dlss5DeploymentMode.Dx11Feeder, is64Bit: false);
            Assert.DoesNotContain(problems, p => p.Contains("renodx-dlss5.addon64", StringComparison.OrdinalIgnoreCase));
            foreach (var name in DeepFriedChickenService.RequiredFiles)
                Assert.DoesNotContain(problems, p => p.Contains(name, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public void FindAutomaticRuntimePackage_PrefersPackagedArchiveOverDownloadsFallback()
    {
        var profile = CreateTemporaryDirectory("adas-runtime-discovery");
        var archive = Path.Combine(profile, "Downloads", "DLSS5", "streamline.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        File.WriteAllText(archive, "runtime package");
        try
        {
            var result = Dlss5ComponentService.FindAutomaticRuntimePackage(profile);

            Assert.NotNull(result);
            Assert.EndsWith(Path.Combine("Assets", "DLSS5", "streamline.zip"), result, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(archive, result);
        }
        finally
        {
            Directory.Delete(profile, recursive: true);
        }
    }

    [Fact]
    public void ImportRuntimeArchive_RepairsExistingHostedFeederRuntimeCopies()
    {
        var root = CreateTemporaryDirectory("adas-runtime-host-sync");
        var archive = Path.Combine(root, "streamline.zip");
        Directory.CreateDirectory(Path.Combine(root, "host64"));
        Dlss5ComponentService.SaveRecord(root, new Dlss5InstallRecord
        {
            Mode = Dlss5DeploymentMode.Dx11Feeder,
        });
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            WriteArchiveEntry(zip, "streamline/nvngx_dlss.dll", "super resolution runtime");
            WriteArchiveEntry(zip, "streamline/nvngx_dlssnr.dll", "neural rendering runtime");
        }

        try
        {
            var service = new Dlss5ComponentService(null!, new NoopCrashReporter(), null!, null!, null!, null!);

            service.ImportLocalRuntimeFolder(archive, root);

            Assert.Equal("super resolution runtime", File.ReadAllText(Path.Combine(root, "host64", "nvngx_dlss.dll")));
            Assert.Equal("neural rendering runtime", File.ReadAllText(Path.Combine(root, "host64", "nvngx_dlssnr.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ImportAutomaticRuntimePackage_UsesDiscoveredArchiveBeforeInstallation()
    {
        var profile = CreateTemporaryDirectory("adas-runtime-auto-import-profile");
        var game = CreateTemporaryDirectory("adas-runtime-auto-import-game");
        var archive = Path.Combine(profile, "Downloads", "DLSS5", "streamline.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            WriteArchiveEntry(zip, "streamline/nvngx_dlss.dll", "super resolution runtime");
            WriteArchiveEntry(zip, "streamline/nvngx_dlssnr.dll", "neural rendering runtime");
        }

        try
        {
            var service = new Dlss5ComponentService(null!, new NoopCrashReporter(), null!, null!, null!, null!);

            var installed = service.ImportAutomaticRuntimePackage(game, profile, Path.Combine(profile, "no-bundled-assets"));

            Assert.Contains(Path.Combine(game, "nvngx_dlss.dll"), installed);
            Assert.Contains(Path.Combine(game, "nvngx_dlssnr.dll"), installed);
        }
        finally
        {
            Directory.Delete(profile, recursive: true);
            Directory.Delete(game, recursive: true);
        }
    }

    [Fact]
    public void ImportAutomaticRuntimePackage_PreservesExistingGameRuntimeAndAddsOnlyMissingFiles()
    {
        var profile = CreateTemporaryDirectory("adas-runtime-preserve-profile");
        var game = CreateTemporaryDirectory("adas-runtime-preserve-game");
        var archive = Path.Combine(profile, "Downloads", "DLSS5", "streamline.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        File.WriteAllText(Path.Combine(game, "nvngx_dlss.dll"), "game-owned runtime");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            WriteArchiveEntry(zip, "streamline/nvngx_dlss.dll", "package replacement");
            WriteArchiveEntry(zip, "streamline/nvngx_dlssnr.dll", "missing NR runtime");
        }

        try
        {
            var service = new Dlss5ComponentService(null!, new NoopCrashReporter(), null!, null!, null!, null!);

            var installed = service.ImportAutomaticRuntimePackage(game, profile, Path.Combine(profile, "no-bundled-assets"));

            Assert.Equal("game-owned runtime", File.ReadAllText(Path.Combine(game, "nvngx_dlss.dll")));
            Assert.Equal("missing NR runtime", File.ReadAllText(Path.Combine(game, "nvngx_dlssnr.dll")));
            Assert.DoesNotContain(Path.Combine(game, "nvngx_dlss.dll"), installed);
            Assert.Contains(Path.Combine(game, "nvngx_dlssnr.dll"), installed);
        }
        finally
        {
            Directory.Delete(profile, recursive: true);
            Directory.Delete(game, recursive: true);
        }
    }

    [Fact]
    public void ImportAutomaticRuntimePackage_For32BitFeeder_StagesRuntimesOnlyInHost64()
    {
        var profile = CreateTemporaryDirectory("adas-runtime-host-only-profile");
        var game = CreateTemporaryDirectory("adas-runtime-host-only-game");
        var archive = Path.Combine(profile, "Downloads", "DLSS5", "streamline.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            WriteArchiveEntry(zip, "streamline/nvngx_dlss.dll", "super resolution runtime");
            WriteArchiveEntry(zip, "streamline/nvngx_dlssnr.dll", "neural rendering runtime");
            WriteArchiveEntry(zip, "streamline/sl.interposer.dll", "streamline runtime");
        }

        try
        {
            var service = new Dlss5ComponentService(null!, new NoopCrashReporter(), null!, null!, null!, null!);

            var installed = service.ImportAutomaticRuntimePackage(
                game,
                profile,
                Path.Combine(profile, "no-bundled-assets"),
                hosted64Only: true);

            Assert.Contains(Path.Combine(game, "host64", "nvngx_dlss.dll"), installed);
            Assert.Contains(Path.Combine(game, "host64", "nvngx_dlssnr.dll"), installed);
            Assert.Contains(Path.Combine(game, "host64", "sl.interposer.dll"), installed);
            Assert.False(File.Exists(Path.Combine(game, "nvngx_dlss.dll")));
            Assert.False(File.Exists(Path.Combine(game, "nvngx_dlssnr.dll")));
            Assert.False(File.Exists(Path.Combine(game, "sl.interposer.dll")));
        }
        finally
        {
            Directory.Delete(profile, recursive: true);
            Directory.Delete(game, recursive: true);
        }
    }

    [Fact]
    public void RestoreNativeGameRuntimes_RevertsOnlyUnchangedAdasReplacement()
    {
        var root = CreateTemporaryDirectory("adas-native-runtime-restore");
        var destination = Path.Combine(root, "sl.interposer.dll");
        var backup = Path.Combine(root, ".adas", "backups", "dlss5", "sl.interposer.dll.bak");
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.WriteAllText(destination, "Adas replacement");
        File.WriteAllText(backup, "game original");
        var record = new Dlss5InstallRecord();
        record.InstalledHashes[destination] = FileHelper.ComputeSha256(destination);
        record.OriginalBackups[destination] = backup;

        try
        {
            var restored = Dlss5ComponentService.RestoreNativeGameRuntimes(
                root,
                record,
                new NoopCrashReporter());

            Assert.Contains(destination, restored);
            Assert.Equal("game original", File.ReadAllText(destination));
            Assert.False(File.Exists(backup));
            Assert.False(record.InstalledHashes.ContainsKey(destination));
            Assert.False(record.OriginalBackups.ContainsKey(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnsureFeederPreset_EnablesLumeniteBeforeFeedAndBindsProviderThree()
    {
        var root = CreateTemporaryDirectory("adas-feeder-preset");
        File.WriteAllText(Path.Combine(root, "ReShade.ini"), "[GENERAL]\nEffectSearchPaths=.\\reshade-shaders\\Shaders\\**\n");
        File.WriteAllText(Path.Combine(root, "ReShadePreset.ini"), "Techniques=CAS@CAS.fx,DRME@MotionEstimation.fx,DLSS5_Feed@DLSS5_Feed.fx\nTechniqueSorting=CAS@CAS.fx,DRME@MotionEstimation.fx\n\n[DLSS5_Feed.fx]\nPreprocessorDefinitions=OLD_VALUE=1,DLSS5_MV_PROVIDER=0\n");
        try
        {
            var record = new Dlss5InstallRecord();
            var presetPath = Dlss5ComponentService.EnsureFeederPreset(root, record);
            var preset = File.ReadAllText(presetPath);

            Assert.Contains("Techniques=Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx,CAS@CAS.fx", preset, StringComparison.Ordinal);
            Assert.DoesNotContain("Techniques=Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx,CAS@CAS.fx,DRME", preset, StringComparison.Ordinal);
            Assert.Contains("TechniqueSorting=Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx,CAS@CAS.fx,DRME@MotionEstimation.fx", preset, StringComparison.Ordinal);
            Assert.Contains("PreprocessorDefinitions=DLSS5_MV_PROVIDER=3,OLD_VALUE=1", preset, StringComparison.Ordinal);
            Assert.Contains("PresetPath=.\\ReShadePreset.ini", File.ReadAllText(Path.Combine(root, "ReShade.ini")), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnsureUnifiedRenoDxSettings_MigratesCinematicAndEnablesNeuralRenderingOnce()
    {
        var root = CreateTemporaryDirectory("adas-renodx-settings");
        var iniPath = Path.Combine(root, "ReShade.ini");
        File.WriteAllText(iniPath,
            "[RENODX-DLSS]\nDirectNeuralRenderingStyle=0\n\n[RenoDX.DLSS5]\nNRStyle=1\n");
        var record = new Dlss5InstallRecord();
        try
        {
            Dlss5ComponentService.EnsureUnifiedRenoDxSettings(root, record);
            var first = File.ReadAllText(iniPath);

            Assert.True(record.UnifiedRenoDxSettingsMigrated);
            Assert.Contains("DirectNeuralRenderingEnabled=1", first, StringComparison.Ordinal);
            Assert.Contains("OptionsMode=0", first, StringComparison.Ordinal);
            Assert.Contains("DirectNeuralRenderingStyle=1", first, StringComparison.Ordinal);

            File.WriteAllText(iniPath, first.Replace(
                "DirectNeuralRenderingStyle=1",
                "DirectNeuralRenderingStyle=2",
                StringComparison.Ordinal));
            Dlss5ComponentService.EnsureUnifiedRenoDxSettings(root, record);

            Assert.Contains(
                "DirectNeuralRenderingStyle=2",
                File.ReadAllText(iniPath),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(Dlss5InstallProfile.MaximumQuality, false, 1, "RenoDX.DLSS5", "NeuralUplift=0", "NRStyle=1")]
    [InlineData(Dlss5InstallProfile.ExperimentalUnified, true, 2, "RENODX-DLSS", "DirectNeuralRenderingEnabled=1", "DirectNeuralRenderingStyle=2")]
    public void SaveRenoDxUserSettings_PersistsSimpleControls(
        Dlss5InstallProfile profile,
        bool enabled,
        int style,
        string section,
        string expectedEnabled,
        string expectedStyle)
    {
        var root = CreateTemporaryDirectory("adas-renodx-user-settings");
        try
        {
            Dlss5ComponentService.SaveRenoDxUserSettings(root, profile, enabled, style);
            var contents = File.ReadAllText(Path.Combine(root, "ReShade.ini"));

            Assert.Contains($"[{section}]", contents, StringComparison.Ordinal);
            Assert.Contains(expectedEnabled, contents, StringComparison.Ordinal);
            Assert.Contains(expectedStyle, contents, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RepairReShadeConfiguration_PreservesFormattingAndRepairsCaseAndRecursivePaths()
    {
        var root = CreateTemporaryDirectory("adas-reshade-repair");
        var path = Path.Combine(root, "ReShade.ini");
        var original = "; keep this comment\r\n[GENERAL]\r\neffectsearchpaths=.\\custom\\**\\**\r\n";
        File.WriteAllText(path, original, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var record = new Dlss5InstallRecord();
        try
        {
            Dlss5ComponentService.RepairReShadeConfiguration(root, record);
            var bytes = File.ReadAllBytes(path);
            var repaired = File.ReadAllText(path);

            Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
            Assert.Contains("; keep this comment", repaired, StringComparison.Ordinal);
            Assert.Contains("EffectSearchPaths=.\\custom\\**,.\\reshade-shaders\\Shaders\\**", repaired, StringComparison.Ordinal);
            Assert.Contains("TextureSearchPaths=.\\reshade-shaders\\Textures\\**", repaired, StringComparison.Ordinal);
            Assert.DoesNotContain("effectsearchpaths=", repaired, StringComparison.Ordinal);
            Assert.Equal(2, record.IniSettingBackups.Count);

            Assert.Empty(Dlss5ComponentService.UninstallTrackedFiles(root, new NoopCrashReporter()));
            var restored = File.ReadAllText(path);
            Assert.Contains("effectsearchpaths=.\\custom\\**\\**", restored, StringComparison.Ordinal);
            Assert.DoesNotContain("TextureSearchPaths", restored, StringComparison.Ordinal);
            Assert.Contains("; keep this comment", restored, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IniSettingJournal_DoesNotOverwriteAUsersLaterChange()
    {
        var root = CreateTemporaryDirectory("adas-reshade-user-change");
        var path = Path.Combine(root, "ReShade.ini");
        File.WriteAllText(path, "[GENERAL]\nEffectSearchPaths=.\\old\n");
        var record = new Dlss5InstallRecord();
        try
        {
            Dlss5ComponentService.RepairReShadeConfiguration(root, record);
            var document = IniTextDocument.Load(path);
            document.SetValue("GENERAL", "EffectSearchPaths", @".\my-custom-path");
            document.Save(path);
            Dlss5ComponentService.RepairReShadeConfiguration(root, record);

            Assert.Empty(Dlss5ComponentService.UninstallTrackedFiles(root, new NoopCrashReporter()));
            Assert.Contains(@"EffectSearchPaths=.\my-custom-path", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DisableObsolete32BitRenoDx_PreservesFileAndRemovesItFromReShadeSearchPath()
    {
        var root = CreateTemporaryDirectory("adas-obsolete-renodx32");
        var obsolete = Path.Combine(root, "renodx-dlss5.addon32");
        var relabelledX64 = Path.Combine(root, "renodx-dlss.addon32");
        File.WriteAllText(obsolete, "obsolete 32-bit add-on");
        File.WriteAllText(relabelledX64, "x64 payload with a 32-bit name");
        var record = new Dlss5InstallRecord();
        try
        {
            Dlss5ComponentService.DisableObsolete32BitRenoDx(root, root, record);

            Assert.False(File.Exists(obsolete));
            Assert.False(File.Exists(relabelledX64));
            Assert.True(record.OriginalBackups.TryGetValue(obsolete, out var backup));
            Assert.True(record.OriginalBackups.TryGetValue(relabelledX64, out var relabelledBackup));
            Assert.NotNull(backup);
            Assert.NotNull(relabelledBackup);
            Assert.Equal("obsolete 32-bit add-on", File.ReadAllText(backup!));
            Assert.Equal("x64 payload with a 32-bit name", File.ReadAllText(relabelledBackup!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ShouldRemainPostInstallWarning_HidesMotionProviderThatFeederInstalls()
    {
        var warning = "a supported motion-vector provider (Adas installs LumeniteFX Kernel)";

        Assert.False(Dlss5ComponentService.ShouldRemainPostInstallWarning(
            Dlss5DeploymentMode.Dx11Feeder,
            "unused",
            warning));
    }

    [Fact]
    public void RepairReShadeAddonState_ReenablesSuiteAndPrunesOnlyMissingManagedEarlyLoads()
    {
        var root = CreateTemporaryDirectory("adas-addon-state-repair");
        var ini = Path.Combine(root, "ReShade.ini");
        File.WriteAllText(ini,
            "[ADDON]\n" +
            "DisabledAddons=Generic Depth,DLSS 5 Neural Rendering@renodx-dlss5.addon64,Effect Runtime Sync\n" +
            "LoadFromDllMain=other.addon64,renodx-dlss.addon64,dlss5-bridge.addon64\n");
        File.WriteAllText(Path.Combine(root, Dlss5ComponentService.BridgeAddon), "installed bridge");
        try
        {
            var removed = Dlss5ComponentService.RepairReShadeAddonState(root);
            var repaired = File.ReadAllText(ini);

            Assert.Contains("DLSS 5 Neural Rendering@renodx-dlss5.addon64", removed);
            Assert.Contains("renodx-dlss.addon64", removed);
            Assert.Contains("DisabledAddons=Generic Depth,Effect Runtime Sync", repaired, StringComparison.Ordinal);
            Assert.Contains("LoadFromDllMain=other.addon64,dlss5-bridge.addon64", repaired, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Diagnostics_ReportSuccessfulNativeFramesInPlainLanguage()
    {
        var root = CreateTemporaryDirectory("adas-dlss5-diagnostics");
        var addonPath = ModInstallService.GetAddonDeployPath(root);
        Directory.CreateDirectory(addonPath);
        File.WriteAllText(Path.Combine(root, "dxgi.dll"), "reshade");
        File.WriteAllText(Path.Combine(addonPath, "renodx-dlss5.addon64"), "renodx");
        File.WriteAllText(Path.Combine(root, "ReShade.log"), "Successful NR frames: 42");
        try
        {
            Dlss5ComponentService.SaveRecord(root, new Dlss5InstallRecord
            {
                Mode = Dlss5DeploymentMode.NativeDirectX12,
                Profile = Dlss5InstallProfile.MaximumQuality,
                ComponentVersion = "test",
            });

            var report = Dlss5DiagnosticService.Diagnose(root, Dlss5DeploymentMode.NativeDirectX12, is64Bit: true);

            Assert.False(report.HasProblems);
            Assert.True(report.IsWorking);
            Assert.Contains("processing", report.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("does not verify the visible picture", report.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Diagnostics_ExplainThatUnifiedAddonDoesNotUseReShadeEffects()
    {
        var root = CreateTemporaryDirectory("adas-dlss5-direct-no-effects");
        var addonPath = ModInstallService.GetAddonDeployPath(root);
        Directory.CreateDirectory(addonPath);
        File.WriteAllText(Path.Combine(root, "dxgi.dll"), "reshade");
        File.WriteAllText(Path.Combine(addonPath, Renodx5AddonService.AddonFileName), "renodx");
        File.WriteAllText(Path.Combine(root, "ReShade.ini"), "[GENERAL]\nEffectSearchPaths=.\\reshade-shaders\\Shaders\\**\n");
        File.WriteAllText(Path.Combine(root, "ReShade.log"), "RenoDX DLSS first present\nFailed to resolve search path 'reshade-shaders\\Shaders' with error code 3.");
        try
        {
            Dlss5ComponentService.SaveRecord(root, new Dlss5InstallRecord
            {
                Mode = Dlss5DeploymentMode.NativeDirectX12,
                Profile = Dlss5InstallProfile.ExperimentalUnified,
                ComponentVersion = "test",
                InstalledAtUtc = DateTime.UtcNow.AddMinutes(-1),
            });

            var report = Dlss5DiagnosticService.Diagnose(root, Dlss5DeploymentMode.NativeDirectX12, is64Bit: true);

            Assert.False(report.HasProblems);
            Assert.False(report.IsWorking);
            Assert.Contains(report.Findings, item => item.Contains("does not use ReShade effect files", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(report.Findings, item => item.Contains("RenoDX DLSS tab", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SimpleStatus_DoesNotCallUnconfirmedRuntimeVerified()
    {
        var report = new Dlss5DiagnosticReport(
            HasProblems: false,
            IsWorking: false,
            Summary: "The installation is complete and ready to test in game.",
            Findings: new[] { "Open the RenoDX DLSS tab." });

        var status = MainWindow.DescribeSimpleInstalledStatus(report);

        Assert.Equal("DLSS 5 files are installed", status.Title);
        Assert.Contains("not yet confirmed", status.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RenoDX DLSS tab", status.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verified", status.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Diagnostics_ExplainWrongArchitectureLogError()
    {
        var root = CreateTemporaryDirectory("adas-dlss5-wrong-architecture");
        File.WriteAllText(Path.Combine(root, "ReShade.log"), "Failed to load add-on with error code 193");
        try
        {
            var report = Dlss5DiagnosticService.Diagnose(root, Dlss5DeploymentMode.Dx11Feeder, is64Bit: false);

            Assert.True(report.HasProblems);
            Assert.Contains(report.Findings, item => item.Contains("64-bit add-on in a 32-bit process", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException($"Repository file not found: {Path.Combine(relativeSegments)}");
    }

    private static void WriteTamperedRecord(string root, Dlss5InstallRecord record)
    {
        var path = Path.Combine(root, ".adas", "dlss5-install.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(record));
    }

    private static string WriteSource(string directory, string name, string content)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static void WriteArchiveEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }

    private sealed class NoopCrashReporter : ICrashReporter
    {
        public bool VerboseLogging { get; set; }
        public void Log(string message) { }
        public void WriteCrashReport(string source, Exception? ex, bool isTerminating = false, string? note = null) { }
    }
}
