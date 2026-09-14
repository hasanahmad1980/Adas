using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class FullScreenWrapperTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 5090", "616.64", 0)]
    [InlineData("NVIDIA GeForce RTX 5070 Ti", "620.10", 0)]
    [InlineData("NVIDIA GeForce RTX 4090", "616.64", 1)]
    [InlineData("NVIDIA GeForce RTX 5080", "581.15", 1)]
    [InlineData(null, "581.15", 2)]
    [InlineData("NVIDIA GeForce RTX 5080", null, 0)]
    public void WarnsOnGpuAndDriver(string? gpu, string? driver, int expected)
        => Assert.Equal(expected, Dlss5ComponentService.FullScreenWrapperWarnings(gpu, driver).Count);
}
