using RenoDXCommander.Models;
using RenoDXCommander.Services;
using System.Reflection;
using System.Text;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class Dlss5SuiteTests
{
    private static Dlss5Probe ReadyProbe(GraphicsApiType api, bool hasNativeDlss = false) => new()
    {
        GameName = "Test Game",
        DeploymentPath = @"C:\Games\Test",
        GraphicsApi = api,
        Is64Bit = true,
        GpuName = "NVIDIA GeForce RTX 4080",
        HasNativeDlss = hasNativeDlss,
        HasReShadeAddonSupport = true,
        HasRenoDx5Addon = true,
        HasNvngxDlssNr = true,
        HasNvngxDlss = true,
        HasMotionVectorProvider = true,
        HasLegacyTranslation = true,
    };

    [Fact]
    public void Assess_DirectX8AllowsOnlyThe32BitTranslatedRoute()
    {
        var probe = ReadyProbe(GraphicsApiType.DirectX8) with { Is64Bit = false };
        Assert.True(Dlss5CompatibilityService.Assess(probe, true).CanInstall);
        Assert.False(Dlss5CompatibilityService.Assess(probe with { Is64Bit = true }, true).CanInstall);
    }

    [Fact]
    public void Assess_DirectX12_UsesNativePath()
    {
        var result = Dlss5CompatibilityService.Assess(ReadyProbe(GraphicsApiType.DirectX12, hasNativeDlss: true), true);

        Assert.True(result.CanInstall);
        Assert.Equal(Dlss5DeploymentMode.NativeDirectX12, result.Mode);
    }

    [Fact]
    public void Assess_RequiresExplicitSinglePlayerConfirmation()
    {
        var result = Dlss5CompatibilityService.Assess(
            ReadyProbe(GraphicsApiType.DirectX12, hasNativeDlss: true));

        Assert.False(result.CanInstall);
        Assert.False(result.SinglePlayerConfirmed);
    }

    [Fact]
    public void Assess_DirectX11WithDlss_UsesUnifiedNativePath()
    {
        var result = Dlss5CompatibilityService.Assess(ReadyProbe(GraphicsApiType.DirectX11, hasNativeDlss: true), true);

        Assert.True(result.CanInstall);
        Assert.Equal(Dlss5DeploymentMode.NativeDirectX11, result.Mode);
    }

    [Fact]
    public void Assess_DirectX11WithoutDlss_UsesFeeder()
    {
        var result = Dlss5CompatibilityService.Assess(ReadyProbe(GraphicsApiType.DirectX11), true);

        Assert.True(result.CanInstall);
        Assert.Equal(Dlss5DeploymentMode.Dx11Feeder, result.Mode);
    }

    [Fact]
    public void Assess_FeederReportsMotionVectorProviderAsGuidedRequirement()
    {
        var probe = ReadyProbe(GraphicsApiType.DirectX11);
        probe = probe with { HasMotionVectorProvider = false };

        var result = Dlss5CompatibilityService.Assess(probe, true);

        Assert.True(result.CanInstall);
        Assert.Contains(result.MissingRequirements, value => value.Contains("motion-vector provider", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.MissingRequirements, value => value.Contains("LaunchPad", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4090")]
    [InlineData("NVIDIA RTX 4060 Laptop GPU")]
    [InlineData("NVIDIA GeForce RTX 2080 Ti")]
    [InlineData("NVIDIA GeForce RTX 3090")]
    [InlineData("NVIDIA GeForce RTX 5080")]
    public void IsSupportedGpu_AcceptsSupportedRtxSeries(string gpuName)
        => Assert.True(Dlss5CompatibilityService.IsSupportedGpu(gpuName));

    [Theory]
    [InlineData("NVIDIA GeForce GTX 1080")]
    [InlineData("NVIDIA GeForce RTX 6090")]
    [InlineData("AMD Radeon RX 7900 XTX")]
    [InlineData("")]
    public void IsSupportedGpu_RejectsOtherHardware(string gpuName)
        => Assert.False(Dlss5CompatibilityService.IsSupportedGpu(gpuName));

    [Fact]
    public void Assess_AntiCheatEvidenceHardBlocksInstall()
    {
        var probe = ReadyProbe(GraphicsApiType.DirectX12) with
        {
            AntiCheatEvidence = new[] { "EasyAntiCheat_EOS.exe" },
        };

        var result = Dlss5CompatibilityService.Assess(probe, true);

        Assert.False(result.CanInstall);
        Assert.Contains(result.BlockingReasons, value => value.Contains("anti-cheat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Assess_32BitDirectX11GameUsesHostedFeeder()
    {
        var result = Dlss5CompatibilityService.Assess(
            ReadyProbe(GraphicsApiType.DirectX11) with { Is64Bit = false }, true);

        Assert.True(result.CanInstall);
        Assert.Equal(Dlss5DeploymentMode.Dx11Feeder, result.Mode);
        Assert.False(result.Is64Bit);
    }

    [Fact]
    public void Assess_DirectX12WithoutNativeDlssUsesFeeder()
    {
        var result = Dlss5CompatibilityService.Assess(ReadyProbe(GraphicsApiType.DirectX12), true);

        Assert.True(result.CanInstall);
        Assert.Equal(Dlss5DeploymentMode.Dx12Feeder, result.Mode);
    }

    [Theory]
    [InlineData("6.3.3", "6.8.0", true)]
    [InlineData("6.3.3", "6.3.3", false)]
    [InlineData("Stable", "6.8.0", false)]
    public void ShouldRefreshReShade_OnlyReplacesMismatchedPinnedVersion(
        string channel, string installedVersion, bool expected)
        => Assert.Equal(expected, Dlss5ComponentService.ShouldRefreshReShade(channel, installedVersion));

    [Fact]
    public void HasOriginalRuntime_IgnoresRuntimeInstalledOnlyBySuite()
    {
        var root = @"C:\Games\Test";
        var runtime = Path.Combine(root, "nvngx_dlss.dll");
        var record = new Dlss5InstallRecord();
        record.InstalledHashes[runtime] = new string('A', 64);
        record.OriginalBackups[runtime] = null;

        Assert.False(Dlss5CompatibilityService.HasOriginalRuntime(
            root, new[] { runtime }, record, "nvngx_dlss.dll"));
    }

    [Fact]
    public void DetectDynamicApiHints_FindsRuntimeAndEntryPointAcrossReadBoundary()
    {
        const int scanChunkSize = 1024 * 1024;
        var bytes = new byte[scanChunkSize + 128];
        Encoding.ASCII.GetBytes("D3D12.DLL").CopyTo(bytes, scanChunkSize - 4);
        Encoding.ASCII.GetBytes("D3D12CreateDevice").CopyTo(bytes, scanChunkSize + 32);
        using var stream = new MemoryStream(bytes);

        var result = GraphicsApiDetector.DetectDynamicApiHints(stream);

        Assert.Contains(GraphicsApiType.DirectX12, result);
    }

    [Fact]
    public void DetectDynamicApiHints_RequiresRuntimeAndEntryPointPair()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("d3d12.dll without its factory export"));

        var result = GraphicsApiDetector.DetectDynamicApiHints(stream);

        Assert.DoesNotContain(GraphicsApiType.DirectX12, result);
    }

    [Fact]
    public void Detect_FallsBackToDynamicHintsForValidPeWithoutGraphicsImports()
    {
        var bytes = new byte[8192];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3c);
        bytes[0x80] = (byte)'P';
        bytes[0x81] = (byte)'E';
        const int coffOffset = 0x84;
        BitConverter.GetBytes((ushort)0).CopyTo(bytes, coffOffset + 2);
        BitConverter.GetBytes((ushort)240).CopyTo(bytes, coffOffset + 16);
        const int optionalHeaderOffset = coffOffset + 20;
        BitConverter.GetBytes((ushort)0x20b).CopyTo(bytes, optionalHeaderOffset);
        Encoding.ASCII.GetBytes("d3d12.dll").CopyTo(bytes, 5000);
        Encoding.ASCII.GetBytes("D3D12CreateDevice").CopyTo(bytes, 5100);

        var exePath = Path.Combine(Path.GetTempPath(), $"adas-dynamic-api-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(exePath, bytes);

            Assert.Equal(GraphicsApiType.DirectX12, GraphicsApiDetector.Detect(exePath));
        }
        finally
        {
            File.Delete(exePath);
        }
    }

    [Fact]
    public void Assess_VulkanUsesFeeder()
    {
        var result = Dlss5CompatibilityService.Assess(ReadyProbe(GraphicsApiType.Vulkan), true);

        Assert.True(result.CanInstall);
        Assert.Equal(Dlss5DeploymentMode.VulkanFeeder, result.Mode);
    }

    [Fact]
    public void Assess_VulkanWithNativeDlssUsesNativeMirror()
    {
        var result = Dlss5CompatibilityService.Assess(
            ReadyProbe(GraphicsApiType.Vulkan, hasNativeDlss: true), true);

        Assert.True(result.CanInstall);
        Assert.Equal(Dlss5DeploymentMode.NativeVulkan, result.Mode);
        Assert.False(Dlss5CompatibilityService.IsFeederMode(result.Mode));
    }

    [Fact]
    public void Assess_OpenGlUsesFeeder()
    {
        var result = Dlss5CompatibilityService.Assess(ReadyProbe(GraphicsApiType.OpenGL), true);

        Assert.True(result.CanInstall);
        Assert.Equal(Dlss5DeploymentMode.OpenGlFeeder, result.Mode);
    }

    [Fact]
    public void Assess_DirectX9UsesFeederWithWrapperGuidance()
    {
        var result = Dlss5CompatibilityService.Assess(
            ReadyProbe(GraphicsApiType.DirectX9) with { HasLegacyTranslation = false }, true);

        Assert.True(result.CanInstall);
        Assert.Equal(Dlss5DeploymentMode.Dx9Feeder, result.Mode);
        Assert.Contains(result.MissingRequirements, value => value.Contains("dgVoodoo2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Assess_DirectX9WithConfirmedDgVoodooCrashUsesPerGameDxvkFallback()
    {
        var result = Dlss5CompatibilityService.Assess(
            ReadyProbe(GraphicsApiType.DirectX9) with
            {
                Is64Bit = false,
                PreferDxvkForDirectX9 = true,
                HasLegacyTranslation = false,
            }, true);

        Assert.True(result.CanInstall);
        Assert.Equal(Dlss5DeploymentMode.Dx9ViaDxvkFeeder, result.Mode);
        Assert.Contains(result.MissingRequirements, value => value.Contains("DXVK", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Assess_64BitDirectX10UsesDxvkVulkanFeeder()
    {
        var result = Dlss5CompatibilityService.Assess(
            ReadyProbe(GraphicsApiType.DirectX10) with { HasLegacyTranslation = false }, true);

        Assert.True(result.CanInstall);
        Assert.Equal(Dlss5DeploymentMode.Dx10ViaDxvkFeeder, result.Mode);
        Assert.Contains(result.MissingRequirements, value => value.Contains("DXVK", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Assess_32BitDirectX10UsesNativeRelayWithoutDxvk()
    {
        var result = Dlss5CompatibilityService.Assess(
            ReadyProbe(GraphicsApiType.DirectX10) with { Is64Bit = false, HasLegacyTranslation = false }, true);

        Assert.True(result.CanInstall);
        Assert.Equal(Dlss5DeploymentMode.Dx10Feeder, result.Mode);
        Assert.DoesNotContain(result.MissingRequirements, value => value.Contains("DXVK", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Assess_AmbiguousDestinationHardBlocksInstall()
    {
        var result = Dlss5CompatibilityService.Assess(
            ReadyProbe(GraphicsApiType.DirectX11) with
            {
                DeploymentPath = null,
                HasAmbiguousDeploymentPath = true,
            }, true);

        Assert.False(result.CanInstall);
        Assert.Contains(result.BlockingReasons, value => value.Contains("multiple", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnumerateFilesSafe_DoesNotDescendIntoReparsePoints()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-reparse-root-{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"adas-reparse-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        var link = Path.Combine(root, "linked-game");
        var outsideFile = Path.Combine(outside, "dxgi.dll");
        File.WriteAllBytes(outsideFile, new byte[] { (byte)'M', (byte)'Z' });
        try
        {
            Directory.CreateSymbolicLink(link, outside);

            var files = Dlss5CompatibilityService.EnumerateFilesSafe(root, maxDepth: 3).ToArray();

            Assert.DoesNotContain(files, path => path.Equals(outsideFile, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void FeederDefaults_ExposeEveryUpstreamConfigKey()
    {
        var defaults = Dlss5ComponentService.GetDefaults(Dlss5DeploymentMode.Dx11Feeder);

        Assert.Equal(
            new[] { "create_delay", "depth_inverted", "enabled", "flags", "hdr", "host_window", "log_frames", "mode", "mv_scale_x", "mv_scale_y", "preset", "rebuild", "reset_every", "warmup_rebuild", "work_resolution" },
            defaults.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void RequiredFeederComponents_SelectCorrectArchitectureAndHost()
    {
        Assert.Equal(
            new[] { "dlss5-feed.addon64", "DLSS5_Feed.fx" },
            Dlss5ComponentService.GetRequiredComponentNames(Dlss5DeploymentMode.Dx11Feeder, is64Bit: true));
        Assert.Equal(
            new[] { "dlss5-feed.addon32", "dlss5-feed-host64.exe", "DLSS5_Feed.fx" },
            Dlss5ComponentService.GetRequiredComponentNames(Dlss5DeploymentMode.Dx11Feeder, is64Bit: false));
        Assert.Equal(
            new[] { "dlss5-feed.addon64", "DLSS5_Feed.fx", "feed-vk-layer.zip" },
            Dlss5ComponentService.GetRequiredComponentNames(Dlss5DeploymentMode.VulkanFeeder, is64Bit: true));
        Assert.Equal(
            new[] { "dlss5-feed.addon32", "dlss5-feed-host64.exe", "DLSS5_Feed.fx", "feed-vk-layer.zip" },
            Dlss5ComponentService.GetRequiredComponentNames(Dlss5DeploymentMode.Dx10ViaDxvkFeeder, is64Bit: false));
        Assert.Equal(
            new[] { "dlss5-feed.addon32", "dlss5-feed-host64.exe", "DLSS5_Feed.fx", "feed-vk-layer.zip" },
            Dlss5ComponentService.GetRequiredComponentNames(Dlss5DeploymentMode.Dx9ViaDxvkFeeder, is64Bit: false));
        Assert.Equal(
            new[] { "dlss5-feed.addon32", "dlss5-feed-host64.exe", "DLSS5_Feed.fx" },
            Dlss5ComponentService.GetRequiredComponentNames(Dlss5DeploymentMode.Dx10Feeder, is64Bit: false));
    }

    [Fact]
    public void CompatibilityMatrix_PinsEachApiToItsTestedComponentSet()
    {
        var dx12 = Dlss5ComponentService.GetCompatibilityPlan(Dlss5DeploymentMode.NativeDirectX12, is64Bit: true);
        var dx11 = Dlss5ComponentService.GetCompatibilityPlan(Dlss5DeploymentMode.NativeDirectX11, is64Bit: true);
        var vulkan = Dlss5ComponentService.GetCompatibilityPlan(Dlss5DeploymentMode.NativeVulkan, is64Bit: true);
        var feeder = Dlss5ComponentService.GetCompatibilityPlan(Dlss5DeploymentMode.Dx11Feeder, is64Bit: true);

        Assert.Equal(Dlss5RenoDxPackage.Native470, dx12.RenoDxPackage);
        Assert.False(dx12.InstallDx11Bridge);
        Assert.Equal(Dlss5RenoDxPackage.Native470, dx11.RenoDxPackage);
        Assert.True(dx11.InstallDx11Bridge);
        Assert.False(dx11.InstallFeeder);
        Assert.Equal(Dlss5RenoDxPackage.Native470, vulkan.RenoDxPackage);
        Assert.True(vulkan.InstallDx11Bridge);
        Assert.False(vulkan.InstallFeeder);
        Assert.Equal(Dlss5RenoDxPackage.Feeder455, feeder.RenoDxPackage);
        Assert.True(feeder.InstallFeeder);
        Assert.False(feeder.InstallDx11Bridge);
        Assert.False(feeder.PatchFeederForUnifiedName);

        var experimental = Dlss5ComponentService.GetCompatibilityPlan(
            Dlss5DeploymentMode.Dx11Feeder,
            is64Bit: true,
            Dlss5InstallProfile.ExperimentalUnified);
        Assert.Equal(Dlss5RenoDxPackage.ExperimentalUnified, experimental.RenoDxPackage);
        Assert.False(experimental.InstallDx11Bridge);
        Assert.False(experimental.InstallFeeder);
        Assert.False(experimental.PatchFeederForUnifiedName);

        var beta = Dlss5ComponentService.GetCompatibilityPlan(
            Dlss5DeploymentMode.Dx11Feeder,
            is64Bit: true,
            Dlss5InstallProfile.LatestFeederBeta);
        Assert.Equal(Dlss5RenoDxPackage.Native470, beta.RenoDxPackage);
        Assert.True(beta.InstallFeeder);
        Assert.True(beta.UsesLatestFeederBeta);
        Assert.Contains("0.14.0-beta.1", beta.ProfileName);
        Assert.False(beta.PatchFeederForUnifiedName);
    }

    [Theory]
    [InlineData(Dlss5DeploymentMode.OpenGlFeeder, "opengl32.dll")]
    [InlineData(Dlss5DeploymentMode.Dx11Feeder, "dxgi.dll")]
    [InlineData(Dlss5DeploymentMode.Dx12Feeder, "dxgi.dll")]
    public void ReShadeRuntimeName_MatchesGraphicsApi(Dlss5DeploymentMode mode, string expected)
        => Assert.Equal(expected, Dlss5ComponentService.GetReShadeFileName(mode));

    [Fact]
    public void UnifiedDirectX9_UsesTheNativeD3d9ProxyWithoutFeederTranslation()
    {
        var plan = Dlss5ComponentService.GetCompatibilityPlan(
            Dlss5DeploymentMode.Dx9Feeder,
            is64Bit: true,
            Dlss5InstallProfile.ExperimentalUnified);

        Assert.False(plan.InstallFeeder);
        Assert.Equal(
            "d3d9.dll",
            Dlss5ComponentService.GetReShadeFileName(
                Dlss5DeploymentMode.Dx9Feeder,
                Dlss5InstallProfile.ExperimentalUnified));
    }

    [Fact]
    public void BundledFeeder_SurfacesMasterSwitchAndCompleteRenoDxNeuralControls()
    {
        var shaderPath = FindRepositoryFile("RenoDXCommander", "Assets", "DLSS5", "DLSS5_Feed.fx");
        var shader = File.ReadAllText(shaderPath);
        var feeder32Path = FindRepositoryFile("RenoDXCommander", "Assets", "DLSS5", "dlss5-feed.addon32");
        var feeder32Strings = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(feeder32Path));
        Assert.Contains("DLSS5_MV_PROVIDER", shader, StringComparison.Ordinal);
        Assert.Contains("LumeniteFX Kernel", shader, StringComparison.Ordinal);
        Assert.Contains("technique DLSS5_Feed", shader, StringComparison.Ordinal);
        Assert.Contains("DLSS 5 Feed (32-bit) 0.7.0", feeder32Strings, StringComparison.Ordinal);
        Assert.Contains("DLSS 5 host settings", feeder32Strings, StringComparison.Ordinal);
        Assert.Contains("Work resolution", feeder32Strings, StringComparison.Ordinal);
        Assert.Contains("OpenGL", feeder32Strings, StringComparison.Ordinal);
        Assert.Contains("NR Intensity", feeder32Strings, StringComparison.Ordinal);
        Assert.Contains("Local Tone Strength", feeder32Strings, StringComparison.Ordinal);
        Assert.Contains("Local Structure Strength", feeder32Strings, StringComparison.Ordinal);
        Assert.Contains("Skin Structure Strength", feeder32Strings, StringComparison.Ordinal);
        Assert.Contains("Automatic Mask", feeder32Strings, StringComparison.Ordinal);
        Assert.Contains("NR UI Correction", feeder32Strings, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dlss5-feed.addon64")]
    [InlineData("dlss5-feed-host64.exe")]
    public void FeederCompatibilityPatch_RecognizesUnifiedRenoDxFileName(string assetName)
    {
        var assetPath = FindRepositoryFile("RenoDXCommander", "Assets", "DLSS5", assetName);
        var original = File.ReadAllBytes(assetPath);

        var patched = Dlss5ComponentService.PatchRenoDxAddonProbeName((byte[])original.Clone());
        var patchedText = Encoding.ASCII.GetString(patched);

        Assert.Equal(original.Length, patched.Length);
        Assert.Contains("renodx-dlss.addon64\0", patchedText, StringComparison.Ordinal);
        Assert.DoesNotContain("renodx-dlss5.addon64\0", patchedText, StringComparison.Ordinal);
        Assert.Equal((byte)'M', patched[0]);
        Assert.Equal((byte)'Z', patched[1]);
    }

    [Theory]
    [InlineData("renodx-dlss5.addon64", "renodx-dlss5-4.55.addon64")]
    [InlineData("renodx-dlss5(2).addon64", "renodx-dlss5-4.55.addon64")]
    [InlineData("dlss5-feed-32bit.addon32", "dlss5-feed.addon32")]
    public void NormalizeComponentFileName_UsesCanonicalDeploymentNames(string source, string expected)
        => Assert.Equal(expected, Dlss5ComponentService.NormalizeComponentFileName(source));

    [Fact]
    public void MigrateLegacyLaunchPad_PreservesRemovedFilesInAdasBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-launchpad-migration-{Guid.NewGuid():N}");
        var shaders = Path.Combine(root, "reshade-shaders", "Shaders");
        var includes = Path.Combine(shaders, "MartysMods");
        Directory.CreateDirectory(includes);
        File.WriteAllText(Path.Combine(shaders, "MartysMods_LAUNCHPAD.fx"), "legacy");
        File.WriteAllText(Path.Combine(includes, "legacy.fxh"), "legacy");
        try
        {
            var preserved = Dlss5ComponentService.MigrateLegacyLaunchPad(root);

            Assert.Equal(2, preserved.Count);
            Assert.False(File.Exists(Path.Combine(shaders, "MartysMods_LAUNCHPAD.fx")));
            Assert.False(Directory.Exists(includes));
            Assert.All(preserved, path => Assert.True(File.Exists(path) || Directory.Exists(path)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void NativeUnifiedMode_HasNoSeparateTransportConfig()
    {
        var defaults = Dlss5ComponentService.GetDefaults(Dlss5DeploymentMode.NativeDirectX11);

        Assert.Empty(defaults);
    }

    [Fact]
    public void LatestFeederBeta_ExposesRecoverableGpuTimeout()
    {
        var defaults = Dlss5ComponentService.GetDefaults(
            Dlss5DeploymentMode.Dx11Feeder,
            Dlss5InstallProfile.LatestFeederBeta);

        Assert.Equal("2000", defaults["gpu_timeout_ms"]);
    }

    [Theory]
    [InlineData("RenoDX DLSS5", true)]
    [InlineData("DLSS5 Tool", true)]
    [InlineData("DLSS Tool (ShortFuse)", true)]
    [InlineData("RenoDX DLSS", false)]
    public void LegacyRhiDlssSelections_AreRetiredWithoutRemovingCurrentPackage(string name, bool expected)
        => Assert.Equal(expected, AddonPackService.IsLegacyManagedDlssPackageName(name));

    [Fact]
    public void DlssNrInspect_MissingFile_IsMissingWithoutMutation()
    {
        var service = new DlssNrRepairService(new NoopCrashReporter());
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), DlssNrRepairService.DllName);

        var state = service.Inspect(missing);

        Assert.Equal(DlssNrClassification.Missing, state.Classification);
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public void ResolveDeploymentPath_ManyLauncherExecutablesDoNotOutscoreDxgiEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-path-test-{Guid.NewGuid():N}");
        var binary = Path.Combine(root, "Game", "Binaries", "Win64");
        Directory.CreateDirectory(binary);
        try
        {
            for (var index = 0; index < 20; index++)
                File.WriteAllBytes(Path.Combine(root, $"launcher{index}.exe"), new byte[] { (byte)'M', (byte)'Z' });
            File.WriteAllBytes(Path.Combine(binary, "game.exe"), new byte[] { (byte)'M', (byte)'Z' });
            File.WriteAllBytes(Path.Combine(binary, "dxgi.dll"), new byte[] { (byte)'M', (byte)'Z' });

            var result = Dlss5CompatibilityService.ResolveDeploymentPath(root);

            Assert.Equal(Dlss5PathResolutionKind.Resolved, result.Kind);
            Assert.Equal(binary, result.Path);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ResolveDeploymentPath_EqualStrongEvidenceIsAmbiguous()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-path-test-{Guid.NewGuid():N}");
        var first = Path.Combine(root, "A");
        var second = Path.Combine(root, "B");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            File.WriteAllBytes(Path.Combine(first, "dxgi.dll"), new byte[] { (byte)'M', (byte)'Z' });
            File.WriteAllBytes(Path.Combine(second, "dxgi.dll"), new byte[] { (byte)'M', (byte)'Z' });

            var result = Dlss5CompatibilityService.ResolveDeploymentPath(root);

            Assert.Equal(Dlss5PathResolutionKind.Ambiguous, result.Kind);
            Assert.Null(result.Path);
            Assert.Equal(2, result.Candidates.Count);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FindInstalledDeploymentPath_FindsNestedSuiteRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-record-root-{Guid.NewGuid():N}");
        var binary = Path.Combine(root, "Game", "Binaries", "Win64");
        Directory.CreateDirectory(binary);
        try
        {
            Dlss5ComponentService.SaveRecord(binary, new Dlss5InstallRecord());

            Assert.Equal(binary, Dlss5ComponentService.FindInstalledDeploymentPath(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RemoveOtherManagedDeployments_RemovesStaleLauncherInstall()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-relocate-root-{Guid.NewGuid():N}");
        var binary = Path.Combine(root, "Bin64");
        Directory.CreateDirectory(binary);
        try
        {
            Dlss5ComponentService.SaveRecord(root, new Dlss5InstallRecord());
            Dlss5ComponentService.SaveRecord(binary, new Dlss5InstallRecord());
            var service = new Dlss5ComponentService(
                new HttpClient(), new NoopCrashReporter(), null!, null!, null!, null!);

            var errors = service.RemoveOtherManagedDeployments(root, binary);

            Assert.Empty(errors);
            Assert.Null(Dlss5ComponentService.LoadRecord(root));
            Assert.NotNull(Dlss5ComponentService.LoadRecord(binary));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TrackedUninstall_PreservesModifiedFileAndRestoresOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-uninstall-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source.addon64");
        var destination = Path.Combine(root, "addons", "component.addon64");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(source, "suite version");
        File.WriteAllText(destination, "original version");
        try
        {
            var record = new Dlss5InstallRecord();
            Dlss5ComponentService.InstallTrackedFile(source, destination, root, record);
            File.WriteAllText(destination, "user modified version");

            var errors = Dlss5ComponentService.UninstallTrackedFiles(root, new NoopCrashReporter());

            Assert.Empty(errors);
            Assert.Equal("original version", File.ReadAllText(destination));
            var preserved = Directory.GetFiles(Path.Combine(root, ".adas", "preserved"), "*.modified");
            Assert.Single(preserved);
            Assert.Equal("user modified version", File.ReadAllText(preserved[0]));
            Assert.Null(Dlss5ComponentService.LoadRecord(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void TrackedUninstall_KeepsRecoveryRecordWhenAFileIsLocked()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-uninstall-lock-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source.addon64");
        var destination = Path.Combine(root, "component.addon64");
        Directory.CreateDirectory(root);
        File.WriteAllText(source, "suite version");
        try
        {
            var record = new Dlss5InstallRecord();
            Dlss5ComponentService.InstallTrackedFile(source, destination, root, record);
            IReadOnlyList<string> errors;
            using (File.Open(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
                errors = Dlss5ComponentService.UninstallTrackedFiles(root, new NoopCrashReporter());

            Assert.NotEmpty(errors);
            Assert.NotNull(Dlss5ComponentService.LoadRecord(root));
            Assert.Empty(Dlss5ComponentService.UninstallTrackedFiles(root, new NoopCrashReporter()));
            Assert.Null(Dlss5ComponentService.LoadRecord(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void EnumerateCandidateFiles_RespectsNonRecursiveSourceScan()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-source-scan-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        var top = Path.Combine(root, "top.dll");
        var deep = Path.Combine(nested, "deep.dll");
        File.WriteAllText(top, "top");
        File.WriteAllText(deep, "deep");
        try
        {
            var files = DlssNrRepairService.EnumerateCandidateFiles(root, recurse: false).ToArray();

            Assert.Contains(top, files);
            Assert.DoesNotContain(deep, files);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DownloadFileAsync_ClosesTemporaryStreamBeforeReplacingDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-download-{Guid.NewGuid():N}");
        var destination = Path.Combine(root, "component.addon64");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(destination, "old payload");
        using var http = new HttpClient(new StaticContentHandler("new payload"));
        var service = new Dlss5ComponentService(http, null!, null!, null!, null!, null!);
        var method = typeof(Dlss5ComponentService).GetMethod(
            "DownloadFileAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            var task = (Task)method.Invoke(service, new object[]
            {
                "https://example.invalid/component.addon64",
                destination,
                CancellationToken.None,
            })!;

            await task;

            Assert.Equal("new payload", await File.ReadAllTextAsync(destination));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class NoopCrashReporter : ICrashReporter
    {
        public bool VerboseLogging { get; set; }
        public void Log(string message) { }
        public void WriteCrashReport(string source, Exception? ex, bool isTerminating = false, string? note = null) { }
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "RenoDXCommander.sln"))) continue;
            return Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }

    private sealed class StaticContentHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(content),
            });
    }
}
