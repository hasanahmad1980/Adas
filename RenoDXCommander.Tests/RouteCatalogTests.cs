using System;
using System.Linq;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Covers <see cref="Dlss5RouteCatalog"/> — the per-game route list and recommendation the setup pane
/// binds to. This is the shell-facing logic that previously lived in the Avalonia project untested;
/// it now sits in Adas.Core so its availability matrix and status copy can be asserted directly.
/// </summary>
public sealed class RouteCatalogTests
{
    private static Dlss5Assessment Assess(Dlss5DeploymentMode mode, bool is64) =>
        new(mode, @"C:\game", Array.Empty<string>(), Array.Empty<string>(), SinglePlayerConfirmed: true, Is64Bit: is64);

    private static readonly Dlss5InstallProfile[] AllProfiles =
        Enum.GetValues<Dlss5InstallProfile>();

    [Theory]
    [InlineData(Dlss5DeploymentMode.NativeDirectX12, true)]
    [InlineData(Dlss5DeploymentMode.Dx11Feeder, false)]
    [InlineData(Dlss5DeploymentMode.NativeVulkan, true)]
    public void BuildAlwaysListsEverySelectableProfile(Dlss5DeploymentMode mode, bool is64)
    {
        var routes = Dlss5RouteCatalog.Build(Assess(mode, is64), Dlss5InstallProfile.MaximumQuality);

        // Every profile the catalog offers appears exactly once, so "every route shown" holds.
        var listed = routes.Select(r => r.Profile).ToArray();
        Assert.Equal(listed.Length, listed.Distinct().Count());
        foreach (var expected in new[]
                 {
                     Dlss5InstallProfile.MaximumQuality, Dlss5InstallProfile.ExperimentalUnified,
                     Dlss5InstallProfile.LatestFeederBeta, Dlss5InstallProfile.StandaloneAio,
                     Dlss5InstallProfile.OpenGlBridge, Dlss5InstallProfile.NeuralUpstream,
                     Dlss5InstallProfile.OptiScalerNeuralRendering, Dlss5InstallProfile.OptiScalerNrBeforeSr,
                     Dlss5InstallProfile.OptiScalerPreSrMultipass,
                 })
            Assert.Contains(expected, listed);
    }

    [Fact]
    public void SupportedFlagAndStatusTextAgree()
    {
        var routes = Dlss5RouteCatalog.Build(Assess(Dlss5DeploymentMode.NativeDirectX12, true),
            Dlss5InstallProfile.MaximumQuality);

        foreach (var r in routes)
        {
            if (r.Supported)
                Assert.DoesNotContain("✕", r.StatusText);
            else
            {
                Assert.False(r.Recommended); // an unsupported route is never the recommendation
                Assert.StartsWith("✕", r.StatusText);
            }
        }
    }

    [Fact]
    public void AtMostOneRouteIsRecommended()
    {
        foreach (var mode in Enum.GetValues<Dlss5DeploymentMode>())
        foreach (var is64 in new[] { true, false })
        {
            var recommended = Dlss5RouteCatalog.Recommend(Assess(mode, is64), Dlss5InstallProfile.MaximumQuality);
            var routes = Dlss5RouteCatalog.Build(Assess(mode, is64), recommended);
            Assert.True(routes.Count(r => r.Recommended) <= 1, $"{mode}/{is64} marked more than one route recommended");
        }
    }

    [Fact]
    public void RecommendedRouteIsSupportedAndMatchesTheRecommendation()
    {
        var recommended = Dlss5RouteCatalog.Recommend(Assess(Dlss5DeploymentMode.NativeDirectX12, true),
            Dlss5InstallProfile.MaximumQuality);
        var routes = Dlss5RouteCatalog.Build(Assess(Dlss5DeploymentMode.NativeDirectX12, true), recommended);

        var marked = routes.Single(r => r.Recommended);
        Assert.Equal(recommended, marked.Profile);
        Assert.True(marked.Supported);
        Assert.Contains("Recommended", marked.StatusText);
    }

    [Fact]
    public void InstalledProfileIsMarkedInstalled()
    {
        const Dlss5InstallProfile installed = Dlss5InstallProfile.StandaloneAio;
        var routes = Dlss5RouteCatalog.Build(Assess(Dlss5DeploymentMode.NativeDirectX12, true),
            Dlss5InstallProfile.MaximumQuality, installed);

        var aio = routes.Single(r => r.Profile == installed);
        Assert.True(aio.Installed);
        Assert.Contains("Installed", aio.StatusText);

        // No other route claims the installed marker.
        Assert.Single(routes.Where(r => r.Installed));
    }

    [Fact]
    public void ThirtyTwoBitGameCannotUseSixtyFourBitOnlyRoutes()
    {
        var routes = Dlss5RouteCatalog.Build(Assess(Dlss5DeploymentMode.Dx11Feeder, is64: false),
            Dlss5InstallProfile.MaximumQuality);

        // ShortFuse, AIO, OpenGL bridge, Neural Upstream and the OptiScaler forks all require 64-bit.
        foreach (var profile in new[]
                 {
                     Dlss5InstallProfile.ExperimentalUnified, Dlss5InstallProfile.StandaloneAio,
                     Dlss5InstallProfile.OpenGlBridge, Dlss5InstallProfile.NeuralUpstream,
                     Dlss5InstallProfile.OptiScalerNeuralRendering,
                 })
            Assert.False(routes.Single(r => r.Profile == profile).Supported, $"{profile} must be unsupported on a 32-bit game");
    }

    [Theory]
    [InlineData(Dlss5InstallProfile.NeuralUpstream)]
    [InlineData(Dlss5InstallProfile.OptiScalerNeuralRendering)]
    public void RecommendKeepsASupportedInstalledProfile(Dlss5InstallProfile installed)
    {
        // An existing install of a route that this game supports keeps its profile as the recommendation.
        var recommended = Dlss5RouteCatalog.Recommend(Assess(Dlss5DeploymentMode.NativeDirectX12, true), installed);
        Assert.Equal(installed, recommended);
    }

    [Fact]
    public void RecommendFallsBackToStableForAFreshNativeInstall()
    {
        var recommended = Dlss5RouteCatalog.Recommend(Assess(Dlss5DeploymentMode.NativeDirectX12, true),
            Dlss5InstallProfile.MaximumQuality);
        Assert.Equal(Dlss5InstallProfile.MaximumQuality, recommended);
    }
}
