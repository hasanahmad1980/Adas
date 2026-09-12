using Microsoft.Win32;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Covers the integrations informed by higher-star DLSS 5 tools: the broadened
/// anti-cheat gate catalog (DLSS5-Autopilot) and the Universal RTXMFG proxy-DLL
/// deploy/restore logic (RTX40MFG-Unlock). Network-dependent staging is not exercised.
/// </summary>
public sealed class CompetitiveIntegrationTests
{
    [Theory]
    [InlineData("EAAntiCheat")]
    [InlineData("PnkBstr")]
    [InlineData("denuvo")]
    [InlineData("anticheatexpert")]
    [InlineData("SGuard")]
    [InlineData("Wellbia")]
    [InlineData("EasyAntiCheat")] // pre-existing marker still present
    public void AntiCheatCatalogCoversAutopilotList(string marker)
    {
        Assert.Contains(marker, Dlss5CompatibilityService.AntiCheatMarkers);
    }

    [Fact]
    public void ProxyFilenamesIncludeDefaultAndAreUnique()
    {
        Assert.Contains(RtxMfgUnlockService.DefaultProxyFilename, RtxMfgUnlockService.ProxyFilenames);
        Assert.Equal(
            RtxMfgUnlockService.ProxyFilenames.Count,
            RtxMfgUnlockService.ProxyFilenames.Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void IsInstalledInReflectsMarkerPresence()
    {
        var dir = Path.Combine(Path.GetTempPath(), "adas-rtxmfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.False(RtxMfgUnlockService.IsInstalledIn(dir));

            var markerPath = Path.Combine(dir, ".adas", "rtxmfg-install.json");
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            File.WriteAllText(markerPath, "{\"proxyFilename\":\"dxgi.dll\",\"hadBackup\":false}");
            Assert.True(RtxMfgUnlockService.IsInstalledIn(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ── Hybrid-GPU preference (DLSS5oneclick) ──────────────────────────────────
    private const string GpuPrefSubKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

    [Fact]
    public void GpuPreferenceSetThenClearRoundTrips()
    {
        // A unique, non-existent exe path so we never touch a real game's preference.
        var exe = Path.Combine(Path.GetTempPath(), "adas-gpupref-" + Guid.NewGuid().ToString("N") + ".exe");
        var name = Path.GetFullPath(exe);
        try
        {
            Assert.True(GpuPreferenceService.Set(exe));
            using (var key = Registry.CurrentUser.OpenSubKey(GpuPrefSubKey))
                Assert.Equal(GpuPreferenceService.HighPerformanceValue, key?.GetValue(name) as string);

            // Idempotent: a second Set is a no-op that still reports success.
            Assert.True(GpuPreferenceService.Set(exe));

            GpuPreferenceService.Clear(exe);
            using (var key = Registry.CurrentUser.OpenSubKey(GpuPrefSubKey))
                Assert.Null(key?.GetValue(name));
        }
        finally
        {
            using var key = Registry.CurrentUser.OpenSubKey(GpuPrefSubKey, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    [Fact]
    public void GpuPreferenceClearLeavesAUserSetValueUntouched()
    {
        var exe = Path.Combine(Path.GetTempPath(), "adas-gpupref-" + Guid.NewGuid().ToString("N") + ".exe");
        var name = Path.GetFullPath(exe);
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(GpuPrefSubKey, writable: true))
                key!.SetValue(name, "GpuPreference=1;", RegistryValueKind.String);

            GpuPreferenceService.Clear(exe); // value is not ours → must be preserved

            using var check = Registry.CurrentUser.OpenSubKey(GpuPrefSubKey);
            Assert.Equal("GpuPreference=1;", check?.GetValue(name) as string);
        }
        finally
        {
            using var key = Registry.CurrentUser.OpenSubKey(GpuPrefSubKey, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    // ── Driver pre-flight gate (Feeder/oneclick) ───────────────────────────────
    [Theory]
    [InlineData(Dlss5DeploymentMode.Dx11Feeder)]
    [InlineData(Dlss5DeploymentMode.Dx12Feeder)]
    [InlineData(Dlss5DeploymentMode.NativeDirectX12)]
    [InlineData(Dlss5DeploymentMode.NativeVulkan)]
    public void DriverPreflightWarnsOn61664ForAffectedRoutes(Dlss5DeploymentMode mode)
    {
        Assert.NotNull(Dlss5CompatibilityService.GetDriverPreflightWarning(mode, driverVersion: "616.64"));
    }

    [Fact]
    public void DriverPreflightIsSilentForGoodDriverOrUnknownRoute()
    {
        // A known-good driver on an affected route, and a known-bad driver on a route that does not
        // use the RenoDX consumer, both stay silent. (An empty version falls back to the machine's real
        // driver by design, so it is not asserted here.)
        Assert.Null(Dlss5CompatibilityService.GetDriverPreflightWarning(Dlss5DeploymentMode.Dx11Feeder, driverVersion: "616.56"));
        Assert.Null(Dlss5CompatibilityService.GetDriverPreflightWarning(Dlss5DeploymentMode.None, driverVersion: "616.64"));
    }

    // The RenoDX-consumer routes are the ones 616.64 breaks; the standalone AIO suite and the
    // OptiScaler-NR forks route around that consumer, so the pre-flight must stay silent for them
    // even on an affected mode + bad driver.
    [Theory]
    [InlineData(Dlss5InstallProfile.MaximumQuality, true)]
    [InlineData(Dlss5InstallProfile.ExperimentalUnified, true)]
    [InlineData(Dlss5InstallProfile.LatestFeederBeta, true)]
    [InlineData(Dlss5InstallProfile.NeuralUpstream, true)]
    [InlineData(Dlss5InstallProfile.StandaloneAio, false)]
    [InlineData(Dlss5InstallProfile.OptiScalerNeuralRendering, false)]
    [InlineData(Dlss5InstallProfile.OptiScalerNrBeforeSr, false)]
    [InlineData(Dlss5InstallProfile.OptiScalerPreSrMultipass, false)]
    public void DriverPreflightIsProfileAware(Dlss5InstallProfile profile, bool expectWarning)
    {
        var warning = Dlss5CompatibilityService.GetDriverPreflightWarning(
            Dlss5DeploymentMode.NativeDirectX12, profile, driverVersion: "616.64");
        Assert.Equal(expectWarning, warning is not null);
    }
}
