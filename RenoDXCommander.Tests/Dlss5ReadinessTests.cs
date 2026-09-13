using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class Dlss5ReadinessTests
{
    private static readonly string[] Cod2Missing =
    {
        "ReShade 6.8+ with full add-on support",
        "RenoDX DLSS 5 neural-rendering add-on",
        "nvngx_dlssnr.dll model/runtime",
        "nvngx_dlss.dll Super Resolution runtime",
        "a supported motion-vector provider (Adas installs LumeniteFX Kernel)",
        "dgVoodoo2 2.87.3+ translation to DirectX 11 (installed automatically)",
    };

    [Fact]
    public void AmbiguousFolderOnlyAsksTheUserToPickTheFolderAndNeverListsAutoInstalledPartsAsProblems()
    {
        var assessment = new Dlss5Assessment(Dlss5DeploymentMode.Dx9Feeder, null,
            new[] { Dlss5CompatibilityService.AmbiguousDeploymentPathReason }, Cod2Missing, true, false);

        var readiness = Dlss5ReadinessText.Describe(assessment);

        Assert.Equal(Dlss5ReadinessState.NeedsGameFolder, readiness.State);
        Assert.True(readiness.NeedsGameFolder);
        var problem = Assert.Single(readiness.Problems);
        Assert.Contains("Choose game folder", problem);
        Assert.DoesNotContain(readiness.Problems, p => p.Contains("ReShade") || p.Contains("nvngx"));
        Assert.Contains("ReShade", readiness.AutoSetupText);
        Assert.DoesNotContain("nvngx", readiness.AutoSetupText);
    }

    [Fact]
    public void ReadyGameGetsAPlainHeadlineWithTheRendererName()
    {
        var assessment = new Dlss5Assessment(Dlss5DeploymentMode.Dx9Feeder, @"C:\Games\CoD2",
            Array.Empty<string>(), Cod2Missing, true, false);

        var readiness = Dlss5ReadinessText.Describe(assessment);

        Assert.Equal(Dlss5ReadinessState.Ready, readiness.State);
        Assert.Contains("DirectX 9", readiness.Headline);
        Assert.Contains("32-bit", readiness.Headline);
        Assert.DoesNotContain("Feeder", readiness.Headline);
        Assert.Empty(readiness.Problems);
    }

    [Fact]
    public void HardBlockersAreTranslatedAndUnknownRendererEvidenceIsSummarised()
    {
        var assessment = new Dlss5Assessment(Dlss5DeploymentMode.None, @"C:\Games\X",
            new[]
            {
                "This package requires an NVIDIA GeForce RTX 20-, 30-, 40-, or 50-series GPU.",
                "Detected anti-cheat software: EasyAntiCheat. Adas will not modify this game.",
                "No renderer imports found in game.exe",
                "Supported: 32-bit DirectX 8, DirectX 9–12, Vulkan and OpenGL. DirectX 8 requires a 32-bit executable.",
            },
            Array.Empty<string>(), true, true);

        var readiness = Dlss5ReadinessText.Describe(assessment);

        Assert.Equal(Dlss5ReadinessState.Blocked, readiness.State);
        Assert.Contains(readiness.Problems, p => p.StartsWith("Your graphics card isn't supported"));
        Assert.Contains(readiness.Problems, p => p.Contains("anti-cheat (EasyAntiCheat)"));
        Assert.Contains(readiness.Problems, p => p.Contains("couldn't tell which graphics technology"));
        Assert.DoesNotContain(readiness.Problems, p => p.Contains("renderer imports") || p.StartsWith("Supported:"));
    }

    [Fact]
    public void RouteStatusTextIsBeginnerFriendly()
    {
        var assessment = new Dlss5Assessment(Dlss5DeploymentMode.Dx9Feeder, @"C:\Games\CoD2",
            Array.Empty<string>(), Array.Empty<string>(), true, false);

        var routes = Dlss5RouteCatalog.Build(assessment, Dlss5RouteCatalog.Recommend(assessment, Dlss5InstallProfile.MaximumQuality));

        Assert.Contains(routes, r => r.Recommended && r.StatusText.Contains("best choice"));
        Assert.All(routes.Where(r => !r.Supported), r => Assert.StartsWith("✕ Not available", r.StatusText));
    }

    [Fact]
    public void ResolveDeploymentPath_UserPickedExeFolderWinsOverSubfoldersWithOtherExecutables()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-exe-root-test-{Guid.NewGuid():N}");
        var tools = Path.Combine(root, "Tools");
        Directory.CreateDirectory(tools);

        try
        {
            File.WriteAllBytes(Path.Combine(root, "Game.exe"), new byte[4096]);
            File.WriteAllBytes(Path.Combine(tools, "ModEditor.exe"), new byte[512]);

            var result = Dlss5CompatibilityService.ResolveDeploymentPath(root);

            Assert.Equal(Dlss5PathResolutionKind.Resolved, result.Kind);
            Assert.Equal(root, result.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
