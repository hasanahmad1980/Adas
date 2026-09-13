using System.Text;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class GraphicsEnvironmentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "adas-api-" + Guid.NewGuid().ToString("N"));
    public GraphicsEnvironmentTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    private string Exe(params string[] markers)
    {
        var path = Path.Combine(root, "game.exe");
        var bytes = new byte[8192];
        bytes[0] = 77; bytes[1] = 90;
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3c);
        bytes[0x80] = 80; bytes[0x81] = 69;
        BitConverter.GetBytes((ushort)MachineType.x64).CopyTo(bytes, 0x84);
        BitConverter.GetBytes((ushort)240).CopyTo(bytes, 0x94);
        BitConverter.GetBytes((ushort)0x20b).CopyTo(bytes, 0x98);
        Encoding.ASCII.GetBytes(string.Join('\0', markers)).CopyTo(bytes, 5000);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Theory]
    [InlineData(1, GraphicsApiType.DirectX9)]
    [InlineData(2, GraphicsApiType.DirectX11)]   // Direct3D11 (was wrongly mapped to DX9)
    [InlineData(18, GraphicsApiType.DirectX12)]
    [InlineData(21, GraphicsApiType.Vulkan)]
    [InlineData(17, GraphicsApiType.OpenGL)]     // OpenGLCore (was wrongly mapped to DX11)
    [InlineData(0, GraphicsApiType.OpenGL)]      // legacy OpenGL2
    [InlineData(4, GraphicsApiType.Unknown)]     // Null device (was wrongly mapped to OpenGL)
    [InlineData(16, GraphicsApiType.Unknown)]    // Metal (not a Windows renderer)
    public void UnityBootConfigMapsGfxDeviceTypeToTheCorrectApi(int deviceType, GraphicsApiType expected)
    {
        var data = Path.Combine(root, "Game_Data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "boot.config"), $"gfx-device-type={deviceType}\n");
        Assert.Equal(expected, GraphicsApiDetector.DetectUnityFromBootConfig(root));
    }

    [Fact]
    public void MultipleApisDoNotSelectTheHighestOrAssumeDx11()
    {
        Exe("d3d9.dll", "Direct3DCreate9", "d3d11.dll", "D3D11CreateDevice");
        var result = GraphicsEnvironmentService.Detect(root);
        Assert.Equal(GraphicsApiType.Unknown, result.Api);
        Assert.Null(result.ReShadeProxy);
        Assert.Contains(GraphicsApiType.DirectX9, result.SupportedApis);
        Assert.Contains(GraphicsApiType.DirectX11, result.SupportedApis);
    }

    [Fact]
    public void ManualRendererOverrideBecomesInstallationAuthority()
    {
        Exe("d3d11.dll", "D3D11CreateDevice", "d3d12.dll", "D3D12CreateDevice");
        var detected = GraphicsEnvironmentService.Detect(root);

        var result = GraphicsEnvironmentService.ApplyUserOverride(detected, GraphicsApiType.DirectX11);

        Assert.Equal(GraphicsApiType.DirectX11, result.Api);
        Assert.Equal("dxgi.dll", result.ReShadeProxy);
        Assert.Contains(GraphicsApiType.DirectX11, result.SupportedApis);
        Assert.Contains("selected manually", result.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("d3d9.dll", "Direct3DCreate9", GraphicsApiType.DirectX9, "d3d9.dll")]
    [InlineData("d3d11.dll", "D3D11CreateDevice", GraphicsApiType.DirectX11, "dxgi.dll")]
    [InlineData("d3d12.dll", "D3D12CreateDevice", GraphicsApiType.DirectX12, "dxgi.dll")]
    [InlineData("opengl32.dll", "wglCreateContext", GraphicsApiType.OpenGL, "opengl32.dll")]
    [InlineData("vulkan-1.dll", "vkCreateInstance", GraphicsApiType.Vulkan, null)]
    public void SingleApiUsesCorrectHook(string dll, string entry, GraphicsApiType api, string? hook)
    {
        Exe(dll, entry);
        var result = GraphicsEnvironmentService.Detect(root);
        Assert.Equal(api, result.Api);
        Assert.Equal(hook, result.ReShadeProxy);
        Assert.Equal(MachineType.x64, result.Machine);
    }

    [Fact]
    public void Dx10To12CanShareAReShadeHookWithoutInventingAnActiveApi()
    {
        Exe("d3d11.dll", "D3D11CreateDevice", "d3d12.dll", "D3D12CreateDevice");
        var result = GraphicsEnvironmentService.Detect(root);
        Assert.Equal(GraphicsApiType.Unknown, result.Api);
        Assert.Equal("dxgi.dll", result.ReShadeProxy);
    }

    [Fact]
    public void UnityWithoutExplicitRendererDoesNotDefaultToDx11()
    {
        Exe();
        Directory.CreateDirectory(Path.Combine(root, "game_Data"));
        Assert.Equal(GraphicsApiType.Unknown, GraphicsApiDetector.DetectUnityFromBootConfig(root));
        Assert.Equal(GraphicsApiType.Unknown, GraphicsEnvironmentService.Detect(root).Api);
    }

    [Fact]
    public void RuntimeLogMustBelongToThisExecutableAndBeCurrent()
    {
        var exe = Exe("d3d9.dll", "Direct3DCreate9", "d3d11.dll", "D3D11CreateDevice");
        var log = Path.Combine(root, "ReShade.log");
        File.WriteAllText(log, $"Initializing crosire's ReShade into '{exe}' ...\nRedirecting D3D11CreateDevice(\nRecreated runtime environment on runtime 123\n");
        Assert.Equal(GraphicsApiType.DirectX11, GraphicsEnvironmentService.Detect(root).Api);
        File.SetLastWriteTimeUtc(log, File.GetLastWriteTimeUtc(exe).AddMinutes(-1));
        Assert.Equal(GraphicsApiType.Unknown, GraphicsEnvironmentService.Detect(root).Api);
        File.WriteAllText(log, "Initializing crosire's ReShade into 'C:\\Other\\game.exe' ...\nRedirecting D3D11CreateDevice(\nRecreated runtime environment on runtime 123\n");
        Assert.Equal(GraphicsApiType.Unknown, GraphicsEnvironmentService.Detect(root).Api);
    }

    [Fact]
    public void HookRegistrationAndPrivateD3D12DevicesAreNotActiveApiProof()
    {
        var exe = Exe("d3d9.dll", "Direct3DCreate9", "d3d11.dll", "D3D11CreateDevice");
        File.WriteAllText(Path.Combine(root, "ReShade.log"), $"Initializing crosire's ReShade into '{exe}' ...\nRegistering hooks for 'd3d12.dll'\n");
        Assert.Equal(GraphicsApiType.Unknown, GraphicsEnvironmentService.Detect(root).Api);
        File.AppendAllText(Path.Combine(root, "ReShade.log"), "Redirecting D3D11CreateDevice(\nRedirecting D3D12CreateDevice(\nRecreated runtime environment on runtime 123\n");
        Assert.Equal(GraphicsApiType.Unknown, GraphicsEnvironmentService.Detect(root).Api);
    }

    [Fact]
    public void ObservationIsInvalidatedWhenConfigurationChanges()
    {
        var exe = Exe("d3d9.dll", "Direct3DCreate9", "d3d11.dll", "D3D11CreateDevice");
        var cache = Path.Combine(root, "observations");
        GraphicsEnvironmentService.SaveObservation(exe, GraphicsApiType.DirectX9, cache);
        Assert.Equal(GraphicsApiType.DirectX9, GraphicsEnvironmentService.Detect(root, observationDirectory: cache).Api);
        File.WriteAllText(Path.Combine(root, "settings.ini"), "new renderer selection");
        Assert.Equal(GraphicsApiType.Unknown, GraphicsEnvironmentService.Detect(root, observationDirectory: cache).Api);
    }

    [Fact]
    public void ObservationSurvivesFilesCreatedByAdasAndReShade()
    {
        var exe = Exe("d3d9.dll", "Direct3DCreate9", "d3d11.dll", "D3D11CreateDevice");
        var cache = Path.Combine(root, "observations");
        GraphicsEnvironmentService.SaveObservation(exe, GraphicsApiType.DirectX9, cache);
        File.WriteAllText(Path.Combine(root, "ReShade.ini"), "[GENERAL]");
        File.WriteAllText(Path.Combine(root, "dlss5-feed.cfg"), "enabled=1");
        Directory.CreateDirectory(Path.Combine(root, "reshade-shaders"));
        Assert.Equal(GraphicsApiType.DirectX9, GraphicsEnvironmentService.Detect(root, observationDirectory: cache).Api);
    }

    [Fact]
    public void RecentObservationFromPreviousFingerprintFormatIsMigratedOnce()
    {
        var exe = Exe("d3d9.dll", "Direct3DCreate9", "d3d11.dll", "D3D11CreateDevice");
        var cache = Path.Combine(root, "observations");
        GraphicsEnvironmentService.SaveObservation(exe, GraphicsApiType.DirectX9, cache);
        var observation = Directory.GetFiles(cache, "*.json").Single();
        File.WriteAllText(observation,
            $$"""{"Fingerprint":"old-format","Api":2,"ObservedUtc":"{{DateTime.UtcNow:O}}"}""");

        Assert.Equal(GraphicsApiType.DirectX9, GraphicsEnvironmentService.Detect(root, observationDirectory: cache).Api);
        Assert.Contains("renderer-inputs-v2", File.ReadAllText(observation));
    }

    [Fact]
    public void WrongBitnessIsDetectedWithoutAnInstallationRecord()
    {
        Exe("d3d11.dll", "D3D11CreateDevice");
        var addon = Path.Combine(root, "wrong.addon64");
        File.Copy(Path.Combine(root, "game.exe"), addon);
        var bytes = File.ReadAllBytes(addon);
        BitConverter.GetBytes((ushort)MachineType.I386).CopyTo(bytes, 0x84);
        File.WriteAllBytes(addon, bytes);
        Assert.Contains(GraphicsEnvironmentService.CheckInstallation(root), issue => issue.Contains("wrong.addon64") && issue.Contains("32-bit"));
    }

    [Theory]
    [InlineData("LogRHI: Loading RHI module D3D12RHI\nLogD3D12RHI: Found D3D12 adapter", GraphicsApiType.DirectX12)]
    [InlineData("LogRHI: RHI D3D12 with Feature Level SM6 is supported and will be used.", GraphicsApiType.DirectX12)]
    [InlineData("LogRHI: Loading RHI module D3D12RHI\nLogRHI: Loading RHI module D3D11RHI", GraphicsApiType.DirectX11)]
    [InlineData("LogVulkanRHI: Display: Found 1 device(s)\nLogVulkanRHI: Using device 0", GraphicsApiType.Vulkan)]
    [InlineData("LogInit: nothing about rendering", GraphicsApiType.Unknown)]
    public void UnrealLogNamesTheRhiThatActuallyRan(string log, GraphicsApiType expected) =>
        Assert.Equal(expected, GraphicsEnvironmentService.UnrealRhiFromLog(log));

    [Theory]
    [InlineData("[/Script/Engine.GameUserSettings]\nPreferredRHI=dx11\n", GraphicsApiType.DirectX11)]
    [InlineData("[/Script/WindowsTargetPlatform.WindowsTargetSettings]\nDefaultGraphicsRHI=DefaultGraphicsRHI_Vulkan\n", GraphicsApiType.Vulkan)]
    [InlineData("[Core.Log]\n", GraphicsApiType.Unknown)]
    public void UnrealConfigNamesThePreferredRhi(string ini, GraphicsApiType expected) =>
        Assert.Equal(expected, GraphicsEnvironmentService.UnrealRhiFromConfig(ini));

    [Fact]
    public void BestGuessUsesTheUnrealLogForAMultiRhiGame()
    {
        Exe("d3d11.dll", "D3D11CreateDevice", "d3d12.dll", "D3D12CreateDevice");
        var bin = Path.Combine(root, "Hellbreak", "Binaries", "Win64");
        Directory.CreateDirectory(bin);
        var exe = Path.Combine(bin, "Hellbreak-Win64-Shipping.exe");
        File.Move(Path.Combine(root, "game.exe"), exe);
        var localAppData = Path.Combine(root, "lad");
        var logs = Path.Combine(localAppData, "Hellbreak", "Saved", "Logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "Hellbreak.log"), "LogRHI: Loading RHI module D3D12RHI\n");

        var result = GraphicsEnvironmentService.DetectWithBestGuess(Path.Combine(root, "Hellbreak"), exe,
            Path.Combine(root, "observations"), localAppData, Path.Combine(root, "docs"));

        Assert.Equal(GraphicsApiType.DirectX12, result.Api);
        Assert.False(result.IsBestGuess);
        Assert.Contains("Unreal Engine log", result.Evidence);
    }

    [Fact]
    public void BestGuessRecognisesTheDirectX12AgilitySdk()
    {
        var exe = Exe("d3d11.dll", "D3D11CreateDevice", "d3d12.dll", "D3D12CreateDevice");
        Directory.CreateDirectory(Path.Combine(root, "D3D12"));
        File.WriteAllBytes(Path.Combine(root, "D3D12", "D3D12Core.dll"), new byte[] { 0 });

        var result = GraphicsEnvironmentService.DetectWithBestGuess(root, exe,
            Path.Combine(root, "observations"), Path.Combine(root, "lad"), Path.Combine(root, "docs"));

        Assert.Equal(GraphicsApiType.DirectX12, result.Api);
        Assert.True(result.IsBestGuess);
        Assert.Contains("Agility SDK", result.Evidence);
    }

    [Fact]
    public void BestGuessNeverLeavesAMultiApiGameUnknown()
    {
        var exe = Exe("d3d9.dll", "Direct3DCreate9", "d3d11.dll", "D3D11CreateDevice");

        Assert.Equal(GraphicsApiType.Unknown, GraphicsEnvironmentService.Detect(root, exe, Path.Combine(root, "observations")).Api);
        var result = GraphicsEnvironmentService.DetectWithBestGuess(root, exe,
            Path.Combine(root, "observations"), Path.Combine(root, "lad"), Path.Combine(root, "docs"));

        Assert.Contains(result.Api, new[] { GraphicsApiType.DirectX9, GraphicsApiType.DirectX11 });
        Assert.True(result.IsBestGuess);
        Assert.NotNull(result.ReShadeProxy);
    }
}
