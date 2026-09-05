using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public sealed partial class Dlss5ComponentService
{
    internal const string OptiScalerNrVersion = "0.2.0";
    internal const string OptiScalerSplitVersion = "0.1.2 NR-before-SR English";
    private static readonly SemaphoreSlim OptiScalerNrCacheLock = new(1, 1);

    internal static bool IsOptiScalerNrProfile(Dlss5InstallProfile? profile)
        => profile is Dlss5InstallProfile.OptiScalerNeuralRendering or Dlss5InstallProfile.OptiScalerNrBeforeSr;

    internal static bool SupportsOptiScalerNr(Dlss5DeploymentMode mode, bool is64Bit, bool split)
        => is64Bit && (mode == Dlss5DeploymentMode.NativeDirectX12
            || (!split && mode is Dlss5DeploymentMode.NativeDirectX11 or Dlss5DeploymentMode.NativeVulkan));

    internal static bool ShouldWriteOptiScalerSplitKeys(Dlss5InstallProfile profile)
        => profile == Dlss5InstallProfile.OptiScalerNrBeforeSr;

    internal static string OptiScalerNrProxy(Dlss5DeploymentMode mode)
        => mode == Dlss5DeploymentMode.NativeVulkan ? "winmm.dll" : "dxgi.dll";

    internal static bool RequiresPipelineRemoval(Dlss5InstallProfile? installed, Dlss5InstallProfile selected)
        => installed.HasValue && installed != selected
            && (IsOptiScalerNrProfile(installed) || IsOptiScalerNrProfile(selected)
                || installed == Dlss5InstallProfile.StandaloneAio || selected == Dlss5InstallProfile.StandaloneAio);

    internal static string? OptiScalerNrDestination(string relative)
    {
        relative = relative.Replace('\\', '/');
        if (relative.StartsWith('/') || relative.Contains(':') || relative.Split('/').Any(part => part is ".." or "."))
            throw new InvalidDataException("Invalid OptiScaler package path.");
        if (relative.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase)) return "dxgi.dll";
        if (relative.Equals("nvngx.dll_dlssnr.dll", StringComparison.OrdinalIgnoreCase)
            || relative.Equals("OptiScaler.ini", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("OptiScaler/", StringComparison.OrdinalIgnoreCase)
                && relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("Licenses/", StringComparison.OrdinalIgnoreCase)
                && Path.GetExtension(relative).ToLowerInvariant() is ".md" or ".txt")
            return relative.Replace('/', Path.DirectorySeparatorChar);
        return null; // Never run or install upstream setup scripts.
    }

    internal static void ValidateOptiScalerNrConflicts(string root, Dlss5InstallRecord? record,
        Dlss5DeploymentMode mode = Dlss5DeploymentMode.NativeDirectX12)
    {
        foreach (var directory in new[] { root, ModInstallService.GetAddonDeployPath(root) }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory)) continue;
            var conflict = Directory.EnumerateFiles(directory).FirstOrDefault(file =>
                Path.GetFileName(file).StartsWith("renodx-dlss", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).StartsWith("dlss5-feed", StringComparison.OrdinalIgnoreCase) && file.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).StartsWith("dlss5-bridge", StringComparison.OrdinalIgnoreCase) && file.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals(ObsoleteBridgeAddon, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals(AioAddon, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("deep-fried-chicken.addon64", StringComparison.OrdinalIgnoreCase));
            if (conflict != null) throw new InvalidOperationException($"Remove the other neural rendering pipeline first: {Path.GetFileName(conflict)}.");
        }
        var proxy = Path.Combine(root, OptiScalerNrProxy(mode));
        if (File.Exists(proxy) && !(record?.InstalledHashes.ContainsKey(proxy) ?? false)
            && !string.Equals(System.Diagnostics.FileVersionInfo.GetVersionInfo(proxy).ProductName, "ReShade", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{Path.GetFileName(proxy)} belongs to another wrapper. Remove it through its installer first; Adas has not overwritten it.");
    }

    private async Task<Dlss5InstallResult> InstallOptiScalerNrAsync(Dlss5Assessment assessment,
        Dlss5InstallProfile profile, IProgress<(string message, double percent)>? progress, CancellationToken cancellationToken)
    {
        var split = profile == Dlss5InstallProfile.OptiScalerNrBeforeSr;
        if (!SupportsOptiScalerNr(assessment.Mode, assessment.Is64Bit, split))
            throw new InvalidOperationException("This OptiScaler NR route requires a 64-bit native-DLSS game: DX11, DX12, or Vulkan for the standard NR fork. Use Feeder or AIO for other games.");
        var root = Path.GetFullPath(assessment.DeploymentPath!);
        var record = LoadRecord(root);
        ValidateOptiScalerNrConflicts(root, record, assessment.Mode);
        var proxyName = OptiScalerNrProxy(assessment.Mode);
        var version = split ? OptiScalerSplitVersion : OptiScalerNrVersion;
        var name = split ? "optiscaler-split.zip" : "optiscaler-nr.zip";
        var expectedHash = split ? "38BB8DDA6EF288FA3546DBF294886E9223DB767F36D7FB933F71C0A1E4CF4449"
            : "8EECE7A4D7DE6DE5917F0C99AC60540B2D77022E7699BBA717B0A6D9E1829BCE";
        var url = split
            ? "https://github.com/Markxiao94/OptiScaler-DLSSNR-NR-before-SR/releases/download/v0.1.2-nr-before-sr-english/OptiScaler-NR-before-SR-English-x64-20260903.zip"
            : "https://github.com/Dagherbou/OptiScaler_DLSSNR/releases/download/v0.2.0-dlssnr/OptiScaler-DLSSNR-v0.2.0.zip";
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "Adas", "DLSS5", "OptiScalerNR", expectedHash);
        var archive = Path.Combine(cache, name);
        await OptiScalerNrCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(cache);
            if (!File.Exists(archive) || !FileHelper.ComputeSha256(archive).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(($"Preparing experimental OptiScaler NR {version}...", 10));
                var bundled = Path.Combine(GetBundledComponentDirectory(), name);
                var pending = archive + ".pending";
                try
                {
                    if (File.Exists(bundled)) File.Copy(bundled, pending, overwrite: true);
                    else await DownloadFileAsync(url, pending, cancellationToken).ConfigureAwait(false);
                    if (!FileHelper.ComputeSha256(pending).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The OptiScaler NR package does not match the reviewed release hash.");
                    File.Move(pending, archive, overwrite: true);
                }
                finally { DeleteIfExists(pending); }
            }
        }
        finally { OptiScalerNrCacheLock.Release(); }

        var stage = Directory.CreateTempSubdirectory("adas-optiscaler-nr-").FullName;
        try
        {
            ExtractArchiveSafely(archive, stage, maxEntryBytes: 128L * 1024 * 1024, packageLabel: "OptiScaler NR");
            var files = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories)
                .Select(source => (Source: source, Relative: OptiScalerNrDestination(Path.GetRelativePath(stage, source))))
                .Where(file => file.Relative != null).ToList();
            files = files.Select(file => (file.Source, Relative: file.Relative == "dxgi.dll" ? proxyName : file.Relative)).ToList();
            foreach (var required in new[] { proxyName, "nvngx.dll_dlssnr.dll", "OptiScaler.ini" })
                if (!files.Any(file => file.Relative == required)) throw new InvalidDataException($"OptiScaler NR package missing {required}.");
            var runtimeStage = Directory.CreateDirectory(Path.Combine(stage, "runtimes")).FullName;
            foreach (var source in StageAioRuntimes(root, GetBundledComponentDirectory(), runtimeStage))
                if (!Path.GetFileName(source).Equals("nvngx_dlssg.dll", StringComparison.OrdinalIgnoreCase))
                    files.Add((source, Path.GetFileName(source)));
            foreach (var file in files)
            {
                if (file.Relative!.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && !AddonPackService.IsAddonArchitectureCompatible(file.Source, false))
                    throw new InvalidDataException($"Not a 64-bit library: {file.Relative}");
                var destination = Path.Combine(root, file.Relative);
                EnsureNoReparsePoints(root, destination);
                if (!File.Exists(destination)) continue;
                // Only ReShade's loader is explicitly replaced (and backed up). Other unowned files are untouched.
                if (!(record?.InstalledHashes.ContainsKey(destination) ?? false) && file.Relative != proxyName)
                    throw new InvalidOperationException($"Existing file is not owned by this suite: {file.Relative}. Remove the previous OptiScaler setup first.");
                using var access = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            cancellationToken.ThrowIfCancellationRequested();
            record ??= new Dlss5InstallRecord();
            record.Profile = profile;
            record.Mode = assessment.Mode;
            record.ComponentVersion = $"OptiScaler NR {version}";
            record.InstalledAtUtc = DateTime.UtcNow;
            foreach (var file in files)
            {
                var destination = Path.Combine(root, file.Relative!);
                if (file.Relative == "OptiScaler.ini" && File.Exists(destination)) continue;
                InstallTrackedFile(file.Source, destination, root, record);
            }
            var iniPath = Path.Combine(root, "OptiScaler.ini");
            var ini = IniTextDocument.Load(iniPath);
            // Preserve existing tuning on Repair while migrating route-critical defaults.
            ConfigureOptiScalerNrIni(ini, assessment.Mode, profile);
            ini.Save(iniPath);
            record.InstalledHashes[iniPath] = FileHelper.ComputeSha256(iniPath);
            SaveRecord(root, record);
            var issues = Dlss5DiagnosticService.VerifyInstallation(root, assessment.Mode, true);
            if (issues.Count > 0)
                throw new InvalidDataException("OptiScaler NR installation needs repair: " + string.Join("; ", issues));
            progress?.Report(("OptiScaler NR files installed. Restart and verify with Insert.", 100));
            return new(true, assessment.Mode, root, record.InstalledHashes.Keys.ToArray(), new[]
            {
                "Experimental: enable the game's own DLSS, press Insert, and verify Neural Rendering in OptiScaler. These controls are not in ReShade.",
                "OptiScaler 0.2 exposes hybrid color composition, live exposure, frame hold, and model supersampling with selectable downscalers. The split fork separately exposes internal SR presets and optional RR supersampling; RR requires real game ray-reconstruction inputs.",
                "Driver 616.56 or newer is required by upstream. File installation is not a GPU compatibility or image-quality test.",
            }, $"OptiScaler NR {version} installed as {proxyName}. Any replaced ReShade loader is backed up for removal; no Feeder, Bridge or RenoDX DLSS pipeline was added.");
        }
        finally { Directory.Delete(stage, recursive: true); }
    }

    internal static void ConfigureOptiScalerNrIni(
        IniTextDocument ini,
        Dlss5DeploymentMode mode,
        Dlss5InstallProfile profile)
    {
        static void SetIfAutomatic(IniTextDocument document, string section, string key, string value)
        {
            if (!document.TryGetValue(section, key, out var current)
                || current.Text.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
                document.SetValue(section, key, value);
        }

        SetIfAutomatic(ini, "DlssNr", "Enabled", "true");
        SetIfAutomatic(ini, "DlssNr", "AutoCapture", "false");
        SetIfAutomatic(ini, "Plugins", "LoadReshade", "false");
        if (mode == Dlss5DeploymentMode.NativeDirectX11)
            SetIfAutomatic(ini, "Upscalers", "Dx11Upscaler", "dlss_12");

        if (ShouldWriteOptiScalerSplitKeys(profile))
        {
            SetIfAutomatic(ini, "DlssNr", "SplitPipeline", "true");
            SetIfAutomatic(ini, "DlssNr", "SplitIncludeRR", "false");
        }
        else
        {
            // These keys belong to the separate NR-before-SR fork and are not
            // part of the 0.2 standard release.
            ini.RemoveValue("DlssNr", "SplitPipeline");
            ini.RemoveValue("DlssNr", "SplitIncludeRR");
        }
    }
}
