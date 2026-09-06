using System.IO.Compression;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public sealed partial class Dlss5ComponentService
{
    public const string AioVersion = "2.0.7-experimental.1";
    public const string AioAddon = "standalone-dlssnr.addon64";
    public const string AioShader = "DLSS5_AIO_Feed.fx";
    public const string AioVortBundle = "vort-shaders.zip";
    public const string AioVortBundleSha256 = "1D7127DB1038266314EB84FAFCCC161829C48C5FAF81FC149C1877E0B94CB6C5";
    public const string AioSection = "Standalone.DLSSNR";
    public const string AioReleaseUrl = "https://github.com/kibblerz/DLSS5-Reshade-AIO/releases/tag/v" + AioVersion;

    // Pin the author-published release rather than a mutable latest URL.
    internal static readonly IReadOnlyDictionary<string, string> AioAssetHashes = new Dictionary<string, string>
    {
        // v2.0.7-experimental.1 — adds the adaptive GPU-pressure governor (on by default).
        [AioAddon] = "A1BB1A6056D9849E08D7C91B8B996DF34803CC2B1B89305885890224632F5CC7",
        ["nvngx.dll"] = "21BC631F72614D34387CCF07EEB4DD60EC848FBF67A042A4D8C05C66E0CD5250",
        [AioShader] = "B0EF9EE8F9C7675C0224B87A614905D4283363438BD7E104B132E7200AD84748",
    };
    internal static readonly IReadOnlyDictionary<string, string> AioDefaults = new Dictionary<string, string>
    {
        ["Enabled"] = "1", ["NeuralRendering"] = "1", ["FrameGeneration"] = "0",
        ["EarlyProxyInitialization"] = "0", ["InputColorProfile"] = "0", ["Model"] = "1",
        ["Intensity"] = "1", ["LocalTone"] = "1", ["LocalStructure"] = "1",
        ["SkinStructure"] = "-1", ["ResetEveryFrame"] = "0", ["StableSrHistory"] = "0",
        ["CompositeReshade"] = "1", ["ShowProxyFps"] = "1",
        ["NrRejectionMask"] = "0", ["NrRejectionStrength"] = "1",
        ["DlssRenderPreset"] = "12", ["PerformanceTelemetry"] = "1",
        ["AutoWindowedVirtualization"] = "1", ["SynchronousProxyPresentation"] = "0",
        ["VortGuides"] = "0",
    };
    private static readonly SemaphoreSlim AioCacheLock = new(1, 1);
    private static string AioCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "Adas", "DLSS5", "AIO", AioVersion);

    public static bool SupportsAio(Dlss5DeploymentMode mode, bool is64Bit)
        => is64Bit && mode is Dlss5DeploymentMode.NativeDirectX12 or Dlss5DeploymentMode.NativeDirectX11
            or Dlss5DeploymentMode.Dx11Feeder or Dlss5DeploymentMode.Dx12Feeder
            or Dlss5DeploymentMode.Dx9Feeder or Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.NativeVulkan;

    internal static bool IsAioVulkan(Dlss5DeploymentMode mode)
        => mode is Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.NativeVulkan;

    internal static string AioProxyName(Dlss5DeploymentMode mode)
        => mode == Dlss5DeploymentMode.Dx9Feeder ? "d3d9.dll" : "dxgi.dll";

    internal static void ValidateAioAsset(string path, string name)
    {
        if (!AioAssetHashes.TryGetValue(name, out var expected)
            || !File.Exists(path) || !FileHelper.ComputeSha256(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{name} is missing or is not the verified AIO {AioVersion} release file. Import all three files from the author's release.");
        if (!name.EndsWith(".fx", StringComparison.OrdinalIgnoreCase)
            && !AddonPackService.IsAddonArchitectureCompatible(path, is32Bit: false))
            throw new InvalidDataException($"{name} is not a 64-bit binary.");
    }

    public async Task ImportAioFolderAsync(string source, CancellationToken cancellationToken = default)
    {
        // Validate the complete set before modifying the cache.
        foreach (var name in AioAssetHashes.Keys) ValidateAioAsset(Path.Combine(source, name), name);
        await AioCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(AioCachePath);
            foreach (var name in AioAssetHashes.Keys)
            {
                var destination = Path.Combine(AioCachePath, name);
                if (!Path.GetFullPath(Path.Combine(source, name)).Equals(destination, StringComparison.OrdinalIgnoreCase))
                    CopyAtomically(Path.Combine(source, name), destination);
                ValidateAioAsset(destination, name);
            }
        }
        finally { AioCacheLock.Release(); }
    }

    private async Task EnsureAioAssetsAsync(CancellationToken cancellationToken)
    {
        await AioCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(AioCachePath);
            foreach (var (name, hash) in AioAssetHashes)
            {
                var destination = Path.Combine(AioCachePath, name);
                if (File.Exists(destination) && FileHelper.ComputeSha256(destination).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    continue;
                var source = Path.Combine(GetBundledComponentDirectory(), name);
                try { ValidateAioAsset(source, name); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    throw new FileNotFoundException($"The packaged AIO {AioVersion} file {name} is missing or invalid. Reinstall Adas; no download is required.", ex);
                }
                CopyAtomically(source, destination);
                ValidateAioAsset(destination, name);
            }
        }
        finally { AioCacheLock.Release(); }
    }

    private async Task<Dlss5InstallResult> InstallAioAsync(
        Dlss5Assessment assessment, IProgress<(string message, double percent)>? progress, CancellationToken cancellationToken)
    {
        if (!SupportsAio(assessment.Mode, assessment.Is64Bit))
            throw new InvalidOperationException("Standalone AIO supports only 64-bit DirectX 9, 11, 12 and Vulkan games. Use the recommended setup for this game.");
        var root = Path.GetFullPath(assessment.DeploymentPath!);
        var record = LoadRecord(root);
        if (record != null && record.Profile != Dlss5InstallProfile.StandaloneAio)
            throw new InvalidOperationException("Remove the current DLSS 5 suite with its × button first, then select standalone AIO. Adas will not stack two rendering pipelines.");
        var addonRoot = Path.GetFullPath(ModInstallService.GetAddonDeployPath(root));
        if (!addonRoot.Equals(root, StringComparison.OrdinalIgnoreCase) && !IsPathBelow(root, addonRoot))
            throw new InvalidOperationException("AIO needs a game-local add-on folder. This game uses a shared external AddonPath; change it in ReShade before installing.");
        var vulkan = IsAioVulkan(assessment.Mode);
        if (vulkan && !VulkanLayerService.IsLayerInstalled())
            throw new InvalidOperationException("Install ReShade's 64-bit Vulkan layer using Adas' ReShade installer first. AIO cannot load through a local dxgi.dll in a Vulkan game.");
        ValidateAioConflicts(root, addonRoot, assessment.Mode, record);

        progress?.Report(($"Preparing packaged AIO {AioVersion}…", 8));
        await EnsureAioAssetsAsync(cancellationToken).ConfigureAwait(false);

        var bundle = GetBundledComponentDirectory();
        var reshade = Path.Combine(bundle, "ReShade-6.8.0-64.dll");
        if (!vulkan && (!File.Exists(reshade) || !AddonPackService.IsAddonArchitectureCompatible(reshade, false)))
            throw new FileNotFoundException("The packaged 64-bit ReShade 6.8 runtime is missing.");
        if (!HasBundledReShadeFrameworkHeaders())
            throw new FileNotFoundException("The packaged ReShade framework headers are missing. Reinstall Adas.");

        // Stage only NR/SR/optional FG. Never deploy the unrelated Streamline interposer.
        var runtimeStage = Directory.CreateTempSubdirectory("adas-aio-runtime-").FullName;
        try
        {
            var vortArchive = Path.Combine(bundle, AioVortBundle);
            if (!File.Exists(vortArchive)
                || !FileHelper.ComputeSha256(vortArchive).Equals(AioVortBundleSha256, StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("The packaged VORT motion bundle is missing or invalid. Reinstall Adas; no download is required.");
            var vortStage = Path.Combine(runtimeStage, "vort");
            ZipFile.ExtractToDirectory(vortArchive, vortStage, overwriteFiles: true);
            var vortRoot = Path.Combine(vortStage, "Shaders", "VortShaders");
            var vortTextureRoot = Path.Combine(vortStage, "Textures", "VortShaders");
            if (!File.Exists(Path.Combine(vortRoot, "vort_Motion.fx")))
                throw new InvalidDataException("The packaged VORT motion bundle is incomplete.");
            var runtimeSources = StageAioRuntimes(root, bundle, runtimeStage);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateAioConflicts(root, addonRoot, assessment.Mode, record);
            record ??= new Dlss5InstallRecord();
            record.Mode = assessment.Mode;
            record.Profile = Dlss5InstallProfile.StandaloneAio;
            record.ComponentVersion = $"Standalone AIO {AioVersion}";
            record.InstalledAtUtc = DateTime.UtcNow;
            var installed = new List<string>();
            void Install(string source, string destination)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNoReparsePoints(root, destination);
                InstallTrackedFile(source, destination, root, record);
                installed.Add(destination);
            }

            progress?.Report(("Installing the standalone pipeline and recording rollback backups...", 45));
            if (!vulkan) Install(reshade, Path.Combine(root, AioProxyName(assessment.Mode)));
            Install(Path.Combine(AioCachePath, AioAddon), Path.Combine(addonRoot, AioAddon));
            Install(Path.Combine(AioCachePath, "nvngx.dll"), Path.Combine(addonRoot, "nvngx.dll"));
            Install(Path.Combine(AioCachePath, AioShader), Path.Combine(root, "reshade-shaders", "Shaders", AioShader));
            foreach (var source in runtimeSources) Install(source, Path.Combine(root, Path.GetFileName(source)));
            installed.AddRange(InstallReShadeFrameworkHeaders(bundle, root, record));
            foreach (var tree in new[] { (Source: vortRoot, Kind: "Shaders"), (Source: vortTextureRoot, Kind: "Textures") })
            {
                if (!Directory.Exists(tree.Source)) continue;
                foreach (var source in Dlss5CompatibilityService.EnumerateFilesSafe(tree.Source, maxDepth: 8)
                             .Where(file => tree.Kind != "Shaders" || !file.EndsWith(".fx", StringComparison.OrdinalIgnoreCase)
                                 || Path.GetFileName(file).Equals("vort_Motion.fx", StringComparison.OrdinalIgnoreCase)))
                    Install(source, Path.Combine(root, "reshade-shaders", tree.Kind, "VortShaders", Path.GetRelativePath(tree.Source, source)));
            }
            EnsureAioSettings(root, record);
            EnsureAioPreset(root, record);
            SaveRecord(root, record);
            var issues = Dlss5DiagnosticService.VerifyInstallation(root, assessment.Mode, true);
            if (issues.Count > 0) throw new IOException(string.Join(Environment.NewLine, issues));
            progress?.Report(("Standalone AIO files verified. Restart the game to test the picture.", 100));
            return new(true, assessment.Mode, root, installed, new[]
            {
                "Turn off the game's built-in DLSS, frame generation and antialiasing yourself; Adas does not guess game-specific menu settings.",
                "Native resolution uses DLAA. DLSS upscaling needs a genuinely lower-resolution game backbuffer; try a different display mode if it still says DLAA.",
                "Use ReShade's Standalone DLSS-NR + SR panel. F10 compares processed and original presentation. NR and frame generation are independent; frame generation starts off on a new setup. Preset L is selected for lower smearing.",
                "AIO 2.0 automatically chooses attached or detached presentation and can recover a bad prior session in serialized mode by holding F8 during launch. Experimental stutter, game-specific window behavior and Vulkan menu issues remain possible. File verification is not a picture-quality test.",
            }, $"Standalone AIO {AioVersion} installed. No Feeder or Bridge was added. Restart the game; use the DLSS 5 settings button for simple controls.");
        }
        finally { Directory.Delete(runtimeStage, recursive: true); }
    }

    internal static void ValidateAioConflicts(string root, string addonRoot, Dlss5DeploymentMode mode, Dlss5InstallRecord? record)
    {
        foreach (var directory in new[] { root, addonRoot }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory)) continue;
            var conflict = Directory.EnumerateFiles(directory, "*.addon*").FirstOrDefault(file =>
                IsManagedDlssAddonReference(Path.GetFileName(file)));
            if (conflict != null)
                throw new InvalidOperationException($"Remove the other DLSS pipeline before installing AIO: {Path.GetFileName(conflict)}. Unrelated game-specific RenoDX mods are not removed.");
            var bridgePath = Path.Combine(directory, "nvngx.dll");
            if (File.Exists(bridgePath) && (!(record?.InstalledHashes.TryGetValue(bridgePath, out var expected) ?? false)
                || !FileHelper.ComputeSha256(bridgePath).Equals(expected, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("This game already contains an unowned or changed nvngx.dll. Adas will not replace the game's or another tool's caller DLL.");
        }
        if (!IsAioVulkan(mode))
        {
            var proxy = Path.Combine(root, AioProxyName(mode));
            if (File.Exists(proxy) && !(record?.InstalledHashes.ContainsKey(proxy) ?? false)
                && !string.Equals(System.Diagnostics.FileVersionInfo.GetVersionInfo(proxy).ProductName, "ReShade", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{Path.GetFileName(proxy)} belongs to another wrapper. Remove that wrapper before installing standalone AIO.");
        }
        var ini = IniTextDocument.Load(Path.Combine(root, "ReShade.ini"));
        if (ini.TryGetValue("GENERAL", "PresetPath", out var preset) && !string.IsNullOrWhiteSpace(preset.Text)
            && !IsPathBelow(root, Path.GetFullPath(Path.Combine(root, preset.Text.Trim().Trim('"')))))
            throw new InvalidOperationException("Select a game-local ReShade preset before installing AIO; this preset is shared outside the game folder.");
        foreach (var file in new[] { Path.Combine(addonRoot, AioAddon), Path.Combine(addonRoot, "nvngx.dll"),
                     Path.Combine(root, AioProxyName(mode)), Path.Combine(root, "ReShade.ini") }.Where(File.Exists))
        {
            using var access = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }

    public static void SaveAioUserSettings(string root, IReadOnlyDictionary<string, string> settings)
    {
        var record = LoadRecord(root);
        if (record?.Profile != Dlss5InstallProfile.StandaloneAio)
            throw new InvalidOperationException("This game does not have an Adas-managed AIO setup.");
        foreach (var (key, value) in settings)
        {
            (double Min, double Max, bool Whole) range = key switch
            {
                "Intensity" or "LocalTone" or "LocalStructure" => (0d, 2d, false),
                "SkinStructure" => (-1d, 1d, false),
                "NrRejectionStrength" => (0d, 1d, false),
                "Enabled" or "NeuralRendering" or "FrameGeneration" or "ShowProxyFps" or "EarlyProxyInitialization" or "NrRejectionMask" => (0d, 1d, true),
                _ => throw new ArgumentException($"Unsupported AIO setting: {key}"),
            };
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
                || !double.IsFinite(number) || number < range.Min || number > range.Max || (range.Whole && number != Math.Truncate(number)))
                throw new ArgumentException($"Invalid AIO value for {key}.");
            if (key == "EarlyProxyInitialization" && value == "1"
                && record.Mode is not (Dlss5DeploymentMode.NativeDirectX12 or Dlss5DeploymentMode.Dx12Feeder))
                throw new ArgumentException("Early output initialization is only supported on D3D12.");
            if (key == "FrameGeneration" && value == "1" && !File.Exists(Path.Combine(root, "nvngx_dlssg.dll")))
                throw new FileNotFoundException("Frame generation requires nvngx_dlssg.dll.");
        }
        foreach (var (key, value) in settings)
            SetTrackedIniValue(root, record, Path.Combine(root, "ReShade.ini"), AioSection, key, value);
    }

    private static IReadOnlyList<string> StageAioRuntimes(string root, string bundle, string stage)
    {
        var result = new List<string>();
        using var archive = ZipFile.OpenRead(Path.Combine(bundle, "streamline.zip"));
        foreach (var name in new[] { "nvngx_dlssnr.dll", "nvngx_dlss.dll", "nvngx_dlssg.dll" })
        {
            var existing = Path.Combine(root, name);
            if (File.Exists(existing))
            {
                if (!AddonPackService.IsAddonArchitectureCompatible(existing, false))
                    throw new InvalidDataException($"{name} has the wrong architecture. Adas has preserved it; resolve it before installing AIO.");
                continue;
            }
            var entry = archive.Entries.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry == null && name == "nvngx_dlssg.dll") continue;
            if (entry == null || entry.Length > MaxDownloadBytes)
                throw new FileNotFoundException($"The packaged runtime is missing or invalid: {name}");
            var destination = Path.Combine(stage, name);
            entry.ExtractToFile(destination);
            if (!AddonPackService.IsAddonArchitectureCompatible(destination, false))
                throw new InvalidDataException($"The packaged {name} is not a 64-bit binary.");
            result.Add(destination);
        }
        return result;
    }

    internal static void EnsureAioSettings(string root, Dlss5InstallRecord record)
    {
        var path = Path.Combine(root, "ReShade.ini");
        var ini = IniTextDocument.Load(path);
        foreach (var (key, value) in AioDefaults)
            if (!ini.TryGetValue(AioSection, key, out _)) SetTrackedIniValue(root, record, path, AioSection, key, value);
        foreach (var (key, fallback) in new[] { ("EffectSearchPaths", @".\reshade-shaders\Shaders\**"), ("TextureSearchPaths", @".\reshade-shaders\Textures\**") })
        {
            ini.TryGetValue("GENERAL", key, out var current);
            SetTrackedIniValue(root, record, path, "GENERAL", key, NormalizeReShadeSearchPaths(current.Text, fallback));
        }
        SetTrackedIniValue(root, record, path, "GENERAL", "SkipLoadingDisabledEffects", "0");
        SetTrackedIniValue(root, record, path, "GENERAL", "NoReloadOnInit", "0");
        SetTrackedIniValue(root, record, path, "GENERAL", "StartupPresetPath", "");
        // AIO schedules these shaders itself, even when unchecked in the ordinary preset.
        if (ini.TryGetValue("ADDON", "DisabledAddons", out var disabled))
            SetTrackedIniValue(root, record, path, "ADDON", "DisabledAddons", string.Join(',', SplitIniList(disabled.Text)
                .Where(value => !GetAddonReferenceFileName(value).Equals(AioAddon, StringComparison.OrdinalIgnoreCase)
                    && !value.StartsWith("Standalone DLSS-NR", StringComparison.OrdinalIgnoreCase))));
    }

    internal static string RemoveAioScheduledTechniques(string techniques)
        => string.Join(',', SplitIniList(techniques).Where(value =>
            !value.Split('@')[0].Equals("vort_MotionEffects", StringComparison.OrdinalIgnoreCase)
            && !value.Split('@')[0].Equals("DLSS5_AIO_Feed", StringComparison.OrdinalIgnoreCase)));

    private static void EnsureAioPreset(string root, Dlss5InstallRecord record)
    {
        var iniPath = Path.Combine(root, "ReShade.ini");
        var ini = IniTextDocument.Load(iniPath);
        var presetPath = Path.Combine(root, "ReShadePreset.ini");
        if (ini.TryGetValue("GENERAL", "PresetPath", out var configured) && !string.IsNullOrWhiteSpace(configured.Text))
        {
            var candidate = Path.GetFullPath(Path.Combine(root, configured.Text.Trim().Trim('"')));
            if (IsPathBelow(root, candidate)) presetPath = candidate;
            else throw new InvalidOperationException("The selected ReShade preset is shared outside this game. Select a game-local preset before installing AIO.");
        }
        SetTrackedIniValue(root, record, iniPath, "GENERAL", "PresetPath", presetPath);
        var preset = IniTextDocument.Load(presetPath);
        preset.TryGetValue("", "Techniques", out var techniques);
        preset.SetValue("", "Techniques", RemoveAioScheduledTechniques(techniques.Text));
        preset.TryGetValue("vort_Motion.fx", "PreprocessorDefinitions", out var definitions);
        var required = new[] { "V_MV_MODE=1", "V_ENABLE_MOT_BLUR=0", "V_ENABLE_TAA=0", "V_MV_DEBUG=0" };
        var retained = SplitIniList(definitions.Text).Where(value => !required.Any(item =>
            item.Split('=')[0].Equals(value.Split('=')[0], StringComparison.OrdinalIgnoreCase)));
        preset.SetValue("vort_Motion.fx", "PreprocessorDefinitions", string.Join(',', retained.Concat(required)));
        var temporary = Path.Combine(Path.GetTempPath(), $"adas-aio-preset-{Guid.NewGuid():N}.ini");
        try
        {
            preset.Save(temporary);
            InstallTrackedFile(temporary, presetPath, root, record);
        }
        finally { DeleteIfExists(temporary); }
    }
}
