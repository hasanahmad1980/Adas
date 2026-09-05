using System;
using System.IO;
using System.Net.Http;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class MfgUnlockTests
{
    // ── GPU gating: only RTX 40-series (Ada) is eligible ──────────────────────

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4080", true)]
    [InlineData("NVIDIA GeForce RTX 4090 Laptop GPU", true)]
    [InlineData("NVIDIA GeForce RTX 4060 Ti", true)]
    [InlineData("NVIDIA RTX 4000 Ada Generation", true)]
    [InlineData("NVIDIA GeForce RTX 3080", false)]
    [InlineData("NVIDIA GeForce RTX 5080", false)]
    [InlineData("NVIDIA GeForce RTX 2080 Super", false)]
    [InlineData("AMD Radeon RX 7900 XTX", false)]
    [InlineData("Intel Arc A770", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAdaGpu_ClassifiesRtx40SeriesOnly(string? gpuName, bool expected)
        => Assert.Equal(expected, Dlss5CompatibilityService.IsAdaGpu(gpuName));

    // ── ReShade.ini [RenoDX.MFGUnlock] config round-trip ──────────────────────

    [Fact]
    public void ReadConfig_ReturnsDefaults_WhenNoIniPresent()
    {
        using var temp = new TempDir();
        var config = NewService().ReadConfig(temp.Path);

        Assert.Equal(1, config.Enabled);
        Assert.Equal(4, config.MaxCount);
        Assert.Equal(1, config.ForceFlipMeteringOff);
        Assert.Equal(1, config.TemporalFix);
        Assert.Equal(0, config.ForceMultiplier);
        Assert.Equal(0, config.RaiseFrameCeiling);
        Assert.Equal(0, config.ForceOTAPlugins);
    }

    [Fact]
    public void WriteConfig_ThenReadConfig_RoundTrips()
    {
        using var temp = new TempDir();
        var service = NewService();

        service.WriteConfig(temp.Path, new MfgUnlockConfig
        {
            Enabled = 0,
            MaxCount = 6,
            ForceFlipMeteringOff = 0,
            TemporalFix = 0,
            ForceMultiplier = 3,
            RaiseFrameCeiling = 1,
            ForceOTAPlugins = 1,
        });

        var read = service.ReadConfig(temp.Path);
        Assert.Equal(0, read.Enabled);
        Assert.Equal(6, read.MaxCount);
        Assert.Equal(0, read.ForceFlipMeteringOff);
        Assert.Equal(0, read.TemporalFix);
        Assert.Equal(3, read.ForceMultiplier);
        Assert.Equal(1, read.RaiseFrameCeiling);
        Assert.Equal(1, read.ForceOTAPlugins);
    }

    [Fact]
    public void WriteConfig_PreservesUnrelatedIniContent()
    {
        using var temp = new TempDir();
        var iniPath = Path.Combine(temp.Path, "reshade.ini");
        File.WriteAllText(iniPath, "[GENERAL]\nPerformanceMode=1\n");

        NewService().WriteConfig(temp.Path, new MfgUnlockConfig { ForceMultiplier = 4 });

        var text = File.ReadAllText(iniPath);
        Assert.Contains("[GENERAL]", text);
        Assert.Contains("PerformanceMode=1", text);
        Assert.Contains("[RenoDX.MFGUnlock]", text);
        Assert.Contains("ForceMultiplier=4", text);
    }

    // ── Install detection ─────────────────────────────────────────────────────

    [Fact]
    public void IsInstalled_ReflectsAddonPresence()
    {
        using var temp = new TempDir();
        Assert.False(MfgUnlockService.IsInstalled(temp.Path));

        File.WriteAllText(Path.Combine(temp.Path, MfgUnlockService.FileName), "stub");
        Assert.True(MfgUnlockService.IsInstalled(temp.Path));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MfgUnlockService NewService() => new(new HttpClient(), new NoopCrashReporter());

    private sealed class NoopCrashReporter : ICrashReporter
    {
        public bool VerboseLogging { get; set; }
        public void Log(string message) { }
        public void WriteCrashReport(string source, Exception? ex, bool isTerminating = false, string? note = null) { }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "mfgunlock-tests-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
