using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class GameCleanupServiceTests
{
    [Fact]
    public void FindsKnownDlss5AndReShadeLeftoversInSubfoldersWithoutTouchingNativeDlss()
    {
        var root = Directory.CreateTempSubdirectory("adas-cleanup-plan-").FullName;
        try
        {
            var binary = Directory.CreateDirectory(Path.Combine(root, "Binaries", "Win64")).FullName;
            var addon = Path.Combine(binary, "dlss5-feed.addon64");
            var config = Path.Combine(binary, "ReShade.ini");
            var nativeDlss = Path.Combine(binary, "nvngx_dlss.dll");
            var unrelated = Path.Combine(binary, "game-data.dll");
            File.WriteAllText(addon, "mod");
            File.WriteAllText(config, "mod");
            File.WriteAllText(nativeDlss, "native game runtime");
            File.WriteAllText(unrelated, "game");

            var leftovers = GameCleanupService.FindKnownLeftovers(root, _ => false);

            Assert.Contains(addon, leftovers.Files);
            Assert.Contains(config, leftovers.Files);
            Assert.DoesNotContain(nativeDlss, leftovers.Files);
            Assert.DoesNotContain(unrelated, leftovers.Files);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CleanupArchivesRecognizedFilesOutsideTheGameAndRestoresOriginalProxy()
    {
        var root = Directory.CreateTempSubdirectory("adas-cleanup-game-").FullName;
        var recovery = Directory.CreateTempSubdirectory("adas-cleanup-recovery-").FullName;
        try
        {
            var addon = Path.Combine(root, "renodx-dlss.addon64");
            var proxy = Path.Combine(root, "dxgi.dll");
            File.WriteAllText(addon, "mod");
            File.WriteAllText(proxy, "reshade");
            File.WriteAllText(proxy + ".original", "original game proxy");
            var leftovers = new GameCleanupLeftovers(new[] { addon, proxy }, Array.Empty<string>());

            var archived = GameCleanupService.ArchiveLeftovers(root, leftovers, recovery);

            Assert.Equal(2, archived.Count);
            Assert.False(File.Exists(addon));
            Assert.Equal("original game proxy", File.ReadAllText(proxy));
            Assert.True(Directory.EnumerateFiles(recovery, "*", SearchOption.AllDirectories).Any());
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(recovery, true);
        }
    }
}
