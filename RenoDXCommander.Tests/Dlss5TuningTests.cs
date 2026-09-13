using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

[Collection("Dlss5GamePreferences")]
public sealed class Dlss5TuningTests
{
    [Theory]
    [InlineData(Dlss5TuningPreset.Quality, 100)]
    [InlineData(Dlss5TuningPreset.Balanced, 75)]
    [InlineData(Dlss5TuningPreset.Performance, 50)]
    public void PresetsMapToWorkArea(Dlss5TuningPreset preset, int expected)
        => Assert.Equal(expected, Dlss5ComponentService.WorkResolutionFor(preset));

    [Theory]
    [InlineData(30, 60, 100, 70)]
    [InlineData(60, 60, 100, 100)]
    [InlineData(10, 120, 100, 50)]
    [InlineData(120, 60, 75, 100)]
    [InlineData(0, 60, 80, 80)]
    public void TargetFpsEstimateIsRoundedAndClamped(double current, double target, int work, int expected)
        => Assert.Equal(expected, Dlss5ComponentService.EstimateWorkResolutionForTarget(current, target, work));

    [Fact]
    public void WorkCostScalesWithPixelCount()
    {
        Assert.Equal(1.0, Dlss5ComponentService.RelativeWorkCost(100), 3);
        Assert.Equal(0.5625, Dlss5ComponentService.RelativeWorkCost(75), 3);
        Assert.Equal(0.25, Dlss5ComponentService.RelativeWorkCost(50), 3);
    }

    [Fact]
    public void SelectFeederReleasePicksPrereleaseOrExactTag()
    {
        using var doc = JsonDocument.Parse("""
            [{"tag_name":"v0.9-draft","draft":true,"prerelease":true},
             {"tag_name":"v0.9-rc1","draft":false,"prerelease":true},
             {"tag_name":"v0.8","draft":false,"prerelease":false}]
            """);
        Assert.Equal("v0.9-rc1", Dlss5ComponentService.SelectFeederRelease(doc.RootElement, Dlss5ComponentService.NewestPrereleaseTag)!.Value.GetProperty("tag_name").GetString());
        Assert.Equal("v0.8", Dlss5ComponentService.SelectFeederRelease(doc.RootElement, "V0.8")!.Value.GetProperty("tag_name").GetString());
        Assert.Null(Dlss5ComponentService.SelectFeederRelease(doc.RootElement, "v9"));
    }

    [Fact]
    public void OldFeederTagsPairWithRenoDx455()
        => Assert.Contains("older than 0.8", Dlss5ComponentService.DescribeFeederPairing("v0.7.2"));

    [Theory]
    [InlineData(Dlss5MotionProvider.LumeniteKernel, "Lumenite_Kernel@lumenite_Kernel.fx", "DLSS5_MV_PROVIDER=3")]
    [InlineData(Dlss5MotionProvider.VortMotion, "vort_MotionEffects@vort_Motion.fx", "DLSS5_MV_PROVIDER=2")]
    public void SelectedProviderSitsAboveFeederAndOtherIsRemoved(Dlss5MotionProvider provider, string technique, string define)
    {
        var result = Dlss5ComponentService.PutTechniquesFirst(
            "Foo@foo.fx,DLSS5_Feed@DLSS5_Feed.fx,vort_MotionEffects@vort_Motion.fx,Lumenite_Kernel@lumenite_Kernel.fx",
            provider: provider);
        Assert.Equal($"{technique},DLSS5_Feed@DLSS5_Feed.fx,Foo@foo.fx", result);
        Assert.Equal(define, Dlss5ComponentService.MotionProviderDefinition(provider));
    }

    [Fact]
    public void PreferencesRoundTripAndResolve()
    {
        var original = Dlss5GamePreferences.StorePath;
        var dir = Path.Combine(Path.GetTempPath(), $"adas-prefs-{Guid.NewGuid():N}");
        Dlss5GamePreferences.StorePath = Path.Combine(dir, "prefs.json");
        try
        {
            Assert.Equal(Dlss5MotionProvider.VortMotion,
                Dlss5GamePreferences.ResolveInstallChoices(new(), Dlss5DeploymentMode.OpenGlFeeder).Provider);
            Assert.Equal(Dlss5MotionProvider.LumeniteKernel,
                Dlss5GamePreferences.ResolveInstallChoices(new(), Dlss5DeploymentMode.Dx11Feeder).Provider);

            Dlss5GamePreferences.Update("Game", "Steam", p =>
            {
                p.FeederChannel = Dlss5FeederChannel.ExactRelease;
                p.FeederReleaseTag = "v0.8.1";
                p.MotionProvider = Dlss5MotionProvider.VortMotion;
            });
            var stored = Dlss5GamePreferences.Get("Game", "Steam");
            var (tag, provider) = Dlss5GamePreferences.ResolveInstallChoices(stored, Dlss5DeploymentMode.Dx11Feeder);
            Assert.Equal("v0.8.1", tag);
            Assert.Equal(Dlss5MotionProvider.VortMotion, provider);
            Assert.Equal(Dlss5FeederChannel.Bundled, Dlss5GamePreferences.Get("Game", "GOG").FeederChannel);

            Dlss5GamePreferences.Update("Game", "Steam", p => p.FeederReleaseTag = "../evil");
            Assert.Equal(Dlss5FeederChannel.Bundled, Dlss5GamePreferences.Get("Game", "Steam").FeederChannel);
        }
        finally
        {
            Dlss5GamePreferences.StorePath = original;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FsrFrameGenerationWritesIniAndReportsMissingLibrary()
    {
        var dir = Directory.CreateTempSubdirectory("adas-fsrfg-").FullName;
        try
        {
            Assert.NotNull(OptiScalerService.ApplyFsr31FrameGeneration(dir, true));
            File.WriteAllText(Path.Combine(dir, "OptiScaler.ini"), "[FrameGen]\nEnabled=auto\nFGInput=auto\nFGOutput=auto\n");
            Assert.NotNull(OptiScalerService.ApplyFsr31FrameGeneration(dir, true));
            File.WriteAllText(Path.Combine(dir, OptiScalerService.FsrFrameGenerationLibrary), "x");
            Assert.Null(OptiScalerService.ApplyFsr31FrameGeneration(dir, true));
            var ini = File.ReadAllText(Path.Combine(dir, "OptiScaler.ini"));
            Assert.Contains("FGOutput=fsrfg", ini);
            Assert.Contains("FGInput=upscaler", ini);
            Assert.Null(OptiScalerService.ApplyFsr31FrameGeneration(dir, false));
            Assert.Contains("FGOutput=nofg", File.ReadAllText(Path.Combine(dir, "OptiScaler.ini")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PreviewWritesNothing()
    {
        var dir = Directory.CreateTempSubdirectory("adas-preview-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "dxgi.dll"), "original");
            var changes = Dlss5ComponentService.PreviewInstall(dir, Dlss5DeploymentMode.Dx11Feeder, true, Dlss5InstallProfile.MaximumQuality);
            Assert.NotEmpty(changes);
            Assert.Contains(changes, c => c.Action == Dlss5PlannedAction.Write);
            Assert.Equal(new[] { "dxgi.dll" }, Directory.GetFileSystemEntries(dir).Select(Path.GetFileName));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TipsMentionHotkeysAndDisplayAdvice()
    {
        var tips = string.Join("\n", Dlss5ComponentService.GetPostInstallTips(Dlss5DeploymentMode.Dx11Feeder, Dlss5InstallProfile.MaximumQuality));
        Assert.Contains("F6", tips);
        Assert.Contains("Home", tips);
        Assert.Contains("orderless", tips);
        Assert.Contains("V-sync", tips, StringComparison.OrdinalIgnoreCase);
    }
}
