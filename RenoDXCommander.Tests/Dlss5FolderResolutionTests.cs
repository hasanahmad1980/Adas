using System;
using System.IO;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public sealed class Dlss5FolderResolutionTests
{
    // Reproduces the F.E.A.R. 2 shape: game .exe at the root plus an uninstaller in
    // an \Uninstall subfolder. The uninstaller folder must NOT create a false
    // "multiple equally likely game binary folders" ambiguity.
    [Fact]
    public void ResolveDeploymentPath_IgnoresUninstallerFolder()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "FEAR2.exe"), "stub");
        File.WriteAllText(Path.Combine(temp.Path, "language_setup.exe"), "stub");
        Directory.CreateDirectory(Path.Combine(temp.Path, "Uninstall"));
        File.WriteAllText(Path.Combine(temp.Path, "Uninstall", "unins000.exe"), "stub");

        var resolution = Dlss5CompatibilityService.ResolveDeploymentPath(temp.Path);

        Assert.Equal(Dlss5PathResolutionKind.Resolved, resolution.Kind);
        Assert.Equal(Path.GetFullPath(temp.Path), resolution.Path!, ignoreCase: true);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "dlss5-folder-tests-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
