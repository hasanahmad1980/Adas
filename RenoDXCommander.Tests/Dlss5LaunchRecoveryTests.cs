using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class Dlss5LaunchRecoveryTests
{
    [Fact]
    public void StackOverflowInDx9DgVoodooRouteActivatesFallback()
    {
        Assert.True(Dlss5LaunchRecoveryService.ShouldActivateDx9Fallback(
            Dlss5DeploymentMode.Dx9Feeder,
            GraphicsApiType.DirectX9,
            unchecked((int)0xC00000FD)));
    }

    [Fact]
    public void MislabeledDx11FeederWithObservedDx9StackOverflowActivatesFallback()
    {
        Assert.True(Dlss5LaunchRecoveryService.ShouldActivateDx9Fallback(
            Dlss5DeploymentMode.Dx11Feeder,
            GraphicsApiType.DirectX9,
            unchecked((int)0xC00000FD)));
    }

    [Theory]
    [InlineData(Dlss5DeploymentMode.Dx9Feeder, GraphicsApiType.DirectX9, 0)]
    [InlineData(Dlss5DeploymentMode.Dx9Feeder, GraphicsApiType.DirectX9, -1)]
    [InlineData(Dlss5DeploymentMode.Dx11Feeder, GraphicsApiType.DirectX11, unchecked((int)0xC00000FD))]
    [InlineData(Dlss5DeploymentMode.Dx9ViaDxvkFeeder, GraphicsApiType.DirectX9, unchecked((int)0xC00000FD))]
    public void OtherExitsAndRoutesDoNotActivateFallback(
        Dlss5DeploymentMode mode,
        GraphicsApiType detectedApi,
        int exitCode)
    {
        Assert.False(Dlss5LaunchRecoveryService.ShouldActivateDx9Fallback(mode, detectedApi, exitCode));
    }

    [Fact]
    public void WindowsEventMustNameThisGamesExecutableAndLocalReshadeModule()
    {
        var root = @"C:\Games\Example";
        var xml = """
            <Events xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
              <Event>
                <System><TimeCreated SystemTime="2026-09-04T12:00:00Z" /></System>
                <EventData>
                  <Data Name="AppPath">C:\Games\Example\game.exe</Data>
                  <Data Name="ModulePath">C:\Games\Example\dxgi.dll</Data>
                  <Data Name="ExceptionCode">c00000fd</Data>
                </EventData>
              </Event>
            </Events>
            """;

        Assert.True(Dlss5LaunchRecoveryService.HasMatchingWindowsCrash(
            xml, root, new DateTime(2026, 9, 4, 11, 0, 0, DateTimeKind.Utc)));
        Assert.False(Dlss5LaunchRecoveryService.HasMatchingWindowsCrash(
            xml.Replace(@"C:\Games\Example\game.exe", @"C:\Games\Other\game.exe"),
            root,
            new DateTime(2026, 9, 4, 11, 0, 0, DateTimeKind.Utc)));
    }
}
