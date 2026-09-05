using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class Dlss5AioTests
{
    [Theory]
    [InlineData(Dlss5DeploymentMode.Dx9Feeder, true)]
    [InlineData(Dlss5DeploymentMode.Dx11Feeder, true)]
    [InlineData(Dlss5DeploymentMode.NativeDirectX12, true)]
    [InlineData(Dlss5DeploymentMode.NativeVulkan, true)]
    [InlineData(Dlss5DeploymentMode.OpenGlFeeder, false)]
    [InlineData(Dlss5DeploymentMode.Dx10ViaDxvkFeeder, false)]
    [InlineData(Dlss5DeploymentMode.Dx10Feeder, false)]
    [InlineData(Dlss5DeploymentMode.None, false)]
    public void EligibilityNeverRoutes32BitGamesIntoAio(Dlss5DeploymentMode mode, bool supported)
    {
        Assert.Equal(supported, Dlss5ComponentService.SupportsAio(mode, true));
        Assert.False(Dlss5ComponentService.SupportsAio(mode, false));
    }

    [Fact]
    public void AioDefaultsDoNotEnableExperimentalFrameGenerationOrEarlyProxy()
    {
        Assert.Equal("0", Dlss5ComponentService.AioDefaults["FrameGeneration"]);
        Assert.Equal("0", Dlss5ComponentService.AioDefaults["EarlyProxyInitialization"]);
        Assert.Equal("1", Dlss5ComponentService.AioDefaults["NeuralRendering"]);
        Assert.Equal("-1", Dlss5ComponentService.AioDefaults["SkinStructure"]);
        Assert.Equal("0", Dlss5ComponentService.AioDefaults["NrRejectionMask"]);
        Assert.Equal("1", Dlss5ComponentService.AioDefaults["NrRejectionStrength"]);
        Assert.Equal("12", Dlss5ComponentService.AioDefaults["DlssRenderPreset"]);
        Assert.Equal("1", Dlss5ComponentService.AioDefaults["PerformanceTelemetry"]);
        Assert.Equal("1", Dlss5ComponentService.AioDefaults["AutoWindowedVirtualization"]);
        Assert.Equal("0", Dlss5ComponentService.AioDefaults["SynchronousProxyPresentation"]);
        Assert.Equal("0", Dlss5ComponentService.AioDefaults["VortGuides"]);
    }

    [Fact]
    public void AioManagesItsGuideTechniquesOutsideTheNormalEffectChain()
    {
        var result = Dlss5ComponentService.RemoveAioScheduledTechniques(
            "Sharpen@Sharpen.fx,vort_MotionEffects@vort_Motion.fx,DLSS5_AIO_Feed@DLSS5_AIO_Feed.fx,Other@Other.fx");
        Assert.Equal("Sharpen@Sharpen.fx,Other@Other.fx", result);
    }

    [Fact]
    public void LocalImportRejectsIncompleteOrMismatchedReleaseBeforeCaching()
    {
        var root = Directory.CreateTempSubdirectory("adas-aio-test-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, Dlss5ComponentService.AioAddon), "wrong release");
            Assert.Throws<InvalidDataException>(() => Dlss5ComponentService.ValidateAioAsset(
                Path.Combine(root, Dlss5ComponentService.AioAddon), Dlss5ComponentService.AioAddon));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AioRepairKeepsUserSettingsAndUninstallRestoresPreviousConfiguration()
    {
        var root = Directory.CreateTempSubdirectory("adas-aio-config-").FullName;
        try
        {
            var path = Path.Combine(root, "ReShade.ini");
            File.WriteAllText(path, "[Standalone.DLSSNR]\nIntensity=0.4\nFrameGeneration=1\n[Other]\nKeep=yes\n");
            var record = new Dlss5InstallRecord { Profile = Dlss5InstallProfile.StandaloneAio };
            Dlss5ComponentService.EnsureAioSettings(root, record);
            var ini = IniTextDocument.Load(path);
            Assert.True(ini.TryGetValue("Standalone.DLSSNR", "Intensity", out var intensity));
            Assert.Equal("0.4", intensity.Text);
            Assert.True(ini.TryGetValue("Standalone.DLSSNR", "FrameGeneration", out var fg));
            Assert.Equal("1", fg.Text);
            Assert.True(ini.TryGetValue("GENERAL", "SkipLoadingDisabledEffects", out var skip));
            Assert.Equal("0", skip.Text);
            Assert.Empty(Dlss5ComponentService.UninstallTrackedFiles(root, new NoopReporter()));
            Assert.Contains("Keep=yes", File.ReadAllText(path));
            Assert.DoesNotContain("EarlyProxyInitialization", File.ReadAllText(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AioDoesNotOverwriteAnExistingGameCallerBridge()
    {
        var root = Directory.CreateTempSubdirectory("adas-aio-collision-").FullName;
        try
        {
            var path = Path.Combine(root, "nvngx.dll");
            File.WriteAllText(path, "game-owned bridge");
            Assert.Throws<InvalidOperationException>(() => Dlss5ComponentService.ValidateAioConflicts(
                root, root, Dlss5DeploymentMode.NativeDirectX12, null));
            Assert.Equal("game-owned bridge", File.ReadAllText(path));
            Assert.False(Directory.Exists(Path.Combine(root, ".adas")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AioRejectsASecondPipelineWithoutDeletingIt()
    {
        var root = Directory.CreateTempSubdirectory("adas-aio-route-").FullName;
        try
        {
            var path = Path.Combine(root, Dlss5ComponentService.FeederAddon);
            File.WriteAllText(path, "existing feeder");
            Assert.Throws<InvalidOperationException>(() => Dlss5ComponentService.ValidateAioConflicts(
                root, root, Dlss5DeploymentMode.Dx11Feeder, null));
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void NativeProcessingCanSucceedWithoutTheOptionalCHook()
    {
        var root = Directory.CreateTempSubdirectory("adas-aio-diagnostic-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "dxgi.dll"), "ReShade");
            File.WriteAllText(Path.Combine(root, "renodx-dlss5.addon64"), "RenoDX");
            Dlss5ComponentService.SaveRecord(root, new Dlss5InstallRecord { Mode = Dlss5DeploymentMode.NativeDirectX12 });
            File.WriteAllText(Path.Combine(root, "ReShade.log"),
                "Failed to find NVSDK_NGX_D3D12_EvaluateFeature_C\ninline feature 18 evaluation succeeded (count=60)");
            var report = Dlss5DiagnosticService.Diagnose(root, Dlss5DeploymentMode.NativeDirectX12, true);
            Assert.False(report.HasProblems);
            Assert.True(report.IsWorking);
            Assert.Contains("does not verify", report.Summary);
            Assert.Contains(report.Findings, item => item.Contains("optional EvaluateFeature_C"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ALogFromBeforeRepairDoesNotProveCurrentProcessing()
    {
        var root = Directory.CreateTempSubdirectory("adas-aio-stale-log-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "dxgi.dll"), "ReShade");
            File.WriteAllText(Path.Combine(root, "renodx-dlss5.addon64"), "RenoDX");
            var log = Path.Combine(root, "ReShade.log");
            File.WriteAllText(log, "Successful NR frames: 123");
            File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddDays(-1));
            Dlss5ComponentService.SaveRecord(root, new Dlss5InstallRecord
            {
                Mode = Dlss5DeploymentMode.NativeDirectX12, InstalledAtUtc = DateTime.UtcNow,
            });
            Assert.False(Dlss5DiagnosticService.Diagnose(root, Dlss5DeploymentMode.NativeDirectX12, true).IsWorking);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class NoopReporter : ICrashReporter
    {
        public void Log(string message) { }
        public void WriteCrashReport(string source, Exception? ex, bool isTerminating = false, string? note = null) { }
        public bool VerboseLogging { get; set; }
    }
}
