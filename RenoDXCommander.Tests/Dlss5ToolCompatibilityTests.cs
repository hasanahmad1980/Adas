using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class Dlss5ToolCompatibilityTests
{
    private static ToolContext Context(
        GraphicsApiType api,
        Dlss5DeploymentMode mode = Dlss5DeploymentMode.None,
        Dlss5InstallProfile? route = null,
        bool is64Bit = true,
        bool hasUpscaler = true,
        bool antiCheat = false,
        bool reLimiter = false,
        params SetupTool[] selected) =>
        new(api, is64Bit, mode, route, selected, HasNativeUpscaler: hasUpscaler, HasAntiCheat: antiCheat, ReLimiterInstalled: reLimiter);

    [Fact]
    public void DxvkDoesNothingForDirectX12AndVulkanGames()
    {
        Assert.Equal(ToolNoteLevel.NoEffect,
            Dlss5ToolCompatibility.Worst(Dlss5ToolCompatibility.Notes(SetupTool.Dxvk, Context(GraphicsApiType.DirectX12))));
        Assert.Equal(ToolNoteLevel.NoEffect,
            Dlss5ToolCompatibility.Worst(Dlss5ToolCompatibility.Notes(SetupTool.Dxvk, Context(GraphicsApiType.Vulkan))));
    }

    [Fact]
    public void FeederRouteFlagsStockOptiScalerAndDisplayCommanderButNeverRemovesThem()
    {
        var context = Context(GraphicsApiType.DirectX11, Dlss5DeploymentMode.Dx11Feeder, Dlss5InstallProfile.MaximumQuality,
            selected: new[] { SetupTool.OptiScaler, SetupTool.DisplayCommander });

        Assert.True(Dlss5ToolCompatibility.IsFeederRoute(context));
        var optiScaler = Dlss5ToolCompatibility.Notes(SetupTool.OptiScaler, context);
        Assert.Equal(ToolNoteLevel.Conflict, Dlss5ToolCompatibility.Worst(optiScaler));
        Assert.Contains(optiScaler, n => n.Text.Contains("Feeder"));
        var displayCommander = Dlss5ToolCompatibility.Notes(SetupTool.DisplayCommander, context);
        Assert.Equal(ToolNoteLevel.Conflict, Dlss5ToolCompatibility.Worst(displayCommander));
        Assert.Contains(displayCommander, n => n.Text.Contains("Feeder"));
    }

    [Fact]
    public void NativeDirectX12RouteIsNotAFeederRouteAndDisplayCommanderWorksOnTheRoutesReShade()
    {
        var context = Context(GraphicsApiType.DirectX12, Dlss5DeploymentMode.NativeDirectX12, Dlss5InstallProfile.MaximumQuality,
            selected: SetupTool.DisplayCommander);

        Assert.False(Dlss5ToolCompatibility.IsFeederRoute(context));
        Assert.Equal(ToolNoteLevel.Good,
            Dlss5ToolCompatibility.Worst(Dlss5ToolCompatibility.Notes(SetupTool.DisplayCommander, context)));
    }

    [Fact]
    public void ReShadeIsAlreadyIncludedWithARouteAndConflictsWithTheOptiScalerNrRoute()
    {
        var withRoute = Context(GraphicsApiType.DirectX12, Dlss5DeploymentMode.NativeDirectX12, Dlss5InstallProfile.MaximumQuality);
        Assert.Equal(ToolNoteLevel.NoEffect,
            Dlss5ToolCompatibility.Worst(Dlss5ToolCompatibility.Notes(SetupTool.ReShade, withRoute)));

        var nr = withRoute with { Route = Dlss5InstallProfile.OptiScalerNeuralRendering };
        Assert.Equal(ToolNoteLevel.Conflict,
            Dlss5ToolCompatibility.Worst(Dlss5ToolCompatibility.Notes(SetupTool.ReShade, nr)));
    }

    [Fact]
    public void OptiScalerNeedsA64BitModernApiGame()
    {
        var notes = Dlss5ToolCompatibility.Notes(SetupTool.OptiScaler, Context(GraphicsApiType.DirectX9, is64Bit: false));
        Assert.Equal(ToolNoteLevel.Conflict, Dlss5ToolCompatibility.Worst(notes));
        Assert.Contains(notes, n => n.Text.Contains("64-bit"));
        Assert.Contains(notes, n => n.Text.Contains("DirectX 11, DirectX 12 and Vulkan"));

        Assert.Equal(ToolNoteLevel.Good,
            Dlss5ToolCompatibility.Worst(Dlss5ToolCompatibility.Notes(SetupTool.OptiScaler, Context(GraphicsApiType.DirectX12))));
        Assert.Equal(ToolNoteLevel.Caution,
            Dlss5ToolCompatibility.Worst(Dlss5ToolCompatibility.Notes(SetupTool.OptiScaler, Context(GraphicsApiType.DirectX12, hasUpscaler: false))));
    }

    [Fact]
    public void DisplayCommanderAsksForReShadeAndConflictsWithReLimiter()
    {
        var alone = Dlss5ToolCompatibility.Notes(SetupTool.DisplayCommander, Context(GraphicsApiType.DirectX11));
        Assert.Equal(ToolNoteLevel.Caution, Dlss5ToolCompatibility.Worst(alone));

        var withReShade = Context(GraphicsApiType.DirectX11, selected: new[] { SetupTool.ReShade, SetupTool.DisplayCommander });
        Assert.Equal(ToolNoteLevel.Good,
            Dlss5ToolCompatibility.Worst(Dlss5ToolCompatibility.Notes(SetupTool.DisplayCommander, withReShade)));

        Assert.Equal(ToolNoteLevel.Conflict,
            Dlss5ToolCompatibility.Worst(Dlss5ToolCompatibility.Notes(SetupTool.DisplayCommander, withReShade with { ReLimiterInstalled = true })));
    }

    [Fact]
    public void DxvkInsideAnAlreadyDxvkRouteIsIncludedAndAntiCheatIsAConflict()
    {
        var included = Context(GraphicsApiType.DirectX9, Dlss5DeploymentMode.Dx9ViaDxvkFeeder, Dlss5InstallProfile.MaximumQuality, is64Bit: false);
        Assert.Contains(Dlss5ToolCompatibility.Notes(SetupTool.Dxvk, included), n => n.Level == ToolNoteLevel.NoEffect);

        var antiCheat = Context(GraphicsApiType.DirectX11, antiCheat: true);
        Assert.Equal(ToolNoteLevel.Conflict,
            Dlss5ToolCompatibility.Worst(Dlss5ToolCompatibility.Notes(SetupTool.Dxvk, antiCheat)));
    }

    [Fact]
    public void UpscalerRuntimeIsFoundInSubfolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "adas-ups-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "bin", "plugins"));
            Assert.False(Dlss5ToolCompatibility.HasUpscalerRuntime(root));
            File.WriteAllBytes(Path.Combine(root, "bin", "plugins", "libxess.dll"), new byte[] { 0 });
            Assert.True(Dlss5ToolCompatibility.HasUpscalerRuntime(root));
            Assert.True(Dlss5ToolCompatibility.HasUpscalerRuntime(null, hasNativeDlss: true));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
