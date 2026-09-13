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

    /// <summary>
    /// Downloads Microsoft's Visual C++ 2015–2022 redistributable for one architecture from aka.ms and runs it
    /// (Windows asks for permission). Returns null on success, otherwise a plain-language error.
    /// </summary>
    internal static async Task<string?> DownloadAndInstallAsync(string architecture, IProgress<(string message, double percent)>? progress = null)
    {
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "Adas", "vcredist");
            Directory.CreateDirectory(folder);
            var installer = Path.Combine(folder, $"vc_redist.{architecture}.exe");
            progress?.Report(($"Downloading Microsoft Visual C++ runtime ({architecture})...", 5));
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Adas");
                var bytes = await http.GetByteArrayAsync(DownloadUrl(architecture)).ConfigureAwait(false);
                await File.WriteAllBytesAsync(installer, bytes).ConfigureAwait(false);
            }

            progress?.Report(($"Installing Microsoft Visual C++ runtime ({architecture})...", 10));
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = installer,
                Arguments = "/install /passive /norestart",
                UseShellExecute = true,
                Verb = "runas",
            });
            if (process == null) return "The Microsoft Visual C++ installer did not start.";
            await process.WaitForExitAsync().ConfigureAwait(false);
            // 0 = installed, 1638 = a newer version is already installed, 3010 = installed, restart pending.
            return process.ExitCode is 0 or 1638 or 3010
                ? null
                : $"The Microsoft Visual C++ installer exited with code {process.ExitCode}.";
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return "Windows permission was declined, so the Microsoft Visual C++ runtime was not installed.";
        }
        catch (Exception ex)
        {
            return $"Could not install the Microsoft Visual C++ runtime ({architecture}): {ex.Message}";
        }
    }

    internal static void EnsureAvailable(string root, bool is64Bit)
    {
        var missing = MissingArchitectures(root, is64Bit);
        if (missing.Count > 0)
            throw new InvalidOperationException("Install the Microsoft Visual C++ runtime before installing DLSS: "
                + string.Join(", ", missing.Select(architecture => $"{architecture}: {DownloadUrl(architecture)}")));
    }
}
