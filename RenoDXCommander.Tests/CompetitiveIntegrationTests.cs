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
}
