using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class GameProcessServiceTests
{
    [Fact]
    public void FindRunningProcesses_DetectsExecutableInsideGameFolder()
    {
        var processPath = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(processPath));

        var gameFolder = Path.GetDirectoryName(processPath!)!;
        var matches = GameProcessService.FindRunningProcesses(gameFolder);

        Assert.Contains(matches, process => process.Id == Environment.ProcessId);
    }

    [Theory]
    [InlineData(@"D:\Games\REANIMAL", @"D:\Games\REANIMAL\Everholm\Binaries\Win64\REANIMAL.exe", true)]
    [InlineData(@"D:\Games\REANIMAL", @"D:\Games\REANIMAL2\REANIMAL.exe", false)]
    public void IsExecutableInsideFolder_RequiresDirectoryBoundary(string folder, string executable, bool expected)
    {
        Assert.Equal(expected, GameProcessService.IsExecutableInsideFolder(folder, executable));
    }
}
