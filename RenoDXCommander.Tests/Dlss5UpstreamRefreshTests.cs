using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class Dlss5UpstreamRefreshTests
{
    [Theory]
    [InlineData(Dlss5DeploymentMode.Dx10Feeder, false, Dlss5InstallProfile.MaximumQuality, Dlss5InstallProfile.LatestFeederBeta)]
    [InlineData(Dlss5DeploymentMode.VulkanFeeder, false, Dlss5InstallProfile.MaximumQuality, Dlss5InstallProfile.LatestFeederBeta)]
    [InlineData(Dlss5DeploymentMode.NativeVulkan, true, Dlss5InstallProfile.ExperimentalUnified, Dlss5InstallProfile.MaximumQuality)]
    [InlineData(Dlss5DeploymentMode.Dx11Feeder, true, Dlss5InstallProfile.MaximumQuality, Dlss5InstallProfile.MaximumQuality)]
    public void ProfileNormalizationRunsBeforeRepairConflictChecks(
        Dlss5DeploymentMode mode,
        bool is64Bit,
        Dlss5InstallProfile selected,
        Dlss5InstallProfile expected)
        => Assert.Equal(expected, Dlss5ComponentService.NormalizeProfileForMode(mode, is64Bit, selected));

    [Theory]
    [InlineData("SF-2026-09-02", true)]
    [InlineData("SF-0.3", false)]
    [InlineData("SF-2026-99-02", false)]
    [InlineData(null, false)]
    public void SuppliedDatedBuildIsNotDowngradedByTheVersionMirror(string? version, bool pinned)
        => Assert.Equal(pinned, Renodx5AddonService.IsPinnedLocalBuild(version));

    [Theory]
    [InlineData(Dlss5DeploymentMode.NativeDirectX12, true, true)]
    [InlineData(Dlss5DeploymentMode.NativeVulkan, true, false)]
    [InlineData(Dlss5DeploymentMode.NativeDirectX11, true, false)]
    [InlineData(Dlss5DeploymentMode.Dx9Feeder, false, false)]
    [InlineData(Dlss5DeploymentMode.Dx12Feeder, false, false)]
    public void ExperimentalOptiScalerRoutesRequireTheRightNativeDlssContract(Dlss5DeploymentMode mode, bool normal, bool split)
    {
        Assert.Equal(normal, Dlss5ComponentService.SupportsOptiScalerNr(mode, true, false));
        Assert.Equal(split, Dlss5ComponentService.SupportsOptiScalerNr(mode, true, true));
        Assert.False(Dlss5ComponentService.SupportsOptiScalerNr(mode, false, false));
        Assert.False(Dlss5ComponentService.SupportsOptiScalerNr(mode, false, true));
    }

    [Fact]
    public void LatestOptiScalerProfileIsPinnedAndSplitOnlyKeysStayWithTheSplitFork()
    {
        Assert.Equal("0.2.0", Dlss5ComponentService.OptiScalerNrVersion);
        Assert.False(Dlss5ComponentService.ShouldWriteOptiScalerSplitKeys(Dlss5InstallProfile.OptiScalerNeuralRendering));
        Assert.True(Dlss5ComponentService.ShouldWriteOptiScalerSplitKeys(Dlss5InstallProfile.OptiScalerNrBeforeSr));

        var root = Directory.CreateTempSubdirectory("adas-opti-02-").FullName;
        var path = Path.Combine(root, "OptiScaler.ini");
        try
        {
            File.WriteAllText(path, "[Upscalers]\nDx11Upscaler=auto\n[DlssNr]\nEnabled=auto\nSplitPipeline=true\nSplitIncludeRR=true\n");
            var ini = IniTextDocument.Load(path);
            Dlss5ComponentService.ConfigureOptiScalerNrIni(
                ini, Dlss5DeploymentMode.NativeDirectX11, Dlss5InstallProfile.OptiScalerNeuralRendering);
            ini.Save(path);
            ini = IniTextDocument.Load(path);

            Assert.True(ini.TryGetValue("Upscalers", "Dx11Upscaler", out var upscaler));
            Assert.Equal("dlss_12", upscaler.Text);
            Assert.True(ini.TryGetValue("DlssNr", "Enabled", out var enabled));
            Assert.Equal("true", enabled.Text);
            Assert.False(ini.TryGetValue("DlssNr", "SplitPipeline", out _));
            Assert.False(ini.TryGetValue("DlssNr", "SplitIncludeRR", out _));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(Dlss5InstallProfile.LatestFeederBeta, Dlss5DeploymentMode.Dx11Feeder, "Feeder 0.12.1-beta.1", true)]
    [InlineData(Dlss5InstallProfile.LatestFeederBeta, Dlss5DeploymentMode.Dx11Feeder, "Feeder 0.13.1-beta.1", true)]
    [InlineData(Dlss5InstallProfile.LatestFeederBeta, Dlss5DeploymentMode.Dx11Feeder, "Feeder 0.14.0-beta.1", false)]
    [InlineData(Dlss5InstallProfile.StandaloneAio, Dlss5DeploymentMode.NativeDirectX12, "Standalone AIO 1.7.24", true)]
    [InlineData(Dlss5InstallProfile.StandaloneAio, Dlss5DeploymentMode.NativeDirectX12, "Standalone AIO 2.0.3", true)]
    [InlineData(Dlss5InstallProfile.StandaloneAio, Dlss5DeploymentMode.NativeDirectX12, "Standalone AIO 2.0.4-experimental.1", false)]
    [InlineData(Dlss5InstallProfile.OptiScalerNeuralRendering, Dlss5DeploymentMode.NativeDirectX12, "OptiScaler NR 0.1.2", true)]
    [InlineData(Dlss5InstallProfile.OptiScalerNeuralRendering, Dlss5DeploymentMode.NativeDirectX12, "OptiScaler NR 0.2.0", false)]
    [InlineData(Dlss5InstallProfile.MaximumQuality, Dlss5DeploymentMode.NativeDirectX11, "Bridge v1.4.7", true)]
    [InlineData(Dlss5InstallProfile.MaximumQuality, Dlss5DeploymentMode.NativeDirectX11, "Bridge v1.4.8", true)]
    [InlineData(Dlss5InstallProfile.MaximumQuality, Dlss5DeploymentMode.NativeDirectX11, "Bridge v1.4.11", false)]
    public void DashboardFlagsOnlySupersededManagedComponentSets(
        Dlss5InstallProfile profile, Dlss5DeploymentMode mode, string version, bool expected)
        => Assert.Equal(expected, Dlss5ComponentService.IsComponentUpdateAvailable(new Dlss5InstallRecord
        {
            Profile = profile,
            Mode = mode,
            ComponentVersion = version,
        }));

    [Fact]
    public void ExclusivePipelinesCanRepairButMustBeRemovedBeforeSwitching()
    {
        foreach (var profile in new[] { Dlss5InstallProfile.StandaloneAio, Dlss5InstallProfile.OptiScalerNeuralRendering, Dlss5InstallProfile.OptiScalerNrBeforeSr })
        {
            Assert.False(Dlss5ComponentService.RequiresPipelineRemoval(null, profile));
            Assert.False(Dlss5ComponentService.RequiresPipelineRemoval(profile, profile));
            Assert.True(Dlss5ComponentService.RequiresPipelineRemoval(profile, Dlss5InstallProfile.MaximumQuality));
            Assert.True(Dlss5ComponentService.RequiresPipelineRemoval(Dlss5InstallProfile.MaximumQuality, profile));
        }
        Assert.True(Dlss5ComponentService.RequiresPipelineRemoval(Dlss5InstallProfile.OptiScalerNeuralRendering, Dlss5InstallProfile.OptiScalerNrBeforeSr));
    }

    [Fact]
    public void OptiScalerArchiveLayoutRetainsDependenciesAndNeverRunsSetup()
    {
        Assert.Equal("dxgi.dll", Dlss5ComponentService.OptiScalerNrDestination("OptiScaler.dll"));
        Assert.Equal(Path.Combine("OptiScaler", "libxess.dll"), Dlss5ComponentService.OptiScalerNrDestination("OptiScaler\\libxess.dll"));
        Assert.Null(Dlss5ComponentService.OptiScalerNrDestination("setup_windows.bat"));
        Assert.Null(Dlss5ComponentService.OptiScalerNrDestination("setup_linux.sh"));
        Assert.Throws<InvalidDataException>(() => Dlss5ComponentService.OptiScalerNrDestination("../evil.dll"));
        Assert.Throws<InvalidDataException>(() => Dlss5ComponentService.OptiScalerNrDestination("C:/evil.dll"));
        Assert.Equal("winmm.dll", Dlss5ComponentService.OptiScalerNrProxy(Dlss5DeploymentMode.NativeVulkan));
    }

    [Fact]
    public void OptiScalerConflictPreflightPreservesTheOtherPipeline()
    {
        var root = Directory.CreateTempSubdirectory("adas-nr-conflict-").FullName;
        try
        {
            var path = Path.Combine(root, "renodx-dlss.addon64");
            File.WriteAllText(path, "existing user add-on");
            Assert.Throws<InvalidOperationException>(() => Dlss5ComponentService.ValidateOptiScalerNrConflicts(root, null));
            Assert.Equal("existing user add-on", File.ReadAllText(path));
            Assert.False(Directory.Exists(Path.Combine(root, ".adas")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void StandardOptiScalerCannotReplaceManagedNeuralRenderingFork()
    {
        var root = Directory.CreateTempSubdirectory("adas-nr-owner-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".adas"));
            File.WriteAllText(Path.Combine(root, ".adas", "dlss5-install.json"),
                System.Text.Json.JsonSerializer.Serialize(new Dlss5InstallRecord
                {
                    Mode = Dlss5DeploymentMode.NativeDirectX12,
                    Profile = Dlss5InstallProfile.OptiScalerNrBeforeSr,
                }));
            Assert.Throws<InvalidOperationException>(() => OptiScalerService.EnsureNotManagedNeuralRendering(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
