using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Guards the Luma-Framework pin. The generic Unreal Engine framework zip must be fetched from the
/// pinned release tag (not the rolling <c>/releases/latest/</c> URL) so installs are reproducible,
/// and the "update available" check is capped at the pinned build. Named per-game mods still come
/// from the live wiki and are intentionally not covered here.
/// </summary>
public sealed class LumaPinTests
{
    [Fact]
    public void PinnedTagMatchesTheBuildNumber()
    {
        Assert.Equal($"latest-{LumaService.LumaPinnedBuild}", LumaService.LumaPinnedTag);
    }

    [Fact]
    public void GenericUnrealEngineUrlTargetsThePinnedTagNotLatest()
    {
        var url = LumaService.GenericUnrealEngineZipUrl;

        // Points at the exact pinned release, never the rolling "latest" alias.
        Assert.Contains($"/releases/download/{LumaService.LumaPinnedTag}/", url);
        Assert.DoesNotContain("/releases/latest/", url);
        Assert.EndsWith("/Luma-Unreal_Engine.zip", url);

        // The install guard only accepts Filoppi-hosted downloads — the pinned URL must satisfy it.
        Assert.StartsWith("https://github.com/Filoppi/", url);
    }
}
