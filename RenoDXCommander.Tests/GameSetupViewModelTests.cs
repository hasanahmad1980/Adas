using System.Linq;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Phase 5 — the per-game setup assessment now lives in a focused, constructor-injected
/// <see cref="GameSetupViewModel"/> instead of inline code-behind reached through a global service
/// locator, so it can be exercised with real engine services and no UI.
/// </summary>
public sealed class GameSetupViewModelTests
{
    private static GameSetupViewModel NewViewModel()
        => new(new Dlss5CompatibilityService(new PeHeaderService()), deepFriedChicken: null);

    [Fact]
    public void Assess_BuildsRoutesAndReportsInstalledState_ForAGameWithADlss5Record()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-setupvm-{Guid.NewGuid():N}");
        var binary = Path.Combine(root, "Game", "Binaries", "Win64");
        var plugin = Path.Combine(root, "Engine", "Plugins", "Marketplace", "DLSS", "Binaries", "ThirdParty", "Win64");
        Directory.CreateDirectory(binary);
        Directory.CreateDirectory(plugin);
        try
        {
            WriteFakeExecutable(Path.Combine(binary, "GAME.exe"), "d3d12.dll\0D3D12CreateDevice");
            var managedRuntime = Path.Combine(binary, "nvngx_dlss.dll");
            File.WriteAllText(managedRuntime, "suite-owned runtime");
            File.WriteAllText(Path.Combine(plugin, "nvngx_dlss.dll"), "game-owned native runtime");
            Dlss5ComponentService.SaveRecord(binary, new Dlss5InstallRecord
            {
                Mode = Dlss5DeploymentMode.Dx12Feeder,
                InstalledHashes = { [managedRuntime] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(managedRuntime))) },
                OriginalBackups = { [managedRuntime] = null },
            });

            var card = new GameCardViewModel { GameName = "Fixture Game", InstallPath = binary };
            var op = Dlss5GameOperation.Capture(card, GraphicsApiType.DirectX12);

            var result = NewViewModel().Assess(op);

            Assert.NotNull(result.Probe);
            Assert.NotNull(result.Assessment);
            Assert.NotEmpty(result.Routes);                 // every route shown, none hidden
            Assert.NotNull(result.Preferred);
            Assert.False(string.IsNullOrWhiteSpace(result.Summary));

            // A saved record means the pane must report "installed" and hand back the record + root.
            Assert.Equal(GameStatus.Installed, result.Dlss5Status);
            Assert.NotNull(result.InstalledRecord);
            Assert.False(string.IsNullOrWhiteSpace(result.InstalledRoot));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void BeginAssessment_SupersedesTheEarlierGeneration()
    {
        var vm = NewViewModel();
        var first = vm.BeginAssessment();
        var second = vm.BeginAssessment();

        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
    }

    private static void WriteFakeExecutable(string path, string ascii)
    {
        var bytes = new byte[8192];
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3c);
        bytes[0x80] = (byte)'P'; bytes[0x81] = (byte)'E';
        BitConverter.GetBytes((ushort)MachineType.x64).CopyTo(bytes, 0x84);
        BitConverter.GetBytes((ushort)240).CopyTo(bytes, 0x94);
        BitConverter.GetBytes((ushort)0x20b).CopyTo(bytes, 0x98);
        System.Text.Encoding.ASCII.GetBytes(ascii).CopyTo(bytes, 5000);
        File.WriteAllBytes(path, bytes);
    }
}
