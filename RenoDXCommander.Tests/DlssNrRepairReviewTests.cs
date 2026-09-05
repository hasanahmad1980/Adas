using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class DlssNrRepairReviewTests
{
    [Fact]
    public void DiscoverTargetPaths_CustomAddonPathTargetsOwningGameRootAndHostSeparately()
    {
        var root = Path.Combine(Path.GetTempPath(), $"adas-nr-target-test-{Guid.NewGuid():N}");
        var customAddons = Path.Combine(root, "reshade-addons");
        var host = Path.Combine(root, "host64");
        Directory.CreateDirectory(customAddons);
        Directory.CreateDirectory(host);

        try
        {
            File.WriteAllText(Path.Combine(root, "reshade.ini"), "[ADDON]\nAddonPath=.\\reshade-addons\n");
            var gameAddon = Path.Combine(customAddons, DlssNrRepairService.AddonName);
            var hostAddon = Path.Combine(host, DlssNrRepairService.AddonName);
            File.WriteAllBytes(gameAddon, Array.Empty<byte>());
            File.WriteAllBytes(hostAddon, Array.Empty<byte>());

            var targets = DlssNrRepairService.DiscoverTargetPaths(
                root,
                new[] { gameAddon, hostAddon },
                deployIfAddonPresent: true);

            Assert.Contains(Path.Combine(root, DlssNrRepairService.DllName), targets);
            Assert.Contains(Path.Combine(host, DlssNrRepairService.DllName), targets);
            Assert.DoesNotContain(Path.Combine(customAddons, DlssNrRepairService.DllName), targets);
            Assert.Equal(2, targets.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
