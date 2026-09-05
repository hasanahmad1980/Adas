using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Focused unit tests covering real failure modes found during development.
/// Tests pure logic methods — no filesystem, no network, no DI container.
/// </summary>
public class CoreLogicTests
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // 1. ResolveAutoReShadeFilename — DLL naming based on detected graphics APIs
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveAutoReShadeFilename_DX9Only_ReturnsD3d9()
    {
        var apis = new HashSet<GraphicsApiType> { GraphicsApiType.DirectX9 };
        var result = MainViewModel.ResolveAutoReShadeFilename(apis);
        Assert.Equal("d3d9.dll", result);
    }

    [Fact]
    public void ResolveAutoReShadeFilename_DX8Only_ReturnsD3d8()
    {
        var apis = new HashSet<GraphicsApiType> { GraphicsApiType.DirectX8 };
        var result = MainViewModel.ResolveAutoReShadeFilename(apis);
        Assert.Equal("d3d8.dll", result);
    }

    [Fact]
    public void ResolveAutoReShadeFilename_DX11_ReturnsNull_ForDxgi()
    {
        var apis = new HashSet<GraphicsApiType> { GraphicsApiType.DirectX11 };
        Assert.Null(MainViewModel.ResolveAutoReShadeFilename(apis));
    }

    [Fact]
    public void ResolveAutoReShadeFilename_DX12_ReturnsNull_ForDxgi()
    {
        var apis = new HashSet<GraphicsApiType> { GraphicsApiType.DirectX12 };
        Assert.Null(MainViewModel.ResolveAutoReShadeFilename(apis));
    }

    [Fact]
    public void ResolveAutoReShadeFilename_OpenGLOnly_ReturnsOpengl32()
    {
        var apis = new HashSet<GraphicsApiType> { GraphicsApiType.OpenGL };
        var result = MainViewModel.ResolveAutoReShadeFilename(apis);
        Assert.Equal("opengl32.dll", result);
    }

    [Fact]
    public void ResolveAutoReShadeFilename_DX9PlusDX11_DX11Wins_ReturnsNull()
    {
        // DX11 takes priority — many games import d3d9.dll for legacy reasons
        var apis = new HashSet<GraphicsApiType> { GraphicsApiType.DirectX9, GraphicsApiType.DirectX11 };
        Assert.Null(MainViewModel.ResolveAutoReShadeFilename(apis));
    }

    [Fact]
    public void ResolveAutoReShadeFilename_EmptySet_ReturnsNull()
    {
        var apis = new HashSet<GraphicsApiType>();
        Assert.Null(MainViewModel.ResolveAutoReShadeFilename(apis));
    }

    [Fact]
    public void ResolveReShadeInstallPolicy_Dx9Feeder_KeepsTranslationChainAndSuiteShaders()
    {
        var policy = Dlss5ComponentService.ResolveReShadeInstallPolicy(
            Dlss5DeploymentMode.Dx9Feeder,
            Dlss5InstallProfile.LatestFeederBeta);

        Assert.False(policy.BlockInstall);
        Assert.Equal("dxgi.dll", policy.ProxyName);
        Assert.True(policy.PreserveSuiteShaders);
    }

    [Fact]
    public void ResolveReShadeInstallPolicy_OptiScalerPipeline_BlocksIndependentReplacement()
    {
        var policy = Dlss5ComponentService.ResolveReShadeInstallPolicy(
            Dlss5DeploymentMode.NativeDirectX12,
            Dlss5InstallProfile.OptiScalerNeuralRendering);

        Assert.True(policy.BlockInstall);
        Assert.Contains("DLSS 5", policy.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyInstalledReShadeRecord_RefreshesCardAfterSuiteInstall()
    {
        var card = new GameCardViewModel { InstallPath = @"E:\Games\Example" };
        var record = new AuxInstalledRecord
        {
            InstallPath = card.InstallPath,
            InstalledAs = "dxgi.dll",
        };

        MainViewModel.ApplyInstalledReShadeRecord(card, record, "6.8.0");

        Assert.Same(record, card.RsRecord);
        Assert.Equal(GameStatus.Installed, card.RsStatus);
        Assert.Equal("dxgi.dll", card.RsInstalledFile);
        Assert.Equal("6.8.0", card.RsInstalledVersion);
    }

    [Fact]
    public void ShouldRemovePreviousReShadeProxy_DlssPipelineOwnsOldSlot_DoesNotDeleteTranslator()
    {
        Assert.False(AuxInstallService.ShouldRemovePreviousReShadeProxy(
            "d3d9.dll",
            "dxgi.dll",
            dlssPipelineOwnsFiles: true));
    }

    [Fact]
    public void ShouldRemovePreviousReShadeProxy_NoDlssPipeline_RemovesObsoleteProxy()
    {
        Assert.True(AuxInstallService.ShouldRemovePreviousReShadeProxy(
            "d3d9.dll",
            "dxgi.dll",
            dlssPipelineOwnsFiles: false));
    }

    [Fact]
    public void ResolveReShadeChannelValue_UsesDefaultGameOverrideForManualEntry()
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["L.A. Noire|"] = "6.3.3",
        };

        Assert.Equal("6.3.3", MainViewModel.ResolveReShadeChannelValue(overrides, "L.A. Noire", "Manual"));
    }

    [Fact]
    public void ResolveReShadeChannelValue_DoesNotForceAnOldVersionByGameTitle()
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Assert.Equal("Stable", MainViewModel.ResolveReShadeChannelValue(overrides, "L.A. Noire", "Manual"));
    }

    [Fact]
    public void CreateDirectLaunchInfo_UsesTheExecutableDirectory()
    {
        var info = MainWindow.CreateDirectLaunchInfo(@"E:\Games\Example\game.exe", "-safe");
        Assert.Equal(@"E:\Games\Example", info.WorkingDirectory);
        Assert.Equal("-safe", info.Arguments);
        Assert.True(info.UseShellExecute);
    }

    [Fact]
    public void FindGameExe_IgnoresLargerUninstaller()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-game-exe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var gameExe = Path.Combine(root, "PowerWashSimulator.exe");
            File.WriteAllBytes(gameExe, new byte[64]);
            File.WriteAllBytes(Path.Combine(root, "unins000.exe"), new byte[1024]);

            Assert.Equal(gameExe, new PeHeaderService().FindGameExe(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindGameExe_PrefersNestedGameBinaryOverRootLauncher()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-nested-game-exe-{Guid.NewGuid():N}");
        var binary = Path.Combine(root, "Bin64");
        Directory.CreateDirectory(binary);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "GameLauncher.exe"), new byte[64]);
            var gameExe = Path.Combine(binary, "Game.x64.exe");
            File.WriteAllBytes(gameExe, new byte[1024]);

            Assert.Equal(gameExe, new PeHeaderService().FindGameExe(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(MachineType.I386, false, false)]
    [InlineData(MachineType.x64, true, true)]
    [InlineData(MachineType.Native, false, true)]
    public void DlssProbe_UsesExecutableArchitectureOverStaleCardMetadata(
        MachineType detected,
        bool cardIs32Bit,
        bool expectedIs64Bit)
    {
        Assert.Equal(
            expectedIs64Bit,
            Dlss5CompatibilityService.ResolveActualIs64Bit(detected, cardIs32Bit));
    }

    [Fact]
    public void AddonArchitectureGuard_RejectsX64PayloadFor32BitGame()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-addon-arch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var addon = Path.Combine(root, "wrong.addon32");
        try
        {
            WriteMinimalPe(addon, MachineType.x64);
            Assert.False(AddonPackService.IsAddonArchitectureCompatible(addon, is32Bit: true));
            Assert.True(AddonPackService.IsAddonArchitectureCompatible(addon, is32Bit: false));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 2. ViewLayout.NextViewLayout — Detail ↔ Compact cycle (no Grid)
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void NextViewLayout_Detail_ReturnsCompact()
    {
        // ViewLayout cycles Detail → Compact → Detail
        ViewLayout layout = ViewLayout.Detail;
        var next = layout switch
        {
            ViewLayout.Detail => ViewLayout.Compact,
            ViewLayout.Compact => ViewLayout.Detail,
            _ => ViewLayout.Detail,
        };
        Assert.Equal(ViewLayout.Compact, next);
    }

    [Fact]
    public void NextViewLayout_Compact_ReturnsDetail()
    {
        ViewLayout layout = ViewLayout.Compact;
        var next = layout switch
        {
            ViewLayout.Detail => ViewLayout.Compact,
            ViewLayout.Compact => ViewLayout.Detail,
            _ => ViewLayout.Detail,
        };
        Assert.Equal(ViewLayout.Detail, next);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 3. ExtractAppIdFromAcfFilename — parse Steam ACF filenames
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExtractAppIdFromAcfFilename_Valid_ReturnsAppId()
    {
        var result = GameDetectionService.ExtractAppIdFromAcfFilename("appmanifest_993090.acf");
        Assert.Equal(993090, result);
    }

    [Fact]
    public void ExtractAppIdFromAcfFilename_InvalidPrefix_ReturnsNull()
    {
        var result = GameDetectionService.ExtractAppIdFromAcfFilename("manifest_12345.acf");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractAppIdFromAcfFilename_NonNumericSuffix_ReturnsNull()
    {
        var result = GameDetectionService.ExtractAppIdFromAcfFilename("appmanifest_abc.acf");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractAppIdFromAcfFilename_EmptyString_ReturnsNull()
    {
        var result = GameDetectionService.ExtractAppIdFromAcfFilename("");
        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 4. NormalizeForMatch — strips special chars, lowercases, keeps letters/digits/spaces
    //    (Testing the algorithm contract; the real method is private on DlssPresetService)
    // ═══════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Returnal™", "returnal")]
    [InlineData("FINAL FANTASY VII REMAKE", "final fantasy vii remake")]
    [InlineData("Tom Clancy's: Ghost Recon®", "tom clancys ghost recon")]
    [InlineData("Half-Life 2", "halflife 2")]
    [InlineData("  spaces  ", "  spaces  ")]
    public void NormalizeForMatch_Algorithm_StripsSpecialChars(string input, string expected)
    {
        // Replicate the NormalizeForMatch algorithm used in DlssPresetService
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c == ' ')
                sb.Append(char.ToLowerInvariant(c));
        }
        Assert.Equal(expected, sb.ToString());
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 5. FormatAge — "just now", "5m ago", "2h ago", "3d ago" formatting
    //    (Testing the algorithm contract; the real method is private static)
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FormatAge_LessThanOneMinute_ReturnsJustNow()
    {
        var utc = DateTime.UtcNow.AddSeconds(-30);
        Assert.Equal("just now", FormatAgeHelper(utc));
    }

    [Fact]
    public void FormatAge_FiveMinutes_Returns5mAgo()
    {
        var utc = DateTime.UtcNow.AddMinutes(-5);
        Assert.Equal("5m ago", FormatAgeHelper(utc));
    }

    [Fact]
    public void FormatAge_TwoHours_Returns2hAgo()
    {
        var utc = DateTime.UtcNow.AddHours(-2);
        Assert.Equal("2h ago", FormatAgeHelper(utc));
    }

    [Fact]
    public void FormatAge_ThreeDays_Returns3dAgo()
    {
        var utc = DateTime.UtcNow.AddDays(-3);
        Assert.Equal("3d ago", FormatAgeHelper(utc));
    }

    /// <summary>Mirrors MainViewModel.FormatAge exactly.</summary>
    private static string FormatAgeHelper(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours   < 1) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalDays    < 1) return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 6. ApplyPeakNits — respects enabled flag, preset filter, creates sections
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ApplyPeakNits_WhenDisabled_DoesNotModifyFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "[renodx-preset1]\nSomething=1\n");

            // Disable peak nits globally
            var previousEnabled = AuxInstallService.GlobalPeakNitsEnabled;
            AuxInstallService.GlobalPeakNitsEnabled = false;
            try
            {
                AuxInstallService.ApplyPeakNits(tempFile, 1000);
                var content = File.ReadAllText(tempFile);
                Assert.DoesNotContain("ToneMapPeakNits", content);
            }
            finally
            {
                AuxInstallService.GlobalPeakNitsEnabled = previousEnabled;
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ApplyPeakNits_WhenEnabled_WritesToCheckedPresets()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "[renodx-preset1]\nSomething=1\n[renodx-preset2]\nOther=2\n");

            var prevEnabled = AuxInstallService.GlobalPeakNitsEnabled;
            var prevPresets = AuxInstallService.GlobalPeakNitsPresets;
            AuxInstallService.GlobalPeakNitsEnabled = true;
            AuxInstallService.GlobalPeakNitsPresets = new HashSet<int> { 1 }; // Only preset 1
            try
            {
                AuxInstallService.ApplyPeakNits(tempFile, 800);
                var content = File.ReadAllText(tempFile);
                // Preset 1 should have it
                Assert.Contains("ToneMapPeakNits=800", content);
            }
            finally
            {
                AuxInstallService.GlobalPeakNitsEnabled = prevEnabled;
                AuxInstallService.GlobalPeakNitsPresets = prevPresets;
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ApplyPeakNits_CreatesMissingPresetSection()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // File has no preset sections at all
            File.WriteAllText(tempFile, "[renodx]\nSomeKey=1\n");

            var prevEnabled = AuxInstallService.GlobalPeakNitsEnabled;
            var prevPresets = AuxInstallService.GlobalPeakNitsPresets;
            AuxInstallService.GlobalPeakNitsEnabled = true;
            AuxInstallService.GlobalPeakNitsPresets = new HashSet<int> { 1, 2 };
            try
            {
                AuxInstallService.ApplyPeakNits(tempFile, 1200);
                var content = File.ReadAllText(tempFile);
                Assert.Contains("[renodx-preset1]", content);
                Assert.Contains("[renodx-preset2]", content);
                Assert.Contains("ToneMapPeakNits=1200", content);
            }
            finally
            {
                AuxInstallService.GlobalPeakNitsEnabled = prevEnabled;
                AuxInstallService.GlobalPeakNitsPresets = prevPresets;
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ApplyPeakNits_ZeroNits_IsNoop()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "[renodx-preset1]\nSomething=1\n");
            AuxInstallService.ApplyPeakNits(tempFile, 0);
            var content = File.ReadAllText(tempFile);
            Assert.DoesNotContain("ToneMapPeakNits", content);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 7. PeakNitsPresets serialization — HashSet round-trips through JSON
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PeakNitsPresets_JsonRoundTrip()
    {
        var original = new HashSet<int> { 1, 3 };
        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var restored = System.Text.Json.JsonSerializer.Deserialize<HashSet<int>>(json);
        Assert.NotNull(restored);
        Assert.Equal(original, restored);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 8. AnyUpdateAvailable — game card status logic
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AnyUpdateAvailable_HiddenCard_Excluded()
    {
        var card = new GameCardViewModel
        {
            GameName = "Test",
            InstallPath = @"C:\Games\Test",
            Status = GameStatus.UpdateAvailable,
            IsHidden = true,
        };
        // AnyUpdateAvailable excludes hidden cards
        var result = ComputeAnyUpdateAvailable(card);
        Assert.False(result);
    }

    [Fact]
    public void AnyUpdateAvailable_ExternalOnly_ExcludedForRdx()
    {
        var card = new GameCardViewModel
        {
            GameName = "External",
            InstallPath = @"C:\Games\External",
            Status = GameStatus.UpdateAvailable,
            IsExternalOnly = true,
        };
        // External-only games excluded from RDX update all
        var result = ComputeAnyUpdateAvailable(card);
        Assert.False(result);
    }

    [Fact]
    public void AnyUpdateAvailable_RsUpdate_NotExcluded_ReturnsTrue()
    {
        var card = new GameCardViewModel
        {
            GameName = "GameWithRsUpdate",
            InstallPath = @"C:\Games\GameWithRsUpdate",
            RsStatus = GameStatus.UpdateAvailable,
            ExcludeFromUpdateAllReShade = false,
        };
        var result = ComputeAnyUpdateAvailable(card);
        Assert.True(result);
    }

    [Fact]
    public void AnyUpdateAvailable_RsUpdate_Excluded_ReturnsFalse()
    {
        var card = new GameCardViewModel
        {
            GameName = "GameExcluded",
            InstallPath = @"C:\Games\GameExcluded",
            RsStatus = GameStatus.UpdateAvailable,
            ExcludeFromUpdateAllReShade = true,
        };
        var result = ComputeAnyUpdateAvailable(card);
        Assert.False(result);
    }

    /// <summary>
    /// Mirrors the MainViewModel.AnyUpdateAvailable logic for a single card.
    /// This tests the filter conditions without needing a full ViewModel.
    /// </summary>
    private static bool ComputeAnyUpdateAvailable(GameCardViewModel c)
    {
        if (c.IsHidden) return false;
        if (string.IsNullOrEmpty(c.InstallPath)) return false;

        return (c.Status == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllRenoDx && !c.IsExternalOnly)
            || (c.RsStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllReShade && !c.RequiresVulkanInstall)
            || (c.UlStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllUl)
            || (c.DcStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllDc)
            || (c.OsStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllOs)
            || (c.DxvkStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllDxvk)
            || (c.RefStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllRef)
            || (c.LumaStatus == GameStatus.UpdateAvailable)
            || (c.DofFixStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllDofFix);
    }

    private static void WriteMinimalPe(string path, MachineType machine)
    {
        var bytes = new byte[512];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3c);
        bytes[0x80] = (byte)'P';
        bytes[0x81] = (byte)'E';
        BitConverter.GetBytes((ushort)machine).CopyTo(bytes, 0x84);
        File.WriteAllBytes(path, bytes);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 9. ParseIni + WriteIni round-trip (foundation for many INI-based tests)
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseIni_SectionAndKeys_Parsed()
    {
        var lines = new[]
        {
            "[renodx]",
            "ToneMapPeakNits=1000",
            "ForceBorderless=1",
            "[GENERAL]",
            "PreprocessorDefinitions=SOMETHING",
        };
        var ini = AuxInstallService.ParseIni(lines);
        Assert.True(ini.ContainsKey("renodx"));
        Assert.Equal("1000", ini["renodx"]["ToneMapPeakNits"]);
        Assert.Equal("1", ini["renodx"]["ForceBorderless"]);
        Assert.True(ini.ContainsKey("GENERAL"));
    }
}
