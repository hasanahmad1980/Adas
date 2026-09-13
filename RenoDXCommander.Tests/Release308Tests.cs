using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class Release308Tests
{
    [Fact]
    public void ParsesGitHubDigestAndChecksumFile()
    {
        var hash = new string('a', 64);
        Assert.Equal(hash, UpdateService.ParseDigest("sha256:" + hash));
        Assert.Null(UpdateService.ParseDigest("md5:abc"));
        Assert.Null(UpdateService.ParseDigest(null));
        var sums = $"{new string('b', 64)}  Other.exe\n{hash} *Adas-Setup.exe\r\n";
        Assert.Equal(hash, UpdateService.FindSumFor(sums, "Adas-Setup.exe"), ignoreCase: true);
        Assert.Null(UpdateService.FindSumFor(sums, "Missing.exe"));
    }

    [Fact]
    public void MfgPrefersAFreeImportedName()
    {
        var dir = Directory.CreateTempSubdirectory("adas-mfg-").FullName;
        try
        {
            var imports = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dxgi.dll", "xinput1_3.dll", "version.dll" };
            File.WriteAllText(Path.Combine(dir, "version.dll"), "someone else's");
            Assert.Equal(("xinput1_3.dll", true), RtxMfgUnlockService.ChooseProxyFilename(dir, imports));
            Assert.Equal(("winmm.dll", false), RtxMfgUnlockService.ChooseProxyFilename(dir, new HashSet<string>()));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(Dlss5DeploymentMode.Dx11Feeder, Dlss5DeploymentMode.NativeDirectX11)]
    [InlineData(Dlss5DeploymentMode.VulkanFeeder, Dlss5DeploymentMode.NativeVulkan)]
    [InlineData(Dlss5DeploymentMode.Dx12Feeder, Dlss5DeploymentMode.Dx12Feeder)]
    public void BridgeSubstituteRetargetsFeederModes(Dlss5DeploymentMode from, Dlss5DeploymentMode to)
    {
        var dir = Directory.CreateTempSubdirectory("adas-sub-").FullName;
        try
        {
            var assessment = new Dlss5Assessment(from, dir, Array.Empty<string>(), Array.Empty<string>(), true, true);
            var result = Dlss5ComponentService.ApplyBridgeSubstitute(assessment);
            Assert.Equal(to, result.Mode);
            if (from != to) Assert.Contains("nvngx_dlssnr.dll", result.MissingRequirements);
            Assert.Equal(from is Dlss5DeploymentMode.Dx11Feeder or Dlss5DeploymentMode.VulkanFeeder,
                Dlss5ComponentService.SupportsBridgeSubstitute(from, true));
            Assert.False(Dlss5ComponentService.SupportsBridgeSubstitute(from, false));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SubstituteConfigMatchesBridgeReadme()
    {
        var vk = Dlss5ComponentService.BridgeSubstituteConfig(Dlss5DeploymentMode.NativeVulkan);
        Assert.Equal("1", vk["synth"]);
        Assert.Equal("auto", vk["source"]);
        Assert.Equal("3", vk["stage"]);
        Assert.Equal("2", vk["mode"]);
        Assert.Equal("1", vk["vk_mirror"]);
        Assert.False(Dlss5ComponentService.BridgeSubstituteConfig(Dlss5DeploymentMode.NativeDirectX11).ContainsKey("vk_mirror"));
    }

    [Fact]
    public void AioLogSignaturesSuggestSwitches()
    {
        var log = "Source exceeds detected native resolution\nwaiting for first game present\nrequired private runtime dependency missing";
        var dx11 = Dlss5AioTroubleshooter.Suggest(log, Dlss5DeploymentMode.NativeDirectX11, out var repair);
        Assert.True(repair);
        Assert.Equal(new[] { Dlss5AioTroubleshooter.DpiCorrection }, dx11);
        var dx12 = Dlss5AioTroubleshooter.Suggest(log, Dlss5DeploymentMode.NativeDirectX12, out _);
        Assert.Contains(Dlss5AioTroubleshooter.EarlyInitialization, dx12);
        Assert.Empty(Dlss5AioTroubleshooter.Suggest(null, Dlss5DeploymentMode.NativeDirectX12, out _));
    }
}
