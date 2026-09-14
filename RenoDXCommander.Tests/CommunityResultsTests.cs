using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class CommunityResultsTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070 Ti SUPER", "RTX 4070 Ti SUPER")]
    [InlineData("NVIDIA GeForce RTX 4090", "RTX 4090")]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", "RTX 4060 Laptop")]
    [InlineData("AMD Radeon RX 7900", "")]
    [InlineData(null, "")]
    public void GpuFamilyKeepsOnlyTheModel(string? name, string expected)
        => Assert.Equal(expected, CommunityResultsService.GpuFamily(name));

    [Fact]
    public void SummaryGroupsRoutesAndFiltersByGpu()
    {
        var games = CommunityResultsService.Parse("""
            {"version":1,"games":{"doometernal":[
              {"route":"StandaloneAio","gpu":"RTX 4090","worked":3,"failed":1},
              {"route":"StandaloneAio","gpu":"","worked":1,"failed":0},
              {"route":"MaximumQuality","gpu":"RTX 3080","worked":1,"failed":4},
              {"route":"","worked":9}
            ]}}
            """);
        var all = CommunityResultsService.Summarize(games, "DOOM Eternal™", null);
        Assert.Equal("StandaloneAio", all[0].Route);
        Assert.Equal(4, all[0].Worked);
        Assert.Equal(2, all.Count);
        var gpu = CommunityResultsService.Summarize(games, "doom eternal", "rtx 3080");
        Assert.Single(gpu);
        Assert.Equal(4, gpu[0].Failed);
        Assert.Empty(CommunityResultsService.Summarize(games, "Other", null));
    }

    [Fact]
    public void ReportUrlCarriesOnlyTheListedFields()
    {
        var report = new CommunityReport("Game & Co\nX", "StandaloneAio", "DirectX 12", true,
            "NVIDIA GeForce RTX 4090", "581.15", "3.0.9", false);
        var url = CommunityResultsService.BuildReportUrl(report);
        Assert.StartsWith("https://github.com/hasanahmad1980/Adas/issues/new?template=community-result.yml&", url);
        Assert.Contains("game=Game%20%26%20Co%20X", url);
        Assert.Contains("gpu=RTX%204090", url);
        Assert.Contains("result=It%20didn%27t%20work", url);
        Assert.DoesNotContain("NVIDIA", url);
    }

    [Fact]
    public void RouteKeyDistinguishesVariants()
    {
        var plain = new RouteOption(Dlss5InstallProfile.MaximumQuality, "", "", true, true, "");
        Assert.Equal("MaximumQuality", CommunityResultsService.RouteKey(plain));
        Assert.Equal("MaximumQuality+BridgeSubstitute", CommunityResultsService.RouteKey(plain with { BridgeSubstitute = true }));
        Assert.Equal("MaximumQuality+DeepFriedChicken", CommunityResultsService.RouteKey(plain with { DeepFriedChicken = true }));
    }
}
