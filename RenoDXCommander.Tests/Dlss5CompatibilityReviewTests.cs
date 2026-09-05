using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class Dlss5CompatibilityReviewTests
{
    [Fact]
    public void Probe_DetectsNativeDlssInUnrealEnginePluginOutsideBinaryFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-ue-native-dlss-{Guid.NewGuid():N}");
        var binary = Path.Combine(root, "Everholm", "Binaries", "Win64");
        var plugin = Path.Combine(root, "Engine", "Plugins", "Marketplace", "DLSS", "Binaries", "ThirdParty", "Win64");
        Directory.CreateDirectory(binary);
        Directory.CreateDirectory(plugin);

        try
        {
            WriteFakeExecutable(Path.Combine(binary, "REANIMAL.exe"), "d3d12.dll\0D3D12CreateDevice");
            var managedRuntime = Path.Combine(binary, "nvngx_dlss.dll");
            File.WriteAllText(managedRuntime, "suite-owned runtime");
            File.WriteAllText(Path.Combine(plugin, "nvngx_dlss.dll"), "game-owned native runtime");
            Dlss5ComponentService.SaveRecord(binary, new Dlss5InstallRecord
            {
                Mode = Dlss5DeploymentMode.Dx12Feeder,
                InstalledHashes = { [managedRuntime] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(managedRuntime))) },
                OriginalBackups = { [managedRuntime] = null },
            });
            var card = new GameCardViewModel { GameName = "Reanimal", InstallPath = binary };
            var service = new Dlss5CompatibilityService(new PeHeaderService());

            var probe = service.Probe(card, GraphicsApiType.DirectX12);
            var assessment = Dlss5CompatibilityService.Assess(probe, singlePlayerConfirmed: true);

            Assert.True(probe.HasNativeDlss);
            Assert.Equal(Dlss5DeploymentMode.NativeDirectX12, assessment.Mode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Probe_UsesManualRendererWhenRuntimeDetectionIsAmbiguous()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-renderer-override-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var executable = Path.Combine(root, "game.exe");
            var bytes = new byte[8192];
            bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
            BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3c);
            bytes[0x80] = (byte)'P'; bytes[0x81] = (byte)'E';
            BitConverter.GetBytes((ushort)MachineType.x64).CopyTo(bytes, 0x84);
            BitConverter.GetBytes((ushort)240).CopyTo(bytes, 0x94);
            BitConverter.GetBytes((ushort)0x20b).CopyTo(bytes, 0x98);
            System.Text.Encoding.ASCII.GetBytes("d3d11.dll\0D3D11CreateDevice\0d3d12.dll\0D3D12CreateDevice")
                .CopyTo(bytes, 5000);
            File.WriteAllBytes(executable, bytes);
            var card = new GameCardViewModel { GameName = "Ambiguous Game", InstallPath = root };
            var service = new Dlss5CompatibilityService(new PeHeaderService());

            var result = service.Probe(card, GraphicsApiType.DirectX12);

            Assert.Equal(GraphicsApiType.DirectX12, result.GraphicsApi);
            Assert.Contains("selected manually", result.GraphicsApiEvidence, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveDeploymentPath_OwnershipRecordWinsOverManagedHostProxy()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-host-path-test-{Guid.NewGuid():N}");
        var host = Path.Combine(root, "host64");
        Directory.CreateDirectory(host);

        try
        {
            File.WriteAllBytes(Path.Combine(root, "game.exe"), new byte[] { (byte)'M', (byte)'Z' });
            File.WriteAllBytes(Path.Combine(root, "dxgi.dll"), new byte[] { (byte)'M', (byte)'Z' });
            File.WriteAllBytes(Path.Combine(host, "dxgi.dll"), new byte[] { (byte)'M', (byte)'Z' });
            Dlss5ComponentService.SaveRecord(root, new Dlss5InstallRecord());

            var result = Dlss5CompatibilityService.ResolveDeploymentPath(root);

            Assert.Equal(Dlss5PathResolutionKind.Resolved, result.Kind);
            Assert.Equal(root, result.Path);
            Assert.Equal(new[] { root }, result.Candidates);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveDeploymentPath_UnmanagedHostFolderRemainsAValidCandidate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-host-path-test-{Guid.NewGuid():N}");
        var host = Path.Combine(root, "host64");
        Directory.CreateDirectory(host);

        try
        {
            File.WriteAllBytes(Path.Combine(root, "dxgi.dll"), new byte[] { (byte)'M', (byte)'Z' });
            File.WriteAllBytes(Path.Combine(host, "dxgi.dll"), new byte[] { (byte)'M', (byte)'Z' });

            var result = Dlss5CompatibilityService.ResolveDeploymentPath(root);

            Assert.Equal(Dlss5PathResolutionKind.Ambiguous, result.Kind);
            Assert.Null(result.Path);
            Assert.Equal(2, result.Candidates.Count);
            Assert.Contains(host, result.Candidates);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveDeploymentPath_NestedGameBinaryOverridesStaleRootInstall()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-nested-path-test-{Guid.NewGuid():N}");
        var binary = Path.Combine(root, "Bin64");
        Directory.CreateDirectory(binary);

        try
        {
            File.WriteAllBytes(Path.Combine(root, "GameLauncher.exe"), new byte[64]);
            File.WriteAllBytes(Path.Combine(binary, "Game.x64.exe"), new byte[1024]);
            File.WriteAllBytes(Path.Combine(root, "dxgi.dll"), new byte[] { (byte)'M', (byte)'Z' });
            Dlss5ComponentService.SaveRecord(root, new Dlss5InstallRecord());

            var result = Dlss5CompatibilityService.ResolveDeploymentPath(root);

            Assert.Equal(Dlss5PathResolutionKind.Resolved, result.Kind);
            Assert.Equal(binary, result.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteFakeExecutable(string path, string imports)
    {
        var bytes = new byte[8192];
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3c);
        bytes[0x80] = (byte)'P'; bytes[0x81] = (byte)'E';
        BitConverter.GetBytes((ushort)MachineType.x64).CopyTo(bytes, 0x84);
        BitConverter.GetBytes((ushort)240).CopyTo(bytes, 0x94);
        BitConverter.GetBytes((ushort)0x20b).CopyTo(bytes, 0x98);
        System.Text.Encoding.ASCII.GetBytes(imports).CopyTo(bytes, 5000);
        File.WriteAllBytes(path, bytes);
    }
}
