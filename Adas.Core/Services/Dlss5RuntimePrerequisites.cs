namespace RenoDXCommander.Services;

internal static class Dlss5RuntimePrerequisites
{
    internal static string DownloadUrl(string architecture) => architecture switch
    {
        "x64" => "https://aka.ms/vc14/vc_redist.x64.exe",
        "x86" => "https://aka.ms/vc14/vc_redist.x86.exe",
        _ => throw new ArgumentException("Unknown runtime architecture.", nameof(architecture)),
    };

    internal static IReadOnlyList<string> MissingArchitectures(string root, bool is64Bit,
        Func<string, bool, bool>? compatibleFile = null, string? windows = null)
    {
        compatibleFile ??= (path, x86) => File.Exists(path) && AddonPackService.IsAddonArchitectureCompatible(path, x86);
        windows ??= Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var missing = new List<string>();
        foreach (var architecture in is64Bit ? new[] { "x64" } : new[] { "x86", "x64" })
        {
            var x86 = architecture == "x86";
            var local = !is64Bit && !x86 ? Path.Combine(root, "host64") : root;
            var system = Path.Combine(windows, x86 ? "SysWOW64" : "System32");
            var names = x86 ? new[] { "msvcp140.dll", "vcruntime140.dll" }
                : new[] { "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll" };
            if (names.Any(name => !compatibleFile(Path.Combine(local, name), x86)
                && (File.Exists(Path.Combine(local, name)) || !compatibleFile(Path.Combine(system, name), x86)))) missing.Add(architecture);
        }
        return missing;
    }

    internal static void EnsureAvailable(string root, bool is64Bit)
    {
        var missing = MissingArchitectures(root, is64Bit);
        if (missing.Count > 0)
            throw new InvalidOperationException("Install the Microsoft Visual C++ runtime before installing DLSS: "
                + string.Join(", ", missing.Select(architecture => $"{architecture}: {DownloadUrl(architecture)}")));
    }
}
