using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class Dlss5SetupAutomationTests
{
    [Fact]
    public void ConfirmedDeploymentPathClearsOnlyFolderResolutionBlockers()
    {
        var assessment = new Dlss5Assessment(
            Dlss5DeploymentMode.NativeDirectX12,
            null,
            new[]
            {
                Dlss5CompatibilityService.AmbiguousDeploymentPathReason,
                "Detected anti-cheat software: test. Adas will not modify this game.",
            },
            Array.Empty<string>(),
            SinglePlayerConfirmed: true,
            Is64Bit: true);

        var confirmed = Dlss5CompatibilityService.ConfirmDeploymentPath(
            assessment,
            @"C:\Games\Example\Binaries\Win64");

        Assert.Equal(@"C:\Games\Example\Binaries\Win64", confirmed.DeploymentPath);
        Assert.DoesNotContain(Dlss5CompatibilityService.AmbiguousDeploymentPathReason, confirmed.BlockingReasons);
        Assert.Single(confirmed.BlockingReasons);
        Assert.False(confirmed.CanInstall);
        Assert.False(Dlss5CompatibilityService.CanConfirmDeploymentPath(assessment));
    }

    [Fact]
    public void ConfirmedDeploymentPathMakesFolderOnlyAssessmentInstallable()
    {
        var assessment = new Dlss5Assessment(
            Dlss5DeploymentMode.Dx11Feeder,
            null,
            new[] { Dlss5CompatibilityService.MissingDeploymentPathReason },
            Array.Empty<string>(),
            SinglePlayerConfirmed: true,
            Is64Bit: true);

        var confirmed = Dlss5CompatibilityService.ConfirmDeploymentPath(assessment, @"C:\Games\Example");

        Assert.True(confirmed.CanInstall);
        Assert.Empty(confirmed.BlockingReasons);
        Assert.True(Dlss5CompatibilityService.CanConfirmDeploymentPath(assessment));
    }

    [Fact]
    public void RuntimeChecksRequireBothArchitecturesForHostedGames()
    {
        Assert.Equal(new[] { "x86", "x64" }, Dlss5RuntimePrerequisites.MissingArchitectures("game", false, (_, _) => false, "windows"));
        Assert.Equal(new[] { "x64" }, Dlss5RuntimePrerequisites.MissingArchitectures("game", true, (_, _) => false, "windows"));
        Assert.Equal(new[] { "x86" }, Dlss5RuntimePrerequisites.MissingArchitectures("game", false, (_, x86) => !x86, "windows"));
    }

    [Fact]
    public void WrongArchitectureLocalRuntimeCannotBeMaskedByAValidSystemRuntime()
    {
        var root = Directory.CreateTempSubdirectory("adas-vcredist-test-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "msvcp140.dll"), "wrong architecture");
            var missing = Dlss5RuntimePrerequisites.MissingArchitectures(root, true,
                (path, _) => !path.StartsWith(root, StringComparison.OrdinalIgnoreCase), "windows");
            Assert.Equal(new[] { "x64" }, missing);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RuntimeChecksAcceptCompleteAppLocalRuntimeWithoutChangingFiles()
    {
        var requested = new List<string>();
        var missing = Dlss5RuntimePrerequisites.MissingArchitectures("game", false, (path, _) =>
        {
            requested.Add(path);
            return path.StartsWith("game");
        }, "windows");
        Assert.Empty(missing);
        Assert.Contains(Path.Combine("game", "host64", "vcruntime140_1.dll"), requested);
        Assert.Throws<ArgumentException>(() => Dlss5RuntimePrerequisites.DownloadUrl("invalid"));
    }

    [Theory]
    [InlineData("PCSX2-QT.exe", "PCSX2")]
    [InlineData("rpcs3.exe", "RPCS3")]
    [InlineData("dolphin.exe", "Dolphin")]
    [InlineData("xenia_canary.exe", "Xenia")]
    public void KnownEmulatorsHaveExplicitRendererProfiles(string executable, string name)
        => Assert.Equal(name, Dlss5EmulatorService.ForExecutable(executable)!.Name);

    [Fact]
    public void EmulatorExecutableChoiceDoesNotSwitchToTheOtherArchitecture()
    {
        var root = Directory.CreateTempSubdirectory("adas-emulator-choice-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "ppssppwindows64.exe"), "64");
            var selected = Path.Combine(root, "ppssppwindows.exe");
            File.WriteAllText(selected, "32");
            var installation = new Dlss5EmulatorInstallation(Dlss5EmulatorService.ForExecutable(selected)!, selected);
            Dlss5EmulatorService.SaveExecutable(root, installation, root);
            Assert.Equal(selected, Dlss5EmulatorService.FindInstallation(root, root)!.Executable);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void EmulatorChoicePersistsPerExecutableAndRejectsUnsupportedRenderer()
    {
        var root = Directory.CreateTempSubdirectory("adas-emulator-test-").FullName;
        try
        {
            var exe = Path.Combine(root, "rpcs3.exe");
            File.WriteAllText(exe, "test");
            var installation = Dlss5EmulatorService.FindInstallation(root)!;
            Assert.Null(Dlss5EmulatorService.LoadRenderer(installation, root));
            Dlss5EmulatorService.SaveRenderer(installation, GraphicsApiType.Vulkan, root);
            Assert.Equal(GraphicsApiType.Vulkan, Dlss5EmulatorService.LoadRenderer(installation, root));
            Assert.Throws<ArgumentException>(() => Dlss5EmulatorService.SaveRenderer(installation, GraphicsApiType.DirectX9, root));
            File.WriteAllText(Path.Combine(root, "dolphin.exe"), "test");
            Assert.Null(Dlss5EmulatorService.FindInstallation(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
