using System.IO.Compression;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public sealed partial class Dlss5ComponentService
{
    public const string AioVersion = "2.2.4";
    public const string AioAddon = "standalone-dlssnr.addon64";
    public const string AioShader = "DLSS5_AIO_Feed.fx";
    public const string AioVortBundle = "vort-shaders.zip";
    public const string AioVortBundleSha256 = "1D7127DB1038266314EB84FAFCCC161829C48C5FAF81FC149C1877E0B94CB6C5";
    public const string AioSection = "Standalone.DLSSNR";
    public const string AioReleaseUrl = "https://github.com/kibblerz/DLSS5-Reshade-AIO/releases/tag/v" + AioVersion;

    // Pin the author-published release rather than a mutable latest URL.
    internal static readonly IReadOnlyDictionary<string, string> AioAssetHashes = new Dictionary<string, string>
    {
        // v2.2.4 — maintenance release over v2.2.3. The add-on and the AIO caller-bridge
        // nvngx.dll changed again; the feed shader (DLSS5_AIO_Feed.fx) is still byte-identical
        // to v2.2.1. Verified against the author's DLSS5-ReShade-AIO-v2.2.4-64-bit.zip
        // (SHA-256 38493d801646fce46c6caa5f0d9869c5079d09247c94368e163a7c5fb8fd38ee, which matches
        // the author's published DLSS5-ReShade-AIO-v2.2.4-SHA256.txt).
        // The redundant StandaloneBoundary.fx Vulkan fallback is still not bundled.
        [AioAddon] = "174C30913E3A974CC701340E65F2E16ECA9569423A3DDBC482B68682F9D302BC",
        ["nvngx.dll"] = "540247E4AE8C68BCF4ED3B70393D8904A522C75CD46A3B801BFBD8800EF106E4",
        [AioShader] = "0710E17EEAFA1933AF18489BFB7D7A1D71BD6204D65D337474F835749FD6DE58",
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
    /// <summary>The author's 32-bit AIO package (32-bit add-on, x86 feed shaders and the host64 carrier), SHA-pinned.</summary>
    public const string AioX86Bundle = "aio-x86-2.2.4.zip";
    public const string AioX86BundleSha256 = "4FC48CCD5E6CA144B20D47FD049CFBC9B888D4F0FB7E87847A6D72A967491FED";
    public const string AioX86Addon = "standalone-dlssnr.addon32";
    public const string AioX86Config = "dlss5-aio-x86.cfg";
    public const string AioHostFolder = "host64";
    private static readonly SemaphoreSlim AioCacheLock = new(1, 1);
    private static string AioCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "Adas", "DLSS5", "AIO", AioVersion);

    public static bool SupportsAio(Dlss5DeploymentMode mode, bool is64Bit)
        => is64Bit
            ? mode is Dlss5DeploymentMode.NativeDirectX12 or Dlss5DeploymentMode.NativeDirectX11
                or Dlss5DeploymentMode.Dx11Feeder or Dlss5DeploymentMode.Dx12Feeder
                or Dlss5DeploymentMode.Dx9Feeder or Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.NativeVulkan
            // The author's x86 package supports native 32-bit D3D9 and D3D11 only.
            : mode is Dlss5DeploymentMode.Dx9Feeder or Dlss5DeploymentMode.Dx11Feeder or Dlss5DeploymentMode.NativeDirectX11;

    /// <summary>True when the AIO install in <paramref name="root"/> is the 32-bit (host64 carrier) layout.</summary>
    public static bool IsAioX86Install(string root) => File.Exists(Path.Combine(root, AioX86Addon));

    /// <summary>The ReShade.ini that holds [Standalone.DLSSNR]: the host64 carrier's for 32-bit games.</summary>
    public static string AioSettingsIniPath(string root)
        => IsAioX86Install(root) ? Path.Combine(root, AioHostFolder, "ReShade.ini") : Path.Combine(root, "ReShade.ini");

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
            throw new InvalidOperationException(assessment.Is64Bit
                ? "Standalone AIO supports 64-bit DirectX 9, 11, 12 and Vulkan games. Use the recommended setup for this game."
                : "Standalone AIO supports 32-bit games only on DirectX 9 and DirectX 11. Use the recommended setup for this game.");
        if (!assessment.Is64Bit)
            return await InstallAioX86Async(assessment, progress, cancellationToken).ConfigureAwait(false);
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

    private async Task<Dlss5InstallResult> InstallAioX86Async(
        Dlss5Assessment assessment, IProgress<(string message, double percent)>? progress, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(assessment.DeploymentPath!);
        var record = LoadRecord(root);
        if (record != null && record.Profile != Dlss5InstallProfile.StandaloneAio)
            throw new InvalidOperationException("Remove the current DLSS 5 suite with its × button first, then select standalone AIO. Adas will not stack two rendering pipelines.");
        var host = Path.Combine(root, AioHostFolder);
        ValidateAioConflicts(root, root, assessment.Mode, record);
        ValidateAioX86Conflicts(root, assessment.Mode, record);
        await Task.Yield();

        progress?.Report(($"Preparing packaged 32-bit AIO {AioVersion}…", 8));
        var bundle = GetBundledComponentDirectory();
        var package = Path.Combine(bundle, AioX86Bundle);
        if (!File.Exists(package) || !FileHelper.ComputeSha256(package).Equals(AioX86BundleSha256, StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("The packaged 32-bit AIO release is missing or invalid. Reinstall Adas; no download is required.");
        var reshade32 = Path.Combine(bundle, "ReShade-6.8.0-32.dll");
        var reshade64 = Path.Combine(bundle, "ReShade-6.8.0-64.dll");
        if (!File.Exists(reshade32) || !AddonPackService.IsAddonArchitectureCompatible(reshade32, true))
            throw new FileNotFoundException("The packaged 32-bit ReShade 6.8 runtime is missing.");
        if (!File.Exists(reshade64) || !AddonPackService.IsAddonArchitectureCompatible(reshade64, false))
            throw new FileNotFoundException("The packaged 64-bit ReShade 6.8 runtime is missing.");
        if (!HasBundledReShadeFrameworkHeaders())
            throw new FileNotFoundException("The packaged ReShade framework headers are missing. Reinstall Adas.");

        var stage = Directory.CreateTempSubdirectory("adas-aio-x86-").FullName;
        try
        {
            ZipFile.ExtractToDirectory(package, stage, overwriteFiles: true);
            if (!File.Exists(Path.Combine(stage, AioX86Addon)) || !File.Exists(Path.Combine(stage, AioHostFolder, AioAddon)))
                throw new InvalidDataException("The packaged 32-bit AIO release is incomplete.");
            Directory.CreateDirectory(host);
            var runtimeStage = Path.Combine(stage, "runtimes");
            Directory.CreateDirectory(runtimeStage);
            var runtimeSources = StageAioRuntimes(host, bundle, runtimeStage);
            cancellationToken.ThrowIfCancellationRequested();

            record ??= new Dlss5InstallRecord();
            record.Mode = assessment.Mode;
            record.Profile = Dlss5InstallProfile.StandaloneAio;
            record.ComponentVersion = $"Standalone AIO {AioVersion} (32-bit)";
            record.InstalledAtUtc = DateTime.UtcNow;
            var installed = new List<string>();
            void Install(string source, string destination)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNoReparsePoints(root, destination);
                InstallTrackedFile(source, destination, root, record);
                installed.Add(destination);
            }

            progress?.Report(("Installing the 32-bit add-on and the 64-bit AIO carrier...", 45));
            foreach (var source in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(stage, source);
                var top = relative.Split(Path.DirectorySeparatorChar)[0];
                if (top.Equals("licenses", StringComparison.OrdinalIgnoreCase) || top.Equals("runtimes", StringComparison.OrdinalIgnoreCase)
                    || source.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    continue;
                var destination = Path.Combine(root, relative);
                // Settings files are seeded once; a repair keeps what the user changed.
                if ((destination.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) || destination.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
                    && File.Exists(destination) && record.InstalledHashes.ContainsKey(destination))
                    continue;
                Install(source, destination);
            }
            Install(reshade32, Path.Combine(root, AioProxyName(assessment.Mode)));
            Install(reshade64, Path.Combine(host, "dxgi.dll"));
            foreach (var source in runtimeSources) Install(source, Path.Combine(host, Path.GetFileName(source)));
            installed.AddRange(InstallReShadeFrameworkHeaders(bundle, root, record));
            installed.AddRange(InstallReShadeFrameworkHeaders(bundle, host, record));
            EnsureAioSettings(root, record, host);
            SaveRecord(root, record);
            var issues = Dlss5DiagnosticService.VerifyInstallation(root, assessment.Mode, false);
            if (issues.Count > 0) throw new IOException(string.Join(Environment.NewLine, issues));
            progress?.Report(("32-bit AIO files verified. Restart the game to test the picture.", 100));
            return new(true, assessment.Mode, root, installed, new[]
            {
                "Turn off the game's own antialiasing and upscaling; Adas does not guess game-specific menu settings.",
                "The 32-bit add-on starts the 64-bit AIO carrier (host64) automatically. Keep every file in host64 where Adas put it.",
                "Use the AIO page in the game's ReShade Add-ons tab. Applying settings restarts only the 64-bit carrier.",
                assessment.Mode == Dlss5DeploymentMode.Dx9Feeder
                    ? "Older D3D9 games that stay on Ready: enable \"Allow classic D3D9 CPU bridge\" in the AIO troubleshooting options (Adas can switch it for you after Verify)."
                    : "32-bit OpenGL and Vulkan are not supported by this package.",
            }, $"Standalone AIO {AioVersion} (32-bit) installed with its 64-bit carrier. Restart the game.");
        }
        finally { Directory.Delete(stage, recursive: true); }
    }

    /// <summary>
    /// 32-bit layout checks: a D3D9 game must not also have a local dxgi.dll proxy (the bridge needs the real DXGI),
    /// and the host64 carrier's ReShade must not belong to another tool.
    /// </summary>
    internal static void ValidateAioX86Conflicts(string root, Dlss5DeploymentMode mode, Dlss5InstallRecord? record)
    {
        if (mode == Dlss5DeploymentMode.Dx9Feeder)
        {
            var dxgi = Path.Combine(root, "dxgi.dll");
            if (File.Exists(dxgi) && !(record?.InstalledHashes.ContainsKey(dxgi) ?? false))
                throw new InvalidOperationException("This D3D9 game has a dxgi.dll beside it. The 32-bit AIO bridge needs Windows' real DXGI; remove that wrapper first.");
        }
        var hostProxy = Path.Combine(root, AioHostFolder, "dxgi.dll");
        if (File.Exists(hostProxy) && !(record?.InstalledHashes.ContainsKey(hostProxy) ?? false)
            && !string.Equals(System.Diagnostics.FileVersionInfo.GetVersionInfo(hostProxy).ProductName, "ReShade", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("host64\\dxgi.dll belongs to another tool. Remove it before installing 32-bit AIO.");
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
                "Enabled" or "NeuralRendering" or "FrameGeneration" or "ShowProxyFps" or "EarlyProxyInitialization" or "NrRejectionMask"
                    or "SynchronousProxyPresentation" or "DpiPhysicalOutputCorrection" or "WindowedVirtualization"
                    or "WindowedInputScaling" or "HideDetachedSystemCursor" or "DetachedPresentation" or "OpaqueComposition"
                    or "SuppressQueuePressureWarning" or "AutoWindowedVirtualization" => (0d, 1d, true),
                "DlssRenderPreset" => (11d, 13d, true),
                _ => throw new ArgumentException($"Unsupported AIO setting: {key}"),
            };
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
                || !double.IsFinite(number) || number < range.Min || number > range.Max || (range.Whole && number != Math.Truncate(number)))
                throw new ArgumentException($"Invalid AIO value for {key}.");
            if (key == "EarlyProxyInitialization" && value == "1"
                && record.Mode is not (Dlss5DeploymentMode.NativeDirectX12 or Dlss5DeploymentMode.Dx12Feeder))
                throw new ArgumentException("Early output initialization is only supported on D3D12.");
            if (key == "FrameGeneration" && value == "1" && !File.Exists(Path.Combine(Path.GetDirectoryName(AioSettingsIniPath(root))!, "nvngx_dlssg.dll")))
                throw new FileNotFoundException("Frame generation requires nvngx_dlssg.dll.");
        }
        foreach (var (key, value) in settings)
            SetTrackedIniValue(root, record, AioSettingsIniPath(root), AioSection, key, value);
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

    internal static void EnsureAioSettings(string root, Dlss5InstallRecord record, string? iniDirectory = null)
    {
        var path = Path.Combine(iniDirectory ?? root, "ReShade.ini");
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
