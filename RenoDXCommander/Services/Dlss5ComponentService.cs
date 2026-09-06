using System.IO.Compression;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Installs and manages the complete packaged DLSS 5 runtime set. Every write is
/// recorded for hash-aware uninstall and rollback.
/// </summary>
public sealed partial class Dlss5ComponentService
{
    internal sealed record ReShadeInstallPolicy(
        bool BlockInstall,
        string? ProxyName,
        bool PreserveSuiteShaders,
        string Reason);

    private readonly record struct DeploymentPathCacheEntry(string? Path, long ExpiresUtcTicks);
    private static readonly ConcurrentDictionary<string, DeploymentPathCacheEntry> DeploymentPathCache =
        new(StringComparer.OrdinalIgnoreCase);

    public const string FeederAddon = "dlss5-feed.addon64";
    public const string FeederAddon32 = "dlss5-feed.addon32";
    public const string FeederHost64 = "dlss5-feed-host64.exe";
    public const string FeederShader = "DLSS5_Feed.fx";
    public const string FeederVulkanLayer = "feed-vk-layer.zip";
    public const string FeederConfig = "dlss5-feed.cfg";
    public const string FeederLog = "dlss5-feed.log";
    public const string BridgeAddon = "dlss5-bridge.addon64";
    public const string BridgeConfig = "dlss5-bridge.cfg";
    public const string BridgeLog = "dlss5-bridge.log";
    public const string OpenGlBridgeAddon = "dlss5-opengl-bridge.addon64";

    private const string ObsoleteBridgeAddon = "dlss5-dx11-bridge.addon64";
    private const string ObsoleteBridgeConfig = "dlss5-dx11-bridge.cfg";
    private const string ObsoleteBridgeLog = "dlss5-dx11-bridge.log";

    private const string FeederRepo = "jlrouzies-fr/DLSS5-Feeder";
    private const string BundledRenoDxVersion = "SF-2026-09-02";
    private const string RenoDxDeploymentName = "renodx-dlss5.addon64";
    private const string NativeRenoDxAsset = "renodx-dlss5-4.70.addon64";
    private const string FeederRenoDxAsset = "renodx-dlss5-4.55.addon64";
    private const string NativeRenoDxVersion = "stable-v4.70";
    private const string FeederRenoDxVersion = "feeder-pinned-v4.55";
    internal const string BridgeVersion = "v1.4.11";
    private const string BundledFeederVersion = "0.7.0";
    internal const string BundledFeederBetaVersion = "0.14.0-beta.2";
    internal const string OpenGlBridgeVersion = "1.0.5";
    internal const string OneClickVersion = "0.11.15";
    private const string BundledStableReShadeVersion = "6.8.0";
    private const string BundledLegacyReShadeVersion = "6.3.3";
    private const string DgVoodooRepo = "dege-diosg/dgVoodoo2";
    private const string LumeniteArchiveUrl = "https://codeload.github.com/umar-afzaal/LumeniteFX/zip/refs/heads/mainline";
    private const string RecordRelativePath = ".adas/dlss5-install.json";
    private const long MaxDownloadBytes = 256L * 1024 * 1024;
    private const long MaxArchiveEntryBytes = 64L * 1024 * 1024;
    private const long MaxExtractedBytes = 512L * 1024 * 1024;
    private const int MaxArchiveEntries = 10_000;

    private static readonly string[] ReShadeFrameworkHeaders = { "ReShade.fxh", "ReShadeUI.fxh", "DrawText.fxh" };
    private static readonly HashSet<string> NativeGameRuntimeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "nvngx_dlss.dll", "nvngx_dlssg.dll",
        "sl.common.dll", "sl.dlss.dll", "sl.dlss_g.dll", "sl.interposer.dll",
        "sl.nis.dll", "sl.pcl.dll", "sl.reflex.dll",
    };
    private static readonly string[] HostedRuntimeNames =
    {
        "nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssnr.dll",
        "sl.common.dll", "sl.dlss.dll", "sl.dlss_g.dll", "sl.dlss_nr.dll",
        "sl.interposer.dll", "sl.nis.dll", "sl.pcl.dll", "sl.reflex.dll",
    };

    internal static Dlss5CompatibilityPlan GetCompatibilityPlan(
        Dlss5DeploymentMode mode,
        bool is64Bit,
        Dlss5InstallProfile profile = Dlss5InstallProfile.MaximumQuality)
    {
        if (profile == Dlss5InstallProfile.OpenGlBridge && SupportsOpenGlBridge(mode, is64Bit))
        {
            return new(
                Dlss5RenoDxPackage.Native470,
                InstallFeeder: false,
                InstallDx11Bridge: false,
                PatchFeederForUnifiedName: false,
                ProfileName: $"OpenGL Bridge {OpenGlBridgeVersion} + RenoDX v4.70")
            {
                InstallOpenGlBridge = true,
            };
        }

        if ((profile == Dlss5InstallProfile.LatestFeederBeta || mode == Dlss5DeploymentMode.Dx10Feeder)
            && IsFeederMode(mode))
        {
            // Feeder routes run the RenoDX consumer in the 64-bit host and must use the feeder-pinned
            // v4.55 build. The native v4.70 consumer faults inside the driver's NGX runtime
            // (access violation in D3D12Core.dll via nvngx_dlssnr.dll) on driver 616.64, producing a
            // black screen while everything else works; v4.55 is the measured-good build.
            return new(
                Dlss5RenoDxPackage.Feeder455,
                InstallFeeder: true,
                InstallDx11Bridge: false,
                PatchFeederForUnifiedName: false,
                ProfileName: $"Latest Feeder beta {BundledFeederBetaVersion} + RenoDX v4.55")
            {
                UsesLatestFeederBeta = true,
            };
        }

        if (profile == Dlss5InstallProfile.ExperimentalUnified
            && is64Bit
            && mode != Dlss5DeploymentMode.NativeVulkan)
        {
            return new(
                Dlss5RenoDxPackage.ExperimentalUnified,
                InstallFeeder: false,
                InstallDx11Bridge: false,
                PatchFeederForUnifiedName: false,
                ProfileName: "ShortFuse unified (direct, no Feeder)");
        }

        return mode switch
        {
            Dlss5DeploymentMode.NativeDirectX12 => new(
                Dlss5RenoDxPackage.Native470, false, false, false, "Maximum Quality — RenoDX v4.70 native D3D12"),
            Dlss5DeploymentMode.NativeDirectX11 when is64Bit => new(
                Dlss5RenoDxPackage.Native470, false, true, false, $"Maximum Quality — RenoDX v4.70 + Bridge {BridgeVersion}"),
            Dlss5DeploymentMode.NativeVulkan when is64Bit => new(
                Dlss5RenoDxPackage.Native470, false, true, false, $"Maximum Quality — RenoDX v4.70 + Bridge {BridgeVersion} Vulkan mirror"),
            Dlss5DeploymentMode.Dx11Feeder or Dlss5DeploymentMode.Dx12Feeder
                or Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.Dx9Feeder or Dlss5DeploymentMode.Dx8Feeder
                or Dlss5DeploymentMode.OpenGlFeeder or Dlss5DeploymentMode.Dx10ViaDxvkFeeder
                or Dlss5DeploymentMode.Dx10Feeder or Dlss5DeploymentMode.Dx9ViaDxvkFeeder => new(
                    Dlss5RenoDxPackage.Feeder455, true, false, false, "Maximum Quality — Feeder-pinned RenoDX v4.55"),
            _ => new(Dlss5RenoDxPackage.ExperimentalUnified, false, false, false, "Experimental fallback"),
        };
    }

    internal static bool IsComponentUpdateAvailable(Dlss5InstallRecord? record)
    {
        if (record == null) return false;
        var version = record.ComponentVersion ?? "";
        return record.Profile switch
        {
            Dlss5InstallProfile.StandaloneAio => !version.Contains($"AIO {AioVersion}", StringComparison.OrdinalIgnoreCase),
            Dlss5InstallProfile.OptiScalerNeuralRendering => !version.Contains($"NR {OptiScalerNrVersion}", StringComparison.OrdinalIgnoreCase),
            Dlss5InstallProfile.OptiScalerNrBeforeSr => !version.Contains(OptiScalerSplitVersion, StringComparison.OrdinalIgnoreCase),
            Dlss5InstallProfile.LatestFeederBeta => !version.Contains($"Feeder {BundledFeederBetaVersion}", StringComparison.OrdinalIgnoreCase),
            Dlss5InstallProfile.OpenGlBridge => !version.Contains($"OpenGL Bridge {OpenGlBridgeVersion}", StringComparison.OrdinalIgnoreCase),
            Dlss5InstallProfile.MaximumQuality when record.Mode is Dlss5DeploymentMode.NativeDirectX11 or Dlss5DeploymentMode.NativeVulkan
                => !version.Contains($"Bridge {BridgeVersion}", StringComparison.OrdinalIgnoreCase),
            Dlss5InstallProfile.MaximumQuality when IsFeederMode(record.Mode)
                => !version.Contains($"Feeder {BundledFeederVersion}", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private readonly HttpClient _http;
    private readonly ICrashReporter _crashReporter;
    private readonly IAuxInstallService _auxInstallService;
    private readonly IShaderPackService _shaderPackService;
    private readonly Renodx5AddonService _renodx5AddonService;
    private readonly ISevenZipExtractor _sevenZipExtractor;
    private readonly IDxvkService? _dxvkService;
    private readonly IReShadeUpdateService? _reShadeUpdateService;
    private readonly DeepFriedChickenService? _deepFriedChicken;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public Dlss5ComponentService(
        HttpClient http,
        ICrashReporter crashReporter,
        IAuxInstallService auxInstallService,
        IShaderPackService shaderPackService,
        Renodx5AddonService renodx5AddonService,
        ISevenZipExtractor sevenZipExtractor,
        IDxvkService? dxvkService = null,
        IReShadeUpdateService? reShadeUpdateService = null,
        DeepFriedChickenService? deepFriedChicken = null)
    {
        _http = http;
        _crashReporter = crashReporter;
        _auxInstallService = auxInstallService;
        _shaderPackService = shaderPackService;
        _renodx5AddonService = renodx5AddonService;
        _sevenZipExtractor = sevenZipExtractor;
        _dxvkService = dxvkService;
        _reShadeUpdateService = reShadeUpdateService;
        _deepFriedChicken = deepFriedChicken;
    }

    public static string? FindInstalledDeploymentPath(string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot)) return null;
        var root = Path.GetFullPath(gameRoot);
        if (File.Exists(Path.Combine(root, RecordRelativePath))) return root;
        if (DeploymentPathCache.TryGetValue(root, out var cached)
            && cached.ExpiresUtcTicks > DateTime.UtcNow.Ticks
            && (cached.Path == null || Directory.Exists(cached.Path)))
            return cached.Path;

        var matches = FindInstalledDeploymentPaths(gameRoot);
        if (matches.Count == 1)
        {
            DeploymentPathCache[root] = new(matches[0], DateTime.UtcNow.AddMinutes(1).Ticks);
            return matches[0];
        }

        var gameExecutable = new PeHeaderService().FindGameExe(gameRoot);
        var executableDirectory = gameExecutable == null ? null : Path.GetDirectoryName(gameExecutable);
        var result = executableDirectory == null
            ? null
            : matches.FirstOrDefault(path => path.Equals(executableDirectory, StringComparison.OrdinalIgnoreCase));
        DeploymentPathCache[root] = new(result, DateTime.UtcNow.AddSeconds(result == null ? 15 : 60).Ticks);
        return result;
    }

    internal static IReadOnlyList<string> FindInstalledDeploymentPaths(string gameRoot, int maxDepth = 5)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot)) return Array.Empty<string>();

        var matches = new List<string>();
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((Path.GetFullPath(gameRoot), 0));
        while (pending.Count > 0)
        {
            var (directory, depth) = pending.Dequeue();
            if (File.Exists(Path.Combine(directory, RecordRelativePath)))
                matches.Add(directory);
            if (depth >= maxDepth) continue;

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    var info = new DirectoryInfo(child);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0
                        || info.Name.Equals(".adas", StringComparison.OrdinalIgnoreCase))
                        continue;
                    pending.Enqueue((child, depth + 1));
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<string> RemoveOtherManagedDeployments(string gameRoot, string targetDeploymentPath)
    {
        var root = Path.GetFullPath(gameRoot);
        var target = Path.GetFullPath(targetDeploymentPath);
        var relativeTarget = Path.GetRelativePath(root, target);
        if (Path.IsPathRooted(relativeTarget)
            || relativeTarget.Equals("..", StringComparison.Ordinal)
            || relativeTarget.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidOperationException("The selected DLSS 5 target is outside the game folder.");

        var errors = new List<string>();
        foreach (var deploymentPath in FindInstalledDeploymentPaths(root))
        {
            if (deploymentPath.Equals(target, StringComparison.OrdinalIgnoreCase)) continue;
            errors.AddRange(UninstallTrackedFiles(deploymentPath, _crashReporter));
        }
        return errors;
    }

    public Dlss5DeploymentMode GetInstalledMode(string deploymentPath)
    {
        var record = LoadRecord(deploymentPath);
        if (record?.Mode is not null and not Dlss5DeploymentMode.None
            && record.InstalledHashes.Keys.Any(File.Exists))
            return record.Mode;
        var addonPath = ModInstallService.GetAddonDeployPath(deploymentPath);
        if (HasCompatibleAddon(deploymentPath, addonPath, BridgeAddon, is32Bit: false)
            || HasCompatibleAddon(deploymentPath, addonPath, ObsoleteBridgeAddon, is32Bit: false))
        {
            var bridgeConfig = ReadConfig(Path.Combine(deploymentPath, BridgeConfig));
            return bridgeConfig.TryGetValue("vk_mirror", out var mirror) && mirror == "1"
                   && bridgeConfig.TryGetValue("source", out var source)
                   && source.Equals("mirror", StringComparison.OrdinalIgnoreCase)
                ? Dlss5DeploymentMode.NativeVulkan
                : Dlss5DeploymentMode.NativeDirectX11;
        }
        if (HasCompatibleAddon(deploymentPath, addonPath, FeederAddon, is32Bit: false)
            || HasCompatibleAddon(deploymentPath, addonPath, FeederAddon32, is32Bit: true))
            return File.Exists(Path.Combine(deploymentPath, "opengl32.dll"))
                ? Dlss5DeploymentMode.OpenGlFeeder
                : Dlss5DeploymentMode.Dx11Feeder;
        var renoDxNames = new[]
        {
            Renodx5AddonService.AddonFileName,
            RenoDxDeploymentName,
            "renodx-dlss5(2).addon64",
        };
        if (renoDxNames.Any(name => HasCompatibleAddon(deploymentPath, addonPath, name, is32Bit: false)))
            return Dlss5DeploymentMode.NativeDirectX12;
        return Dlss5DeploymentMode.None;
    }

    private static bool HasCompatibleAddon(string root, string addonPath, string fileName, bool is32Bit)
        => new[] { Path.Combine(addonPath, fileName), Path.Combine(root, fileName) }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Any(path => File.Exists(path) && AddonPackService.IsAddonArchitectureCompatible(path, is32Bit));

    private async Task<Dlss5InstallResult> InstallCoreAsync(
        string gameName,
        Dlss5Assessment assessment,
        IProgress<(string message, double percent)>? progress = null,
        CancellationToken cancellationToken = default,
        string? reShadeChannel = null,
        string? store = null,
        Dlss5InstallProfile profile = Dlss5InstallProfile.MaximumQuality,
        Dlss5ManualOverrides? overrides = null)
    {
        if (!assessment.CanInstall || string.IsNullOrWhiteSpace(assessment.DeploymentPath))
            throw new InvalidOperationException(string.Join(Environment.NewLine, assessment.BlockingReasons));
        Dlss5RuntimePrerequisites.EnsureAvailable(assessment.DeploymentPath, assessment.Is64Bit);

        profile = NormalizeProfileForMode(assessment.Mode, assessment.Is64Bit, profile);

        var existingProfile = LoadRecord(assessment.DeploymentPath)?.Profile;
        if (RequiresPipelineRemoval(existingProfile, profile))
            throw new InvalidOperationException("Remove the current DLSS suite with its × button before switching rendering pipelines.");
        if (!IsOptiScalerNrProfile(profile) && File.Exists(Path.Combine(assessment.DeploymentPath, "nvngx.dll_dlssnr.dll")))
            throw new InvalidOperationException("Remove the OptiScaler NR pipeline before installing a different DLSS suite.");
        if (IsOptiScalerNrProfile(profile))
            return await InstallOptiScalerNrAsync(assessment, profile, progress, cancellationToken).ConfigureAwait(false);
        if (profile == Dlss5InstallProfile.StandaloneAio)
            return await InstallAioAsync(assessment, progress, cancellationToken).ConfigureAwait(false);
        if (LoadRecord(assessment.DeploymentPath)?.Profile == Dlss5InstallProfile.StandaloneAio
            || File.Exists(Path.Combine(ModInstallService.GetAddonDeployPath(assessment.DeploymentPath), AioAddon)))
            throw new InvalidOperationException("Remove the standalone AIO suite with its × button before installing a different DLSS route. The two pipelines must not run together.");

        var path = assessment.DeploymentPath;
        var installed = new List<string>();
        var record = LoadRecord(path) ?? new Dlss5InstallRecord();

        if (assessment.Mode is Dlss5DeploymentMode.NativeDirectX12
            or Dlss5DeploymentMode.NativeDirectX11
            or Dlss5DeploymentMode.NativeVulkan)
            RestoreNativeGameRuntimes(path, record, _crashReporter);

        if (assessment.MissingRequirements.Any(requirement =>
                requirement.StartsWith("nvngx_dlss", StringComparison.OrdinalIgnoreCase)))
        {
            progress?.Report(("Loading the supplied Streamline runtime package...", 3));
            installed.AddRange(ImportAutomaticRuntimePackage(
                path,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                hosted64Only: !assessment.Is64Bit && IsFeederMode(assessment.Mode)));
        }

        // Runtime import persists ownership immediately. Reload so later suite
        // writes cannot accidentally discard those journal entries.
        record = LoadRecord(path) ?? record;

        if (!assessment.Is64Bit && IsFeederMode(assessment.Mode))
        {
            var hostedRuntimePath = Path.Combine(path, "host64");
            var missingHostedRuntimes = new[] { "nvngx_dlssnr.dll", "nvngx_dlss.dll" }
                .Where(name => !IsUsableRuntimeFile(Path.Combine(hostedRuntimePath, name))
                    && !IsUsableRuntimeFile(Path.Combine(path, name)))
                .ToArray();
            if (missingHostedRuntimes.Length > 0)
                throw new FileNotFoundException(
                    $"The supplied Streamline runtime package is required before this 32-bit game can be installed. Missing: {string.Join(", ", missingHostedRuntimes)}.",
                    Path.Combine(hostedRuntimePath, missingHostedRuntimes[0]));
        }

        var warnings = assessment.MissingRequirements
            .Where(requirement => ShouldRemainPostInstallWarning(assessment.Mode, path, requirement))
            .ToList();

        progress?.Report(("Preparing author-published components...", 5));
        var compatibilityPlan = GetCompatibilityPlan(assessment.Mode, assessment.Is64Bit, profile);
        // Manual mode can override the profile's recommended neural consumer (e.g. force the
        // feeder-pinned v4.55 or the native/latest v4.70) without changing the curated profile.
        if (overrides?.RenoDxPackage is { } renoDxOverride)
            compatibilityPlan = compatibilityPlan with { RenoDxPackage = renoDxOverride };
        // Deep Fried Chicken is an alternative neural consumer, imported locally (never bundled).
        // When chosen it is deployed wherever the RenoDX consumer would go, replacing it.
        var useDfc = overrides?.DeepFriedChicken == true && _deepFriedChicken?.IsImported == true;
        StagedComponent? staged = null;
        if (compatibilityPlan.InstallFeeder)
            staged = await EnsureStagedAsync(assessment.Mode, assessment.Is64Bit, profile, cancellationToken).ConfigureAwait(false);
        var bundledRenoDx = Path.Combine(GetBundledComponentDirectory(), Renodx5AddonService.AddonFileName);
        var selectedStableRenoDx = compatibilityPlan.RenoDxPackage switch
        {
            Dlss5RenoDxPackage.Native470 => Path.Combine(GetBundledComponentDirectory(), NativeRenoDxAsset),
            Dlss5RenoDxPackage.Feeder455 => Path.Combine(GetBundledComponentDirectory(), FeederRenoDxAsset),
            _ => bundledRenoDx,
        };
        var bundledBridge = Path.Combine(GetBundledComponentDirectory(), BridgeAddon);
        var bundledOpenGlBridge = Path.Combine(GetBundledComponentDirectory(), OpenGlBridgeAddon);
        var stagedBridge = Path.Combine(GetComponentStagingPath(), BridgeAddon);
        var stagingVersionFile = Path.Combine(GetComponentStagingPath(), "version.txt");
        var hasUserBridgeImport = File.Exists(stagingVersionFile)
            && File.ReadAllText(stagingVersionFile).Trim() == "local-user-import";
        var selectedBridge = File.Exists(stagedBridge) && (hasUserBridgeImport || !File.Exists(bundledBridge))
            ? stagedBridge : bundledBridge;
        if (compatibilityPlan.UsesExperimentalUnified && File.Exists(bundledRenoDx))
        {
            ValidatePortableExecutable(bundledRenoDx, 64 * 1024, Renodx5AddonService.AddonFileName, expectedMachine: 0x8664);
            _renodx5AddonService.StageLocalAddon(bundledRenoDx, BundledRenoDxVersion);
        }
        if (compatibilityPlan.UsesExperimentalUnified)
        {
            await _renodx5AddonService.EnsureStagingAsync(progress, autoRedeploy: false).ConfigureAwait(false);
            if (!_renodx5AddonService.IsStagingReady)
                throw new InvalidOperationException("The experimental unified RenoDX DLSS add-on could not be staged. No suite files were installed.");
        }
        else
        {
            if (!File.Exists(selectedStableRenoDx))
                throw new FileNotFoundException($"The required {compatibilityPlan.ProfileName} RenoDX package is missing from Adas.", selectedStableRenoDx);
            ValidateComponent(Path.GetFileName(selectedStableRenoDx), selectedStableRenoDx);
        }
        if (compatibilityPlan.InstallDx11Bridge)
        {
            if (!File.Exists(selectedBridge))
                throw new FileNotFoundException($"The DLSS 5 Bridge {BridgeVersion} package is missing from Adas.", selectedBridge);
            ValidateComponent(BridgeAddon, selectedBridge);
        }
        if (compatibilityPlan.InstallOpenGlBridge)
        {
            if (!File.Exists(bundledOpenGlBridge))
                throw new FileNotFoundException($"The OpenGL Bridge {OpenGlBridgeVersion} package is missing from Adas.", bundledOpenGlBridge);
            ValidateComponent(OpenGlBridgeAddon, bundledOpenGlBridge);
        }

        record.Mode = assessment.Mode;
        record.Profile = profile;
        // Record which neural consumer was deployed so verification requires the right files.
        // Deep Fried Chicken replaces the RenoDX consumer; anything else installs RenoDX.
        record.DeepFriedChicken = useDfc;
        if (assessment.Mode == Dlss5DeploymentMode.Dx9ViaDxvkFeeder)
            record.PreferDxvkForDirectX9 = true;
        record.ComponentVersion = $"{compatibilityPlan.ProfileName}; Feeder {staged?.Version ?? "not used"}";
        record.InstalledAtUtc = DateTime.UtcNow;


        if (compatibilityPlan.InstallFeeder
            && assessment.Mode is Dlss5DeploymentMode.Dx9Feeder or Dlss5DeploymentMode.Dx8Feeder)
        {
            progress?.Report(("Preparing the legacy DirectX compatibility layer...", 18));
            installed.AddRange(await InstallDgVoodooAsync(path, assessment.Is64Bit, record, cancellationToken,
                directX8: assessment.Mode == Dlss5DeploymentMode.Dx8Feeder).ConfigureAwait(false));
        }
        else if (compatibilityPlan.InstallFeeder
                 && assessment.Mode is Dlss5DeploymentMode.Dx10ViaDxvkFeeder or Dlss5DeploymentMode.Dx9ViaDxvkFeeder)
        {
            var sourceApi = assessment.Mode == Dlss5DeploymentMode.Dx9ViaDxvkFeeder
                ? GraphicsApiType.DirectX9 : GraphicsApiType.DirectX10;
            progress?.Report(($"Translating {GraphicsApiDetector.GetLabel(sourceApi)} to Vulkan...", 18));
            installed.AddRange(await InstallDxvkTranslationAsync(gameName, path, assessment.Is64Bit, sourceApi, record, progress, cancellationToken).ConfigureAwait(false));
        }

        progress?.Report(("Preparing ReShade with add-on support...", 25));
        var reShadeFileName = GetReShadeFileName(assessment.Mode, profile);
        var installedReShadeVersion = AuxInstallService.ReadInstalledVersion(path, reShadeFileName);
        EnsureBundledReShadeStaging(reShadeChannel);
        if (assessment.Mode is not (Dlss5DeploymentMode.Dx10ViaDxvkFeeder or Dlss5DeploymentMode.Dx9ViaDxvkFeeder)
            && (assessment.MissingRequirements.Any(requirement =>
                requirement.StartsWith("ReShade", StringComparison.OrdinalIgnoreCase))
            || ShouldRefreshReShade(reShadeChannel, installedReShadeVersion)))
        {
            if (Dlss5SwitchJournal.Current != null)
            {
                // The DLSS 5 suite always deploys add-ons (Feeder, RenoDX), which require the modern
                // ReShade add-on API (18/20). Legacy ReShade (6.3.3, API 14) cannot load them, so the
                // suite always uses the stable build regardless of any per-game legacy channel setting.
                var version = BundledStableReShadeVersion;
                var source = Path.Combine(GetBundledComponentDirectory(), $"ReShade-{version}-{(assessment.Is64Bit ? 64 : 32)}.dll");
                InstallTrackedFile(source, Path.Combine(path, reShadeFileName), path, record);
            }
            else await _auxInstallService.InstallReShadeAsync(
                gameName,
                path,
                use32Bit: !assessment.Is64Bit,
                filenameOverride: reShadeFileName,
                useNormalReShade: false,
                progress: progress,
                // Never install a legacy (non-add-on) ReShade for the suite — bump it to Stable.
                channel: AuxInstallService.IsLegacyVersion(reShadeChannel) ? "Stable" : reShadeChannel,
                store: store,
                mergeIni: true).ConfigureAwait(false);
        }

        RepairReShadeConfiguration(path, record);

        if (compatibilityPlan.InstallFeeder)
        {
            progress?.Report(("Preparing the ReShade shader framework...", 35));
            if (!HasBundledReShadeFrameworkHeaders())
                await _shaderPackService.EnsurePacksAsync(new[] { "CrosireMaster" }).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(("Installing the DLSS 5 neural add-on...", 45));
        var addonDeployPath = ModInstallService.GetAddonDeployPath(path);
        Directory.CreateDirectory(addonDeployPath);
        if (!assessment.Is64Bit)
            DisableObsolete32BitRenoDx(path, addonDeployPath, record);
        if (assessment.Is64Bit && useDfc)
        {
            // Deploy the imported Deep Fried Chicken consumer into the same add-on folder Adas
            // resolved for this game; RenoDX is dropped below by RemoveIncompatibleDlssAddons.
            foreach (var name in _deepFriedChicken!.DeployFiles(includeDx11Bridge: false))
            {
                var dfcDestination = Path.Combine(addonDeployPath, name);
                InstallTrackedFile(_deepFriedChicken.CachedFile(name), dfcDestination, path, record);
                installed.Add(dfcDestination);
            }
        }
        else if (assessment.Is64Bit)
        {
            var renoName = compatibilityPlan.UsesExperimentalUnified
                ? Renodx5AddonService.AddonFileName
                : RenoDxDeploymentName;
            var renoSource = compatibilityPlan.UsesExperimentalUnified
                ? _renodx5AddonService.StagedFilePath
                : selectedStableRenoDx;
            var renoDestination = Path.Combine(addonDeployPath, renoName);
            InstallTrackedFile(renoSource, renoDestination, path, record);
            installed.Add(renoDestination);
        }
        if (compatibilityPlan.InstallDx11Bridge)
        {
            var bridgeDestination = Path.Combine(addonDeployPath, BridgeAddon);
            InstallTrackedFile(selectedBridge, bridgeDestination, path, record);
            installed.Add(bridgeDestination);
        }
        if (compatibilityPlan.InstallOpenGlBridge)
        {
            var bridgeDestination = Path.Combine(addonDeployPath, OpenGlBridgeAddon);
            InstallTrackedFile(bundledOpenGlBridge, bridgeDestination, path, record);
            installed.Add(bridgeDestination);
        }
        RemoveIncompatibleDlssAddons(path, addonDeployPath, compatibilityPlan, record, useDfc);
        RepairReShadeAddonState(path);
        if (assessment.Is64Bit && compatibilityPlan.UsesExperimentalUnified)
            EnsureUnifiedRenoDxSettings(path, record);
        if (assessment.Is64Bit && compatibilityPlan.RenoDxPackage == Dlss5RenoDxPackage.Native470)
            EnsureStableRenoDxSettings(path, record);
        if (RequiresEarlyLoadSettings(assessment, compatibilityPlan))
            EnsureNativeEarlyLoadSettings(
                path,
                compatibilityPlan,
                record,
                force: assessment.Mode == Dlss5DeploymentMode.NativeVulkan);
        if (assessment.Mode == Dlss5DeploymentMode.NativeVulkan)
            EnsureTrackedConfig(Path.Combine(path, BridgeConfig), NativeVulkanBridgeDefaults, path, record);

        if (!compatibilityPlan.InstallFeeder)
        {
            RemoveFeederComponent(path, addonDeployPath, record);
            SaveRecord(path, record);
            var nativeVerificationProblems = Dlss5DiagnosticService.VerifyInstallation(path, assessment.Mode, assessment.Is64Bit);
            if (nativeVerificationProblems.Count > 0)
                throw new IOException("Automatic verification found an incomplete installation:\n• " + string.Join("\n• ", nativeVerificationProblems));
            return new(true, assessment.Mode, path, installed, warnings,
                $"{assessment.ModeLabel} DLSS support is installed with {compatibilityPlan.ProfileName}. Adas verified every required file. Configure it from the ReShade overlay in game.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(("Installing the recommended Feeder component...", 75));
        {
            var feederAddon = assessment.Is64Bit ? FeederAddon : FeederAddon32;
            if (assessment.Is64Bit)
                InstallTrackedFeederBinary(
                    staged!.Files[feederAddon],
                    Path.Combine(addonDeployPath, feederAddon),
                    path,
                    record,
                    compatibilityPlan.PatchFeederForUnifiedName);
            else
                InstallTrackedFile(staged!.Files[feederAddon], Path.Combine(addonDeployPath, feederAddon), path, record);
            var shaderDirectory = Path.Combine(path, "reshade-shaders", "Shaders");
            Directory.CreateDirectory(shaderDirectory);
            var headerSource = HasBundledReShadeFrameworkHeaders()
                ? GetBundledComponentDirectory()
                : ShaderPackService.ShadersDir;
            installed.AddRange(InstallReShadeFrameworkHeaders(headerSource, path, record));
            InstallTrackedFile(staged.Files[FeederShader], Path.Combine(shaderDirectory, FeederShader), path, record);
            EnsureTrackedConfig(
                Path.Combine(path, FeederConfig),
                compatibilityPlan.UsesLatestFeederBeta ? FeederBetaDefaults : FeederDefaults,
                path,
                record);
            installed.Add(Path.Combine(addonDeployPath, feederAddon));
            installed.Add(Path.Combine(shaderDirectory, FeederShader));

            var migratedLaunchPadSources = new List<string>();
            try
            {
                var preservedLaunchPad = MigrateLegacyLaunchPad(path, record, migratedLaunchPadSources);
                if (preservedLaunchPad.Count > 0)
                    warnings.Add("Legacy iMMERSE LaunchPad files were moved into .adas\\legacy-launchpad-backup because DLSS5_Feed.fx no longer uses them.");

                var motionProvider = await EnsureMotionProviderStagedAsync(cancellationToken).ConfigureAwait(false);
                foreach (var providerFile in motionProvider)
                {
                    var destination = Path.Combine(path, providerFile.RelativeGamePath);
                    InstallTrackedFile(providerFile.SourcePath, destination, path, record);
                    installed.Add(destination);
                }

                var presetPath = EnsureFeederPreset(path, record);
                installed.Add(presetPath);

                if ((assessment.Mode is Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.Dx10ViaDxvkFeeder
                        or Dlss5DeploymentMode.Dx9ViaDxvkFeeder)
                    && staged.Files.TryGetValue(FeederVulkanLayer, out var layerArchive))
                {
                    installed.AddRange(InstallVulkanFallbackLayer(path, layerArchive, record));
                }

                if (!assessment.Is64Bit)
                    InstallHostedFeederFiles(
                        path,
                        staged.Files,
                        AuxInstallService.RsStagedPath64,
                        compatibilityPlan.UsesExperimentalUnified
                            ? _renodx5AddonService.StagedFilePath
                            : selectedStableRenoDx,
                        compatibilityPlan,
                        record,
                        installed,
                        warnings,
                        useDfc ? _deepFriedChicken : null);
            }
            catch (Exception installError)
            {
                try
                {
                    RollbackLegacyLaunchPad(path, record, migratedLaunchPadSources);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException(
                        installError,
                        new InvalidOperationException($"Legacy LaunchPad rollback failed: {rollbackError.Message}", rollbackError));
                }
                throw;
            }
        }

        RepairReShadeAddonState(path);
        if (!assessment.Is64Bit)
            RepairReShadeAddonState(Path.Combine(path, "host64"));

        var verificationProblems = Dlss5DiagnosticService.VerifyInstallation(path, assessment.Mode, assessment.Is64Bit);
        if (verificationProblems.Count > 0)
            throw new IOException("Automatic verification found an incomplete installation:\n• " + string.Join("\n• ", verificationProblems));

        if (assessment.Mode is not (Dlss5DeploymentMode.Dx10ViaDxvkFeeder or Dlss5DeploymentMode.Dx9ViaDxvkFeeder))
        {
            var reShadeName = GetReShadeFileName(assessment.Mode, profile);
            _auxInstallService.SaveAuxRecord(new AuxInstalledRecord
            {
                GameName = gameName,
                InstallPath = path,
                Store = store ?? "",
                AddonType = AuxInstallService.TypeReShade,
                InstalledAs = reShadeName,
                InstalledAt = DateTime.UtcNow,
                Channel = reShadeChannel,
            });
        }

        SaveRecord(path, record);
        progress?.Report(("DLSS 5 files installed and checked.", 100));
        _crashReporter.Log($"[Dlss5ComponentService] Installed {assessment.Mode} v{staged.Version} for '{gameName}' at '{path}'");

        var completionMessage = $"{assessment.ModeLabel} installed with the {compatibilityPlan.ProfileName} compatibility profile. " +
                                "Neural rendering, LumeniteFX Kernel, and DLSS 5 Feed were enabled automatically in ReShade with the correct provider binding. Adas verified every required file.";
        if (assessment.Mode == Dlss5DeploymentMode.VulkanFeeder)
            completionMessage += " A per-game Vulkan fallback launcher is available under DLSS5-Vulkan-Fallback if the Feeder log says its normal vkCreateDevice hook was not reached.";
        if (assessment.Mode is Dlss5DeploymentMode.Dx9Feeder or Dlss5DeploymentMode.Dx8Feeder)
            completionMessage += " dgVoodoo2 was configured automatically to translate legacy DirectX to DirectX 11; ReShade remains on dxgi.dll so the two loaders do not conflict.";
        if (assessment.Mode == Dlss5DeploymentMode.Dx10ViaDxvkFeeder)
            completionMessage += " DXVK and the Vulkan ReShade layer were configured automatically because the 64-bit Feeder has no native DirectX 10 backend.";
        if (assessment.Mode == Dlss5DeploymentMode.Dx9ViaDxvkFeeder)
            completionMessage += " The crashing dgVoodoo/ReShade chain was replaced for this game only with DXVK and the 32-bit Vulkan Feeder path.";
        if (assessment.Mode == Dlss5DeploymentMode.Dx10Feeder)
            completionMessage += " Feeder's native 32-bit DirectX 10 relay was installed directly as dxgi.dll; no DXVK or machine-wide Vulkan layer was added.";
        if (!assessment.Is64Bit)
            completionMessage += " For this 32-bit game, neural rendering runs in host64; open ReShade's Add-ons tab, expand DLSS 5 Feed, and use its complete RenoDX neural-rendering controls.";
        return new(true, assessment.Mode, path, installed, warnings, completionMessage);
    }

    public IReadOnlyList<string> Uninstall(string deploymentPath)
    {
        Dlss5SwitchJournal.Recover(deploymentPath);
        SaveInstalledProfileSettings(deploymentPath);
        var errors = UninstallTrackedFiles(deploymentPath, _crashReporter).ToList();
        if (errors.Count == 0)
        {
            errors.AddRange(QuarantineOrphanSuiteFiles(deploymentPath, _crashReporter));
        }
        return errors;
    }

    private static void WriteTextAtomically(string path, string text)
    {
        Dlss5SwitchJournal.BeforeWrite(path);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }
        finally { DeleteIfExists(temporary); }
    }

    internal static bool EnsureBundledReShadeStaging(string? channel)
    {
        var effectiveChannel = string.IsNullOrWhiteSpace(channel)
            ? AuxInstallService.ChannelStable
            : channel;
        string version;
        string destination32;
        string destination64;
        if (string.Equals(effectiveChannel, AuxInstallService.ChannelStable, StringComparison.OrdinalIgnoreCase))
        {
            version = BundledStableReShadeVersion;
            destination32 = AuxInstallService.RsStagedPath32;
            destination64 = AuxInstallService.RsStagedPath64;
        }
        else if (string.Equals(effectiveChannel, BundledLegacyReShadeVersion, StringComparison.OrdinalIgnoreCase))
        {
            version = BundledLegacyReShadeVersion;
            destination32 = AuxInstallService.GetLegacyStagedPath(version, use32Bit: true);
            destination64 = AuxInstallService.GetLegacyStagedPath(version, use32Bit: false);
        }
        else
        {
            return false;
        }

        var componentDirectory = GetBundledComponentDirectory();
        var source32 = Path.Combine(componentDirectory, $"ReShade-{version}-32.dll");
        var source64 = Path.Combine(componentDirectory, $"ReShade-{version}-64.dll");
        if (!File.Exists(source32) || !File.Exists(source64)) return false;
        ValidatePortableExecutable(source32, AuxInstallService.MinReShadeSize, Path.GetFileName(source32), expectedMachine: 0x014c);
        ValidatePortableExecutable(source64, AuxInstallService.MinReShadeSize, Path.GetFileName(source64), expectedMachine: 0x8664);

        if (!File.Exists(destination32) || new FileInfo(destination32).Length <= AuxInstallService.MinReShadeSize)
            CopyAtomically(source32, destination32);
        if (!File.Exists(destination64) || new FileInfo(destination64).Length <= AuxInstallService.MinReShadeSize)
            CopyAtomically(source64, destination64);
        return true;
    }

    internal static IReadOnlyList<string> QuarantineOrphanSuiteFiles(
        string deploymentPath,
        ICrashReporter crashReporter)
    {
        var root = Path.GetFullPath(deploymentPath);
        var addonPath = ModInstallService.GetAddonDeployPath(root);
        var hostPath = Path.Combine(root, "host64");
        var candidates = new[]
        {
            Path.Combine(root, Renodx5AddonService.AddonFileName),
            Path.Combine(addonPath, Renodx5AddonService.AddonFileName),
            Path.Combine(root, RenoDxDeploymentName),
            Path.Combine(addonPath, RenoDxDeploymentName),
            Path.Combine(root, "renodx-dlss5(2).addon64"),
            Path.Combine(addonPath, "renodx-dlss5(2).addon64"),
            Path.Combine(root, "renodx-dlss.addon32"),
            Path.Combine(addonPath, "renodx-dlss.addon32"),
            Path.Combine(root, "renodx-dlss5.addon32"),
            Path.Combine(addonPath, "renodx-dlss5.addon32"),
            Path.Combine(root, BridgeAddon),
            Path.Combine(addonPath, BridgeAddon),
            Path.Combine(root, ObsoleteBridgeAddon),
            Path.Combine(addonPath, ObsoleteBridgeAddon),
            Path.Combine(root, "dlss5-dx11-bridge.addon32"),
            Path.Combine(addonPath, "dlss5-dx11-bridge.addon32"),
            Path.Combine(root, FeederAddon),
            Path.Combine(addonPath, FeederAddon),
            Path.Combine(root, FeederAddon32),
            Path.Combine(addonPath, FeederAddon32),
            Path.Combine(hostPath, FeederHost64),
            Path.Combine(hostPath, RenoDxDeploymentName),
            Path.Combine(hostPath, Renodx5AddonService.AddonFileName),
            Path.Combine(root, "reshade-shaders", "Shaders", FeederShader),
            Path.Combine(root, FeederConfig),
            Path.Combine(root, BridgeConfig),
            Path.Combine(root, ObsoleteBridgeConfig),
        };

        var errors = new List<string>();
        var additional = GetCleanupPlan(root, Dlss5DeploymentMode.None, Dlss5InstallProfile.MaximumQuality).Files.Select(file => file.Path);
        foreach (var path in candidates.Concat(additional).Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
        {
            try
            {
                var preserved = PreserveModifiedFile(root, path);
                crashReporter.Log($"[Dlss5ComponentService.Uninstall] Archived orphaned suite file as '{preserved}'");
            }
            catch (Exception ex)
            {
                errors.Add($"{path}: {ex.Message}");
            }
        }
        return errors;
    }

    internal static IReadOnlyList<string> InstallReShadeFrameworkHeaders(
        string stagingDirectory,
        string deploymentPath,
        Dlss5InstallRecord record)
    {
        var sources = ReShadeFrameworkHeaders.ToDictionary(
            header => header,
            header =>
            {
                var rootSource = Path.Combine(stagingDirectory, header);
                return File.Exists(rootSource)
                    ? rootSource
                    : Path.Combine(stagingDirectory, "CrosireMaster", header);
            },
            StringComparer.OrdinalIgnoreCase);
        var missing = sources.Where(pair => !File.Exists(pair.Value)).Select(pair => pair.Key).ToArray();
        if (missing.Length > 0)
            throw new FileNotFoundException(
                $"The ReShade shader framework is incomplete. Missing: {string.Join(", ", missing)}. Repair the DLSS 5 suite again while online.",
                sources[missing[0]]);

        var shaderDirectory = Path.Combine(deploymentPath, "reshade-shaders", "Shaders");
        Directory.CreateDirectory(shaderDirectory);
        var installed = new List<string>();
        foreach (var pair in sources)
        {
            var destination = Path.Combine(shaderDirectory, pair.Key);
            InstallTrackedFile(pair.Value, destination, deploymentPath, record);
            installed.Add(destination);
        }
        return installed;
    }

    public IReadOnlyList<string> ImportLocalRuntimeFolder(
        string sourcePath,
        string deploymentPath,
        bool overwriteExisting = true,
        bool hosted64Only = false)
    {
        string? temporaryDirectory = null;
        var sourceRoot = sourcePath;
        if (File.Exists(sourcePath) && Path.GetExtension(sourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            temporaryDirectory = Path.Combine(Path.GetTempPath(), $"adas-runtime-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryDirectory);
            ExtractArchiveSafely(
                sourcePath,
                temporaryDirectory,
                maxEntryBytes: 256L * 1024 * 1024,
                maxTotalBytes: 512L * 1024 * 1024,
                maxEntries: 1_000,
                packageLabel: "Runtime");
            sourceRoot = temporaryDirectory;
        }
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException("Select an extracted Streamline/runtime folder or its ZIP archive.");

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Renodx5AddonService.AddonFileName, "nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssnr.dll",
            "sl.common.dll", "sl.dlss.dll", "sl.dlss_g.dll", "sl.dlss_nr.dll",
            "sl.interposer.dll", "sl.nis.dll", "sl.pcl.dll", "sl.reflex.dll",
        };
        try
        {
            var candidates = Dlss5CompatibilityService.EnumerateFilesSafe(sourceRoot, maxDepth: 4)
                .Select(file => (Path: file, Name: Path.GetFileName(file)))
                .Where(file => file.Name != null && allowed.Contains(file.Name))
                .GroupBy(file => file.Name!, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (candidates.Any(group => group.Select(file => FileHelper.ComputeSha256(file.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
                throw new InvalidOperationException("The selected package contains different files with the same runtime name. Use an unambiguous package.");

            var installed = new List<string>();
            var record = LoadRecord(deploymentPath) ?? new Dlss5InstallRecord { InstalledAtUtc = DateTime.UtcNow };
            var runtimeDestinationDirectory = hosted64Only
                ? Path.Combine(deploymentPath, "host64")
                : deploymentPath;
            foreach (var group in candidates)
            {
                if (hosted64Only
                    && group.Key.Equals(Renodx5AddonService.AddonFileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var destination = group.Key.Equals(Renodx5AddonService.AddonFileName, StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(ModInstallService.GetAddonDeployPath(deploymentPath), group.Key)
                    : Path.Combine(runtimeDestinationDirectory, group.Key);
                if (!overwriteExisting && File.Exists(destination))
                {
                    _crashReporter.Log($"[Dlss5ComponentService] Preserved existing game runtime '{destination}' during automatic import");
                    continue;
                }
                InstallTrackedFile(group.First().Path, destination, deploymentPath, record);
                installed.Add(destination);
            }

            var hostDirectory = Path.Combine(deploymentPath, "host64");
            if (!hosted64Only
                && Directory.Exists(hostDirectory)
                && IsFeederMode(record.Mode))
            {
                foreach (var runtimeName in new[] { "nvngx_dlssnr.dll", "nvngx_dlss.dll" })
                {
                    var source = Path.Combine(deploymentPath, runtimeName);
                    if (!File.Exists(source)) continue;
                    var destination = Path.Combine(hostDirectory, runtimeName);
                    InstallTrackedFile(source, destination, deploymentPath, record);
                    installed.Add(destination);
                }
            }
            if (installed.Count > 0) SaveRecord(deploymentPath, record);
            return installed;
        }
        finally
        {
            if (temporaryDirectory != null)
            {
                try { Directory.Delete(temporaryDirectory, recursive: true); } catch { }
            }
        }
    }

    /// <summary>
    /// Repairs native games modified by older Adas builds that replaced the
    /// game's own DLSS/Streamline stack while adding DLSS NR. Native runtimes
    /// are restored only while the current file still matches the Adas-owned
    /// hash; user-modified files are never overwritten.
    /// </summary>
    internal static IReadOnlyList<string> RestoreNativeGameRuntimes(
        string deploymentPath,
        Dlss5InstallRecord record,
        ICrashReporter crashReporter)
    {
        var restored = new List<string>();
        foreach (var pair in record.OriginalBackups.ToArray())
        {
            var destination = pair.Key;
            var backup = pair.Value;
            if (string.IsNullOrWhiteSpace(backup)
                || !NativeGameRuntimeNames.Contains(Path.GetFileName(destination))
                || !File.Exists(backup))
                continue;

            if (File.Exists(destination))
            {
                if (!record.InstalledHashes.TryGetValue(destination, out var installedHash)
                    || !FileHelper.ComputeSha256(destination).Equals(installedHash, StringComparison.OrdinalIgnoreCase))
                {
                    crashReporter.Log($"[Dlss5ComponentService] Preserved user-modified native runtime '{destination}' instead of restoring its backup");
                    continue;
                }
            }

            CopyAtomically(backup, destination);
            File.Delete(backup);
            record.InstalledHashes.Remove(destination);
            record.OriginalBackups.Remove(destination);
            restored.Add(destination);
            crashReporter.Log($"[Dlss5ComponentService] Restored game-owned native runtime '{destination}'");
        }

        if (restored.Count > 0)
            SaveRecord(deploymentPath, record);
        return restored;
    }

    internal static string? FindAutomaticRuntimePackage(string userProfile, string? bundledComponentDirectory = null)
    {
        bundledComponentDirectory ??= Path.Combine(AppContext.BaseDirectory, "Assets", "DLSS5");
        var candidates = new[]
        {
            Path.Combine(bundledComponentDirectory, "streamline.zip"),
            Path.Combine(userProfile, "Downloads", "DLSS5", "streamline.zip"),
            Path.Combine(userProfile, "Downloads", "streamline.zip"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    internal IReadOnlyList<string> ImportAutomaticRuntimePackage(
        string deploymentPath,
        string userProfile,
        string? bundledComponentDirectory = null,
        bool hosted64Only = false)
    {
        var package = FindAutomaticRuntimePackage(userProfile, bundledComponentDirectory);
        return package == null
            ? Array.Empty<string>()
            : ImportLocalRuntimeFolder(package, deploymentPath, overwriteExisting: false, hosted64Only: hosted64Only);
    }

    private static bool RuntimeRequirementIsNowSatisfied(
        string deploymentPath,
        string requirement,
        bool includeHostedRuntime = false)
    {
        bool Exists(string name) => File.Exists(Path.Combine(deploymentPath, name))
            || (includeHostedRuntime && File.Exists(Path.Combine(deploymentPath, "host64", name)));
        if (requirement.StartsWith("nvngx_dlssnr.dll", StringComparison.OrdinalIgnoreCase))
            return Exists("nvngx_dlssnr.dll");
        if (requirement.StartsWith("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase))
            return Exists("nvngx_dlss.dll");
        return false;
    }

    private static bool IsFeederMode(Dlss5DeploymentMode mode)
        => mode is Dlss5DeploymentMode.Dx11Feeder
            or Dlss5DeploymentMode.Dx12Feeder
            or Dlss5DeploymentMode.VulkanFeeder
            or Dlss5DeploymentMode.Dx9Feeder
            or Dlss5DeploymentMode.Dx8Feeder
            or Dlss5DeploymentMode.OpenGlFeeder
            or Dlss5DeploymentMode.Dx10ViaDxvkFeeder
            or Dlss5DeploymentMode.Dx9ViaDxvkFeeder
            or Dlss5DeploymentMode.Dx10Feeder;

    internal static bool SupportsOpenGlBridge(Dlss5DeploymentMode mode, bool is64Bit)
        => is64Bit && mode == Dlss5DeploymentMode.OpenGlFeeder;

    internal static bool IsUsableRuntimeFile(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    internal static bool ShouldRefreshReShade(string? configuredChannel, string? installedVersion)
        => AuxInstallService.IsLegacyVersion(configuredChannel)
            && !string.Equals(configuredChannel, installedVersion, StringComparison.OrdinalIgnoreCase);

    internal static string GetReShadeFileName(Dlss5DeploymentMode mode)
        => mode == Dlss5DeploymentMode.OpenGlFeeder ? "opengl32.dll" : "dxgi.dll";

    internal static string GetReShadeFileName(Dlss5DeploymentMode mode, Dlss5InstallProfile profile)
        => profile == Dlss5InstallProfile.ExperimentalUnified && mode == Dlss5DeploymentMode.Dx9Feeder
            ? "d3d9.dll"
            : GetReShadeFileName(mode);

    internal static ReShadeInstallPolicy ResolveReShadeInstallPolicy(
        Dlss5DeploymentMode mode,
        Dlss5InstallProfile profile)
    {
        if (IsOptiScalerNrProfile(profile))
            return new(
                BlockInstall: true,
                ProxyName: null,
                PreserveSuiteShaders: true,
                Reason: "This game's DLSS 5 OptiScaler pipeline owns the graphics proxy. Change or remove it from the DLSS 5 row before installing ReShade separately.");

        var proxyName = profile == Dlss5InstallProfile.StandaloneAio
            ? AioProxyName(mode)
            : GetReShadeFileName(mode, profile);
        return new(
            BlockInstall: false,
            ProxyName: proxyName,
            PreserveSuiteShaders: true,
            Reason: $"The installed DLSS 5 {profile} pipeline owns {proxyName} and its shader files.");
    }

    public IReadOnlyList<string> ImportLocalComponentFolder(string sourcePath)
    {
        if (!Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException(sourcePath);

        var knownNames = new[]
        {
            Renodx5AddonService.AddonFileName, "renodx-dlss5.addon64", "renodx-dlss5(2).addon64",
            NativeRenoDxAsset, FeederRenoDxAsset, BridgeAddon,
            FeederAddon, FeederAddon32, "dlss5-feed-32bit.addon32", FeederHost64, FeederShader,
        };
        var files = Dlss5CompatibilityService.EnumerateFilesSafe(sourcePath, maxDepth: 5)
            .Select(file => (Path: file, Name: Path.GetFileName(file)!))
            .Where(file => knownNames.Contains(file.Name, StringComparer.OrdinalIgnoreCase))
            .Select(file => (file.Path, Name: NormalizeComponentFileName(file.Name)))
            .GroupBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var variants = group.GroupBy(file => FileHelper.ComputeSha256(file.Path), StringComparer.OrdinalIgnoreCase).ToArray();
                    if (variants.Length != 1)
                        throw new InvalidOperationException($"Multiple different copies of {group.Key} were found.");
                    return variants[0].First().Path;
                },
                StringComparer.OrdinalIgnoreCase);

        if (files.Count == 0)
            throw new InvalidOperationException("No recognized DLSS 5 component files were found.");
        var imported = new List<string>();
        foreach (var pair in files)
        {
            ValidateComponent(pair.Key, pair.Value);

            string destination;
            if (pair.Key.Equals(Renodx5AddonService.AddonFileName, StringComparison.OrdinalIgnoreCase))
            {
                _renodx5AddonService.StageLocalAddon(pair.Value, BundledRenoDxVersion);
                destination = _renodx5AddonService.StagedFilePath;
            }
            else
            {
                var staging = GetComponentStagingPath();
                Directory.CreateDirectory(staging);
                File.WriteAllText(Path.Combine(staging, "version.txt"), "local-user-import");
                destination = Path.Combine(staging, pair.Key);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!pair.Key.Equals(Renodx5AddonService.AddonFileName, StringComparison.OrdinalIgnoreCase))
                CopyAtomically(pair.Value, destination);
            imported.Add(destination);
        }
        return imported;
    }

    public string ImportReShadeAddonInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("Select a local ReShade full add-on installer.", installerPath);
        var name = Path.GetFileName(installerPath);
        if (!name.StartsWith("ReShade_Setup_", StringComparison.OrdinalIgnoreCase)
            || !name.Contains("Addon", StringComparison.OrdinalIgnoreCase)
            || !Path.GetExtension(name).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected file is not a ReShade full add-on installer.");
        ValidatePortableExecutable(installerPath, minimumBytes: 500_000, "ReShade installer");

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"adas-reshade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var extracted64 = Path.Combine(temporaryDirectory, AuxInstallService.RsStaged64);
            var extracted32 = Path.Combine(temporaryDirectory, AuxInstallService.RsStaged32);
            _sevenZipExtractor.ExtractFile(installerPath, AuxInstallService.RsStaged64, extracted64);
            _sevenZipExtractor.ExtractFile(installerPath, AuxInstallService.RsStaged32, extracted32);
            ValidatePortableExecutable(extracted64, AuxInstallService.MinReShadeSize, "ReShade64.dll");
            ValidatePortableExecutable(extracted32, AuxInstallService.MinReShadeSize, "ReShade32.dll");

            Directory.CreateDirectory(AuxInstallService.RsStagingDir);
            CopyAtomically(extracted64, AuxInstallService.RsStagedPath64);
            CopyAtomically(extracted32, AuxInstallService.RsStagedPath32);
            if (!AuxInstallService.EnsureReShadeStaging())
                throw new InvalidOperationException("The extracted ReShade files failed staging verification.");
            _crashReporter.Log($"[Dlss5ComponentService] Imported local ReShade add-on installer '{name}'");
            return name;
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); } catch { }
        }
    }

    public static Dictionary<string, string> ReadConfig(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return values;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return values;
    }

    public static void WriteConfig(string path, IReadOnlyDictionary<string, string> values)
    {
        Dlss5SwitchJournal.BeforeWrite(path);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(temporary, values.Select(pair => $"{pair.Key}={pair.Value}"));
        File.Move(temporary, path, overwrite: true);
    }

    public static IReadOnlyDictionary<string, string> GetDefaults(
        Dlss5DeploymentMode mode,
        Dlss5InstallProfile profile = Dlss5InstallProfile.MaximumQuality)
        => IsFeederMode(mode)
            ? profile == Dlss5InstallProfile.LatestFeederBeta ? FeederBetaDefaults : FeederDefaults
            : new Dictionary<string, string>();

    internal static Dlss5InstallProfile NormalizeProfileForMode(
        Dlss5DeploymentMode mode,
        bool is64Bit,
        Dlss5InstallProfile profile)
    {
        // These are the first Feeder builds that support the native 32-bit
        // DirectX 10 relay and 32-bit Vulkan transport.
        if (mode == Dlss5DeploymentMode.Dx10Feeder
            || (mode is Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.Dx10ViaDxvkFeeder
                    or Dlss5DeploymentMode.Dx9ViaDxvkFeeder
                && !is64Bit))
            return Dlss5InstallProfile.LatestFeederBeta;

        // The beta applies only to Feeder routes. The unified ShortFuse package
        // does not replace the native Vulkan bridge contract.
        if ((!IsFeederMode(mode) && profile == Dlss5InstallProfile.LatestFeederBeta)
            || (profile == Dlss5InstallProfile.OpenGlBridge && !SupportsOpenGlBridge(mode, is64Bit))
            || (mode == Dlss5DeploymentMode.NativeVulkan
                && profile == Dlss5InstallProfile.ExperimentalUnified))
            return Dlss5InstallProfile.MaximumQuality;

        return profile;
    }

    internal static string[] GetRequiredComponentNames(Dlss5DeploymentMode mode, bool is64Bit)
    {
        var names = is64Bit
            ? new List<string> { FeederAddon, FeederShader }
            : new List<string> { FeederAddon32, FeederHost64, FeederShader };
        if (mode is Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.Dx10ViaDxvkFeeder
            or Dlss5DeploymentMode.Dx9ViaDxvkFeeder)
            names.Add(FeederVulkanLayer);
        return names.ToArray();
    }

    internal static string NormalizeComponentFileName(string sourceName)
    {
        if (sourceName.Equals("renodx-dlss5.addon64", StringComparison.OrdinalIgnoreCase)
            || sourceName.Equals("renodx-dlss5(2).addon64", StringComparison.OrdinalIgnoreCase))
            return FeederRenoDxAsset;
        if (sourceName.Equals("dlss5-feed-32bit.addon32", StringComparison.OrdinalIgnoreCase))
            return FeederAddon32;
        return sourceName;
    }

    public static string GetConfigPath(string deploymentPath, Dlss5DeploymentMode mode)
        => Path.Combine(deploymentPath, FeederConfig);

    public static string GetLogPath(string deploymentPath, Dlss5DeploymentMode mode)
        => Path.Combine(deploymentPath, FeederLog);

    private async Task<StagedComponent> EnsureStagedAsync(
        Dlss5DeploymentMode mode,
        bool is64Bit,
        Dlss5InstallProfile profile,
        CancellationToken cancellationToken)
    {
        var repo = FeederRepo;
        var useBeta = profile == Dlss5InstallProfile.LatestFeederBeta;
        var required = GetRequiredComponentNames(mode, is64Bit);
        var staging = GetComponentStagingPath(useBeta ? BundledFeederBetaVersion : null);
        Directory.CreateDirectory(staging);

        var localFiles = required.ToDictionary(name => name, name => Path.Combine(staging, name), StringComparer.OrdinalIgnoreCase);
        var localVersionPath = Path.Combine(staging, "version.txt");
        var localVersion = File.Exists(localVersionPath) ? File.ReadAllText(localVersionPath).Trim() : "";
        if (!useBeta
            && localFiles.Values.All(File.Exists)
            && localVersion.Equals("local-user-import", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pair in localFiles)
                ValidateComponent(pair.Key, pair.Value);
            return new(localVersion, localFiles);
        }

        var bundled = required.ToDictionary(
            name => name,
            name => Path.Combine(GetBundledComponentDirectory(), GetBundledFeederAssetName(name, useBeta, is64Bit)),
            StringComparer.OrdinalIgnoreCase);
        if (bundled.Values.All(File.Exists))
        {
            foreach (var pair in bundled)
            {
                ValidateComponent(pair.Key, pair.Value);
                var destination = localFiles[pair.Key];
                CopyAtomically(pair.Value, destination);
            }
            var bundledVersion = useBeta ? BundledFeederBetaVersion : BundledFeederVersion;
            File.WriteAllText(localVersionPath, bundledVersion);
            return new(bundledVersion, localFiles);
        }

        if (useBeta)
            throw new FileNotFoundException(
                $"Adas is missing its packaged DLSS5-Feeder {BundledFeederBetaVersion} test-build payload. Repair or reinstall Adas; beta files are never mixed with the stable Feeder.",
                bundled.Values.FirstOrDefault(path => !File.Exists(path)));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        request.Headers.Add("Accept", "application/vnd.github+json");
        request.Headers.Add("User-Agent", "Adas-RHI");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var version = document.RootElement.GetProperty("tag_name").GetString() ?? "unknown";
        var assets = document.RootElement.GetProperty("assets").EnumerateArray()
            .Select(asset => new
            {
                Name = asset.GetProperty("name").GetString() ?? "",
                Url = asset.GetProperty("browser_download_url").GetString() ?? "",
            }).ToArray();

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requiredName in required)
        {
            var direct = assets.FirstOrDefault(asset => asset.Name.Equals(requiredName, StringComparison.OrdinalIgnoreCase));
            if (direct != null)
            {
                var destination = Path.Combine(staging, requiredName);
                await DownloadFileAsync(direct.Url, destination, cancellationToken).ConfigureAwait(false);
                resolved[requiredName] = destination;
            }
        }

        if (resolved.Count != required.Length)
        {
            foreach (var archive in assets.Where(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                if (resolved.Count == required.Length) break;
                var archivePath = Path.Combine(staging, $"download-{Guid.NewGuid():N}.zip");
                await DownloadFileAsync(archive.Url, archivePath, cancellationToken).ConfigureAwait(false);
                try
                {
                    using var zip = ZipFile.OpenRead(archivePath);
                    foreach (var requiredName in required.Where(name => !resolved.ContainsKey(name)))
                    {
                        var entry = zip.Entries.FirstOrDefault(value => value.Name.Equals(requiredName, StringComparison.OrdinalIgnoreCase));
                        if (entry == null) continue;
                        var maximumLength = requiredName.Equals(FeederShader, StringComparison.OrdinalIgnoreCase)
                            ? 10L * 1024 * 1024
                            : MaxArchiveEntryBytes;
                        if (entry.Length <= 0 || entry.Length > maximumLength)
                            throw new InvalidOperationException($"Release entry {entry.FullName} has an unsafe size ({entry.Length:N0} bytes).");
                        var destination = Path.Combine(staging, requiredName);
                        entry.ExtractToFile(destination, overwrite: true);
                        resolved[requiredName] = destination;
                    }
                }
                finally { DeleteIfExists(archivePath); }
            }
        }

        var missing = required.Where(name => !resolved.ContainsKey(name)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Release {version} from {repo} is missing: {string.Join(", ", missing)}");

        foreach (var pair in resolved)
            ValidateComponent(pair.Key, pair.Value);

        File.WriteAllText(Path.Combine(staging, "version.txt"), version);
        return new(version, resolved);
    }

    private async Task DownloadFileAsync(string url, string destination, CancellationToken cancellationToken)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
                throw new InvalidOperationException($"Release asset is larger than the {MaxDownloadBytes / 1024 / 1024} MB safety limit.");
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[128 * 1024];
                long total = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    total += read;
                    if (total > MaxDownloadBytes)
                        throw new InvalidOperationException($"Release asset exceeded the {MaxDownloadBytes / 1024 / 1024} MB safety limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally { DeleteIfExists(temporary); }
    }

    private async Task<IReadOnlyList<string>> InstallDgVoodooAsync(
        string root,
        bool is64Bit,
        Dlss5InstallRecord record,
        CancellationToken cancellationToken,
        bool directX8 = false)
    {
        if (directX8 && is64Bit) throw new InvalidOperationException("DirectX 8 translation requires a 32-bit game.");
        var wrapperName = directX8 ? "D3D8.dll" : "D3D9.dll";
        var wrapperTarget = Path.Combine(root, wrapperName.ToLowerInvariant());
        var wrapperIsOwned = record.InstalledHashes.ContainsKey(wrapperTarget);
        var wrapperIsDgVoodoo = File.Exists(wrapperTarget)
            && (FileVersionInfo.GetVersionInfo(wrapperTarget).FileDescription?.Contains("dgVoodoo", StringComparison.OrdinalIgnoreCase) ?? false);
        var wrapperIsReShade = File.Exists(wrapperTarget) && AuxInstallService.IsReShadeFileStrict(wrapperTarget);
        if (File.Exists(wrapperTarget) && !wrapperIsOwned && !wrapperIsDgVoodoo && !wrapperIsReShade)
            throw new InvalidOperationException($"{wrapperName} belongs to an existing wrapper. Remove it through its installer before adding dgVoodoo2.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{DgVoodooRepo}/releases/latest");
        request.Headers.Add("Accept", "application/vnd.github+json");
        request.Headers.Add("User-Agent", "Adas-RHI");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var version = document.RootElement.GetProperty("tag_name").GetString() ?? "unknown";
        var asset = document.RootElement.GetProperty("assets").EnumerateArray()
            .Select(value => new
            {
                Name = value.GetProperty("name").GetString() ?? "",
                Url = value.GetProperty("browser_download_url").GetString() ?? "",
            })
            .FirstOrDefault(value => value.Name.StartsWith("dgVoodoo2_", StringComparison.OrdinalIgnoreCase)
                                     && value.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                     && !value.Name.Contains("_dbg", StringComparison.OrdinalIgnoreCase)
                                     && !value.Name.Contains("_dev", StringComparison.OrdinalIgnoreCase));
        if (asset == null)
            throw new InvalidOperationException($"The latest dgVoodoo2 release ({version}) has no standard binary package.");

        var safeVersion = string.Concat(version.Where(value => char.IsLetterOrDigit(value) || value is '.' or '-' or '_'));
        var stagingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "Adas", "DLSS5", "dgVoodoo2", safeVersion);
        var archivePath = Path.Combine(stagingRoot, asset.Name);
        var extractedPath = Path.Combine(stagingRoot, "extracted");
        Directory.CreateDirectory(stagingRoot);
        if (!File.Exists(archivePath))
            await DownloadFileAsync(asset.Url, archivePath, cancellationToken).ConfigureAwait(false);
        if (!Directory.Exists(extractedPath))
        {
            Directory.CreateDirectory(extractedPath);
            ExtractArchiveSafely(archivePath, extractedPath, packageLabel: "dgVoodoo2");
        }

        var architecture = is64Bit ? "x64" : "x86";
        var d3d9 = Directory.EnumerateFiles(extractedPath, wrapperName, SearchOption.AllDirectories)
            .FirstOrDefault(path => Path.GetDirectoryName(path)?.EndsWith($"MS{Path.DirectorySeparatorChar}{architecture}", StringComparison.OrdinalIgnoreCase) == true);
        var controlPanel = Directory.EnumerateFiles(extractedPath, "dgVoodooCpl.exe", SearchOption.AllDirectories).FirstOrDefault();
        var sourceConfig = Directory.EnumerateFiles(extractedPath, "dgVoodoo.conf", SearchOption.AllDirectories).FirstOrDefault();
        if (d3d9 == null || controlPanel == null || sourceConfig == null)
            throw new InvalidDataException($"dgVoodoo2 {version} is missing its {architecture} {wrapperName} wrapper, configuration, or control panel.");

        ValidatePortableExecutable(d3d9, 64 * 1024, $"dgVoodoo2 {architecture} {wrapperName}", is64Bit ? (ushort)0x8664 : (ushort)0x014c);
        ValidatePortableExecutable(controlPanel, 64 * 1024, "dgVoodoo2 control panel");

        var installed = new List<string>();
        var d3d9Destination = wrapperTarget;
        var controlPanelDestination = Path.Combine(root, "dgVoodooCpl.exe");
        if (wrapperIsReShade)
        {
            if (!RelocateLegacyReShadeProxy(root, wrapperTarget, record))
                throw new IOException($"{wrapperName} changed while Adas was preparing dgVoodoo2. Close the game and retry.");
            _crashReporter.Log($"[Dlss5ComponentService] Relocated ReShade from {wrapperName} to dxgi.dll before installing dgVoodoo2.");
        }
        InstallTrackedFile(d3d9, d3d9Destination, root, record);
        InstallTrackedFile(controlPanel, controlPanelDestination, root, record);
        installed.Add(d3d9Destination);
        installed.Add(controlPanelDestination);

        var configured = IniTextDocument.Load(File.Exists(Path.Combine(root, "dgVoodoo.conf"))
            ? Path.Combine(root, "dgVoodoo.conf")
            : sourceConfig);
        configured.SetValue("General", "OutputAPI", "d3d11_fl11_0");
        configured.SetValue("DirectX", "DisableAndPassThru", "false");
        configured.SetValue("DirectX", "VRAM", "1024");
        configured.SetValue("DirectX", "VideoCard", "internal3D");
        configured.SetValue("DirectX", "dgVoodooWatermark", "false");
        var temporaryConfig = Path.Combine(Path.GetTempPath(), $"adas-dgvoodoo-{Guid.NewGuid():N}.conf");
        try
        {
            configured.Save(temporaryConfig);
            var configDestination = Path.Combine(root, "dgVoodoo.conf");
            InstallTrackedFile(temporaryConfig, configDestination, root, record);
            installed.Add(configDestination);
        }
        finally { DeleteIfExists(temporaryConfig); }

        record.ComponentVersion += $"; dgVoodoo2 {version}";
        SaveRecord(root, record);
        return installed;
    }

    internal static bool RelocateLegacyReShadeProxy(
        string root,
        string wrapperTarget,
        Dlss5InstallRecord record)
    {
        if (!File.Exists(wrapperTarget) || !AuxInstallService.IsReShadeFileStrict(wrapperTarget))
            return false;

        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        wrapperTarget = Path.GetFullPath(wrapperTarget);
        var reShadeTarget = Path.Combine(root, "dxgi.dll");
        EnsureNoReparsePoints(root, wrapperTarget);
        EnsureNoReparsePoints(root, reShadeTarget);

        if (File.Exists(reShadeTarget))
        {
            if (!AuxInstallService.IsReShadeFileStrict(reShadeTarget))
                throw new InvalidOperationException("dxgi.dll belongs to another graphics wrapper. Remove it through its installer before Adas relocates ReShade for legacy DirectX translation.");
            PreserveModifiedFile(root, wrapperTarget);
        }
        else
        {
            MoveFileOrDirectory(wrapperTarget, reShadeTarget);
        }

        // ReShade was an independent pre-existing component, not a game original
        // that the DLSS suite should restore into the translator slot on uninstall.
        record.OriginalBackups[wrapperTarget] = null;
        return true;
    }

    private async Task<IReadOnlyList<string>> InstallDxvkTranslationAsync(
        string gameName,
        string root,
        bool is64Bit,
        GraphicsApiType sourceApi,
        Dlss5InstallRecord record,
        IProgress<(string message, double percent)>? progress,
        CancellationToken cancellationToken)
    {
        if (_dxvkService == null || _reShadeUpdateService == null)
            throw new InvalidOperationException("The DXVK compatibility services are unavailable in this build of Adas.");

        // DXVK presents through Vulkan, so a game-local DirectX ReShade proxy must
        // leave the chain. Keep the shaders and settings; only remove the tracked DLL.
        var directReShade = _auxInstallService.FindRecord(gameName, root, AuxInstallService.TypeReShade)
            ?? _auxInstallService.FindRecord(gameName, root, AuxInstallService.TypeReShadeNormal);
        if (directReShade != null && directReShade.InstalledAs.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            _auxInstallService.UninstallDllOnly(directReShade);
            _crashReporter.Log($"[Dlss5ComponentService] Removed the tracked local ReShade proxy before switching '{gameName}' to Vulkan presentation.");
        }

        progress?.Report(("Preparing current ReShade for the Vulkan path...", 12));
        EnsureBundledReShadeStaging(AuxInstallService.ChannelStable);
        if (!AuxInstallService.EnsureReShadeStaging())
            await _reShadeUpdateService.EnsureLatestAsync(progress).ConfigureAwait(false);
        if (!AuxInstallService.EnsureReShadeStaging())
            throw new FileNotFoundException("The current 32-bit and 64-bit ReShade add-on runtimes could not be staged.", AuxInstallService.RsStagedPath64);

        if (!VulkanLayerService.IsLayerInstalled(require32Bit: !is64Bit))
        {
            if (!VulkanLayerService.IsRunningAsAdmin())
                throw new UnauthorizedAccessException(
                    $"{GraphicsApiDetector.GetLabel(sourceApi)} needs the Vulkan ReShade layer. Restart Adas as administrator, then run Review / Repair again; no game files were changed.");
            VulkanLayerService.InstallLayer();
        }

        var previousVariant = _dxvkService.SelectedVariant;
        string? stableDxvkVersion;
        try
        {
            _dxvkService.SelectedVariant = DxvkVariant.Stable;
            await _dxvkService.EnsureStagingAsync(progress).ConfigureAwait(false);
            if (!_dxvkService.IsStagingReady)
                throw new InvalidOperationException("The current stable DXVK package could not be staged.");
            stableDxvkVersion = _dxvkService.StagedVersion;
        }
        finally
        {
            _dxvkService.SelectedVariant = previousVariant;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var dxvkRoot = DxvkService.GetStagingDirForVariant(DxvkVariant.Stable);
        var architecture = is64Bit ? "x64" : "x32";
        var installed = new List<string>();
        var translationDlls = sourceApi == GraphicsApiType.DirectX9
            ? new[] { "d3d9.dll" }
            : new[] { "d3d10core.dll", "dxgi.dll" };
        foreach (var name in translationDlls)
        {
            var source = Path.Combine(dxvkRoot, architecture, name);
            ValidatePortableExecutable(source, 64 * 1024, $"DXVK {name}", is64Bit ? (ushort)0x8664 : (ushort)0x014c);
            var destination = Path.Combine(root, name);
            InstallTrackedFile(source, destination, root, record);
            installed.Add(destination);
        }

        AuxInstallService.EnsureInisDir();
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"adas-dx10-reshade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var existingIni = Path.Combine(root, "ReShade.ini");
            if (File.Exists(existingIni))
                File.Copy(existingIni, Path.Combine(temporaryDirectory, "reshade.ini"), overwrite: true);
            AuxInstallService.MergeRsVulkanIni(temporaryDirectory, gameName);
            var generatedIni = Path.Combine(temporaryDirectory, "reshade.ini");
            InstallTrackedFile(generatedIni, existingIni, root, record);
            installed.Add(existingIni);
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); } catch { }
        }

        var footprintSource = Path.Combine(Path.GetTempPath(), $"adas-vulkan-footprint-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(footprintSource, VulkanFootprintService.FootprintContent);
            var footprintDestination = Path.Combine(root, VulkanFootprintService.FootprintFileName);
            InstallTrackedFile(footprintSource, footprintDestination, root, record);
            installed.Add(footprintDestination);
        }
        finally { DeleteIfExists(footprintSource); }

        record.ComponentVersion += $"; DXVK {stableDxvkVersion ?? "stable"}";
        SaveRecord(root, record);
        return installed;
    }

    private async Task<IReadOnlyList<StagedProviderFile>> EnsureMotionProviderStagedAsync(
        CancellationToken cancellationToken)
    {
        var stagingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "Adas", "DLSS5", "LumeniteFX");
        var current = Path.Combine(stagingRoot, "current");
        Directory.CreateDirectory(stagingRoot);

        var archivePath = Path.Combine(stagingRoot, $"lumenite-{Guid.NewGuid():N}.zip");
        var extractionPath = Path.Combine(stagingRoot, $"extract-{Guid.NewGuid():N}");
        try
        {
            await DownloadFileAsync(LumeniteArchiveUrl, archivePath, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(extractionPath);
            ExtractArchiveSafely(archivePath, extractionPath, packageLabel: "LumeniteFX");
            var candidate = Directory.EnumerateDirectories(extractionPath).SingleOrDefault()
                ?? extractionPath;
            ValidateLumeniteProvider(candidate);

            if (Directory.Exists(current))
                Directory.Delete(current, recursive: true);
            Directory.Move(candidate, current);
        }
        catch (Exception ex) when (Directory.Exists(current) && IsValidLumeniteProvider(current))
        {
            _crashReporter.Log($"[Dlss5ComponentService] Could not refresh LumeniteFX; using the last validated official download: {ex.Message}");
        }
        finally
        {
            DeleteIfExists(archivePath);
            if (Directory.Exists(extractionPath))
            {
                try { Directory.Delete(extractionPath, recursive: true); } catch { }
            }
        }

        ValidateLumeniteProvider(current);
        var shaderRoot = Path.Combine(current, "Shaders");
        var textureRoot = Path.Combine(current, "Textures");
        var files = Directory.EnumerateFiles(shaderRoot, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file).Equals(".fx", StringComparison.OrdinalIgnoreCase)
                           || Path.GetExtension(file).Equals(".fxh", StringComparison.OrdinalIgnoreCase))
            .Select(file => new StagedProviderFile(
                file,
                Path.Combine("reshade-shaders", "Shaders", Path.GetRelativePath(shaderRoot, file))))
            .ToList();
        files.AddRange(Directory.EnumerateFiles(textureRoot, "*", SearchOption.AllDirectories)
            .Select(file => new StagedProviderFile(
                file,
                Path.Combine("reshade-shaders", "Textures", Path.GetRelativePath(textureRoot, file)))));
        return files;
    }

    private static bool IsValidLumeniteProvider(string root)
    {
        try { ValidateLumeniteProvider(root); return true; }
        catch { return false; }
    }

    private static void ValidateLumeniteProvider(string root)
    {
        var kernel = Path.Combine(root, "Shaders", "lumenite_Kernel.fx");
        var include = Path.Combine(root, "Shaders", "include", "lumenite_Compute.fxh");
        var texture = Path.Combine(root, "Textures", "lumenite_bluenoise256.png");
        if (!File.Exists(kernel) || !File.Exists(include) || !File.Exists(texture))
            throw new InvalidDataException("The official LumeniteFX package is missing Kernel shader dependencies.");
        var source = File.ReadAllText(kernel);
        if (source.Length < 1_000
            || !source.Contains("technique Lumenite_Kernel", StringComparison.Ordinal)
            || !source.Contains("ui_label = \"LUMENITE: Kernel", StringComparison.Ordinal))
            throw new InvalidDataException("The downloaded LumeniteFX Kernel shader did not match the expected interface.");
    }

    internal static string EnsureFeederPreset(string root, Dlss5InstallRecord record)
    {
        var reShadeIniPath = Path.Combine(root, "ReShade.ini");
        var presetPath = Path.Combine(root, "ReShadePreset.ini");
        if (File.Exists(reShadeIniPath))
        {
            var reShadeIni = IniTextDocument.Load(reShadeIniPath);
            if (reShadeIni.TryGetValue("GENERAL", "PresetPath", out var configuredPreset))
            {
                var unquoted = configuredPreset.Text.Trim().Trim('"');
                var candidate = Path.IsPathFullyQualified(unquoted)
                    ? Path.GetFullPath(unquoted)
                    : Path.GetFullPath(Path.Combine(root, unquoted));
                if (IsPathBelow(root, candidate))
                    presetPath = candidate;
                else
                    SetTrackedIniValue(root, record, reShadeIniPath, "GENERAL", "PresetPath", @".\ReShadePreset.ini");
            }
            else
            {
                SetTrackedIniValue(root, record, reShadeIniPath, "GENERAL", "PresetPath", @".\ReShadePreset.ini");
            }
        }

        var preset = IniTextDocument.Load(presetPath);
        preset.TryGetValue("", "Techniques", out var techniques);
        preset.SetValue("", "Techniques", PutTechniquesFirst(
            techniques?.Text ?? "",
            disableDrme: true));
        preset.TryGetValue("", "TechniqueSorting", out var sorting);
        preset.SetValue("", "TechniqueSorting", PutTechniquesFirst(sorting?.Text ?? ""));

        var hasDefinitions = preset.TryGetValue(FeederShader, "PreprocessorDefinitions", out var existingDefinitions);
        var definitions = hasDefinitions
            ? existingDefinitions.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !value.StartsWith("DLSS5_MV_PROVIDER=", StringComparison.OrdinalIgnoreCase))
                .ToList()
            : new List<string>();
        definitions.Insert(0, "DLSS5_MV_PROVIDER=3");
        preset.SetValue(FeederShader, "PreprocessorDefinitions", string.Join(',', definitions));

        var temporary = Path.Combine(Path.GetTempPath(), $"adas-dlss5-preset-{Guid.NewGuid():N}.ini");
        try
        {
            preset.Save(temporary);
            InstallTrackedFile(temporary, presetPath, root, record);
        }
        finally { DeleteIfExists(temporary); }
        return presetPath;
    }

    internal static void EnsureUnifiedRenoDxSettings(string root, Dlss5InstallRecord record)
    {
        var reShadeIniPath = Path.Combine(root, "ReShade.ini");
        var ini = IniTextDocument.Load(reShadeIniPath);

        if (!record.UnifiedRenoDxSettingsMigrated)
        {
            if (ini.TryGetValue("RenoDX.DLSS5", "NRStyle", out var legacyStyleText)
                && int.TryParse(legacyStyleText.Text, out var legacyStyle)
                && legacyStyle is 0 or 1)
            {
                // The old add-on called these values Natural and Cinematic. The
                // unified add-on exposes the same DLSSNR.Style values as Model A/B.
                SetTrackedIniValue(root, record, reShadeIniPath, "RENODX-DLSS", "DirectNeuralRenderingStyle", legacyStyle.ToString());
            }
            else if (!ini.TryGetValue("RENODX-DLSS", "DirectNeuralRenderingStyle", out _))
            {
                SetTrackedIniValue(root, record, reShadeIniPath, "RENODX-DLSS", "DirectNeuralRenderingStyle", "0");
            }

            record.UnifiedRenoDxSettingsMigrated = true;
            SaveRecord(root, record);
        }

        SetTrackedIniValue(root, record, reShadeIniPath, "RENODX-DLSS", "DirectNeuralRenderingEnabled", "1");
        SetTrackedIniValue(root, record, reShadeIniPath, "RENODX-DLSS", "OptionsMode", "0");
    }

    internal static void EnsureStableRenoDxSettings(string root, Dlss5InstallRecord record)
    {
        var reShadeIniPath = Path.Combine(root, "ReShade.ini");
        var ini = IniTextDocument.Load(reShadeIniPath);
        if (!ini.TryGetValue("RenoDX.DLSS5", "NRStyle", out _))
            SetTrackedIniValue(root, record, reShadeIniPath, "RenoDX.DLSS5", "NRStyle", "0");
        SetTrackedIniValue(root, record, reShadeIniPath, "RenoDX.DLSS5", "NeuralUplift", "1");
        if (!ini.TryGetValue("RenoDX.DLSS5", "NREnableUpscaling", out _))
            SetTrackedIniValue(root, record, reShadeIniPath, "RenoDX.DLSS5", "NREnableUpscaling", "0");
        if (!ini.TryGetValue("RenoDX.DLSS5", "EnableHooks", out _))
            SetTrackedIniValue(root, record, reShadeIniPath, "RenoDX.DLSS5", "EnableHooks", "2");
    }

    internal static void SaveRenoDxUserSettings(
        string root,
        Dlss5InstallProfile profile,
        bool enabled,
        int style)
    {
        if (style is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(style));
        if (profile is not (Dlss5InstallProfile.MaximumQuality or Dlss5InstallProfile.ExperimentalUnified or Dlss5InstallProfile.OpenGlBridge))
            throw new ArgumentException("This profile does not use RenoDX's native settings.", nameof(profile));

        var iniPath = Path.Combine(Path.GetFullPath(root), "ReShade.ini");
        var ini = IniTextDocument.Load(iniPath);
        if (profile == Dlss5InstallProfile.ExperimentalUnified)
        {
            ini.SetValue("RENODX-DLSS", "DirectNeuralRenderingEnabled", enabled ? "1" : "0");
            ini.SetValue("RENODX-DLSS", "DirectNeuralRenderingStyle", style.ToString());
            ini.SetValue("RENODX-DLSS", "OptionsMode", "0");
        }
        else
        {
            if (style > 1)
                throw new ArgumentOutOfRangeException(nameof(style), "The stable RenoDX build supports Natural and Cinematic only.");
            ini.SetValue("RenoDX.DLSS5", "NeuralUplift", enabled ? "1" : "0");
            ini.SetValue("RenoDX.DLSS5", "NRStyle", style.ToString());
        }
        ini.Save(iniPath);
    }

    internal static void EnsureNativeEarlyLoadSettings(
        string root,
        Dlss5CompatibilityPlan compatibilityPlan,
        Dlss5InstallRecord record,
        bool force = false)
    {
        if (!force && !File.Exists(Path.Combine(root, "sl.interposer.dll"))) return;
        var reShadeIniPath = Path.Combine(root, "ReShade.ini");
        var ini = IniTextDocument.Load(reShadeIniPath);
        ini.TryGetValue("ADDON", "LoadFromDllMain", out var existing);
        var values = (existing?.Text ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var renoDxName = compatibilityPlan.UsesExperimentalUnified
            ? Renodx5AddonService.AddonFileName
            : RenoDxDeploymentName;
        if (!values.Contains(renoDxName, StringComparer.OrdinalIgnoreCase))
            values.Add(renoDxName);
        if (compatibilityPlan.InstallDx11Bridge
            && !values.Contains(BridgeAddon, StringComparer.OrdinalIgnoreCase))
            values.Add(BridgeAddon);
        SetTrackedIniValue(root, record, reShadeIniPath, "ADDON", "LoadFromDllMain", string.Join(',', values));
    }

    internal static bool RequiresEarlyLoadSettings(
        Dlss5Assessment assessment,
        Dlss5CompatibilityPlan compatibilityPlan)
        => assessment.Is64Bit
            && (compatibilityPlan.UsesExperimentalUnified
                || assessment.Mode is Dlss5DeploymentMode.NativeDirectX11
                    or Dlss5DeploymentMode.NativeDirectX12
                    or Dlss5DeploymentMode.NativeVulkan);

    internal static void RepairReShadeConfiguration(string root, Dlss5InstallRecord record)
    {
        var path = Path.Combine(root, "ReShade.ini");
        if (!File.Exists(path)) return;

        var document = IniTextDocument.Load(path);
        document.TryGetValue("GENERAL", "EffectSearchPaths", out var effects);
        document.TryGetValue("GENERAL", "TextureSearchPaths", out var textures);
        SetTrackedIniValue(
            root,
            record,
            path,
            "GENERAL",
            "EffectSearchPaths",
            NormalizeReShadeSearchPaths(effects?.Text, @".\reshade-shaders\Shaders\**"));
        SetTrackedIniValue(
            root,
            record,
            path,
            "GENERAL",
            "TextureSearchPaths",
            NormalizeReShadeSearchPaths(textures?.Text, @".\reshade-shaders\Textures\**"));
        RepairReShadeAddonState(root);
    }

    /// <summary>
    /// ReShade can persist a suite add-on in DisabledAddons after a crash. Remove
    /// only Adas DLSS entries, and prune early-load entries whose files no longer
    /// exist. Unrelated disabled add-ons and early-load entries are preserved.
    /// </summary>
    internal static IReadOnlyList<string> RepairReShadeAddonState(string settingsRoot)
    {
        var path = Path.Combine(settingsRoot, "ReShade.ini");
        if (!File.Exists(path)) return Array.Empty<string>();

        var removed = new List<string>();
        var document = IniTextDocument.Load(path);
        if (document.TryGetValue("ADDON", "DisabledAddons", out var disabled))
        {
            var values = SplitIniList(disabled.Text);
            var kept = values.Where(value => !IsManagedDlssAddonReference(value)).ToArray();
            removed.AddRange(values.Except(kept, StringComparer.OrdinalIgnoreCase));
            if (kept.Length != values.Length)
                document.SetValue("ADDON", "DisabledAddons", string.Join(',', kept));
        }

        if (document.TryGetValue("ADDON", "LoadFromDllMain", out var earlyLoad))
        {
            var values = SplitIniList(earlyLoad.Text);
            var kept = values.Where(value => !IsManagedDlssAddonReference(value)
                || ManagedAddonReferenceExists(settingsRoot, value)).ToArray();
            removed.AddRange(values.Except(kept, StringComparer.OrdinalIgnoreCase));
            if (kept.Length != values.Length)
            {
                if (kept.Length == 0) document.RemoveValue("ADDON", "LoadFromDllMain");
                else document.SetValue("ADDON", "LoadFromDllMain", string.Join(',', kept));
            }
        }

        if (removed.Count > 0) document.Save(path);
        return removed.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] SplitIniList(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsManagedDlssAddonReference(string value)
    {
        var fileName = GetAddonReferenceFileName(value);
        return fileName.StartsWith("renodx-dlss5", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("renodx-dlss.addon", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("dlss5-feed", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("dlss5-bridge", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("dlss5-opengl-bridge", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("dlss5-dx11-bridge", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("DLSS 5 Neural Rendering", StringComparison.OrdinalIgnoreCase)
            || value.Trim().StartsWith("DLSS 5 Feed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ManagedAddonReferenceExists(string settingsRoot, string value)
    {
        var fileName = GetAddonReferenceFileName(value);
        if (!fileName.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase))
            return false;
        return File.Exists(Path.Combine(settingsRoot, fileName))
            || File.Exists(Path.Combine(ModInstallService.GetAddonDeployPath(settingsRoot), fileName));
    }

    private static string GetAddonReferenceFileName(string value)
    {
        var separator = value.LastIndexOf('@');
        var candidate = (separator >= 0 ? value[(separator + 1)..] : value).Trim();
        var slash = Math.Max(candidate.LastIndexOf('/'), candidate.LastIndexOf('\\'));
        return slash >= 0 ? candidate[(slash + 1)..] : candidate;
    }

    internal static string NormalizeReShadeSearchPaths(string? value, string requiredPath)
    {
        var paths = (value ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CollapseRepeatedRecursiveWildcards)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!paths.Contains(requiredPath, StringComparer.OrdinalIgnoreCase))
            paths.Add(requiredPath);
        return string.Join(',', paths);
    }

    private static string CollapseRepeatedRecursiveWildcards(string path)
    {
        string previous;
        do
        {
            previous = path;
            path = path.Replace(@"\**\**", @"\**", StringComparison.Ordinal)
                .Replace("/**/**", "/**", StringComparison.Ordinal)
                .Replace(@"\**/**", @"\**", StringComparison.Ordinal)
                .Replace(@"/**\**", "/**", StringComparison.Ordinal);
        } while (!path.Equals(previous, StringComparison.Ordinal));
        return path;
    }

    private static void SetTrackedIniValue(
        string root,
        Dlss5InstallRecord record,
        string path,
        string section,
        string key,
        string value)
    {
        var document = IniTextDocument.Load(path);
        var backup = record.IniSettingBackups.FirstOrDefault(item =>
            item.Path.Equals(path, StringComparison.OrdinalIgnoreCase)
            && item.Section.Equals(section, StringComparison.OrdinalIgnoreCase)
            && item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (backup == null)
        {
            var existed = document.TryGetValue(section, key, out var original);
            backup = new Dlss5IniSettingBackup
            {
                Path = Path.GetFullPath(path),
                Section = section,
                Key = key,
                Existed = existed,
                OriginalKey = existed ? original.Key : null,
                OriginalValue = existed ? original.Text : null,
                InstalledValue = value,
            };
            record.IniSettingBackups.Add(backup);
        }
        else
        {
            var currentExists = document.TryGetValue(section, key, out var current);
            if (!currentExists || !current.Text.Equals(backup.InstalledValue, StringComparison.Ordinal))
            {
                // A user changed this setting after Adas installed it. Treat that
                // newer value as the state to restore after this repair.
                backup.Existed = currentExists;
                backup.OriginalKey = currentExists ? current.Key : null;
                backup.OriginalValue = currentExists ? current.Text : null;
            }
            backup.InstalledValue = value;
        }

        // Persist ownership before touching the user's setting so a terminated
        // repair can still be reversed safely.
        SaveRecord(root, record);
        document.SetValue(section, key, value);
        document.Save(path);
    }

    private static string PutTechniquesFirst(string value, bool disableDrme = false)
    {
        const string provider = "Lumenite_Kernel@lumenite_Kernel.fx";
        const string feeder = "DLSS5_Feed@DLSS5_Feed.fx";
        var remaining = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !item.Equals(provider, StringComparison.OrdinalIgnoreCase)
                           && !item.Equals(feeder, StringComparison.OrdinalIgnoreCase)
                           && (!disableDrme || !IsDrmeTechnique(item)));
        return string.Join(',', new[] { provider, feeder }.Concat(remaining));
    }

    private static bool IsDrmeTechnique(string item)
    {
        var separator = item.IndexOf('@');
        var technique = separator >= 0 ? item[..separator] : item;
        return technique.Equals("DRME", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> InstallVulkanFallbackLayer(
        string root,
        string archivePath,
        Dlss5InstallRecord record)
    {
        var extracted = Path.Combine(Path.GetTempPath(), $"adas-feed-vk-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(extracted);
            ExtractArchiveSafely(archivePath, extracted, packageLabel: "DLSS5-Feeder Vulkan fallback");
            var installed = new List<string>();
            foreach (var source in Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(root, "DLSS5-Vulkan-Fallback", Path.GetRelativePath(extracted, source));
                InstallTrackedFile(source, destination, root, record);
                installed.Add(destination);
            }
            return installed;
        }
        finally
        {
            if (Directory.Exists(extracted))
            {
                try { Directory.Delete(extracted, recursive: true); } catch { }
            }
        }
    }

    internal static bool ShouldRemainPostInstallWarning(
        Dlss5DeploymentMode mode,
        string deploymentPath,
        string requirement)
    {
        if (requirement.StartsWith("ReShade", StringComparison.OrdinalIgnoreCase)
            || requirement.StartsWith("RenoDX", StringComparison.OrdinalIgnoreCase)
            || requirement.StartsWith("dgVoodoo2", StringComparison.OrdinalIgnoreCase)
            || requirement.StartsWith("DXVK", StringComparison.OrdinalIgnoreCase))
            return false;

        if (IsFeederMode(mode)
            && requirement.Contains("motion-vector provider", StringComparison.OrdinalIgnoreCase))
            return false;

        return !RuntimeRequirementIsNowSatisfied(
            deploymentPath,
            requirement,
            includeHostedRuntime: IsFeederMode(mode));
    }

    internal static void InstallHostedFeederFiles(
        string root,
        IReadOnlyDictionary<string, string> stagedFiles,
        string reshadeRuntimePath,
        string renoDxPath,
        Dlss5InstallRecord record,
        ICollection<string> installed,
        ICollection<string> warnings)
        => InstallHostedFeederFiles(
            root,
            stagedFiles,
            reshadeRuntimePath,
            renoDxPath,
            new Dlss5CompatibilityPlan(
                Dlss5RenoDxPackage.ExperimentalUnified,
                InstallFeeder: true,
                InstallDx11Bridge: false,
                PatchFeederForUnifiedName: true,
                ProfileName: "Unified compatibility"),
            record,
            installed,
            warnings);

    internal static void InstallHostedFeederFiles(
        string root,
        IReadOnlyDictionary<string, string> stagedFiles,
        string reshadeRuntimePath,
        string renoDxPath,
        Dlss5CompatibilityPlan compatibilityPlan,
        Dlss5InstallRecord record,
        ICollection<string> installed,
        ICollection<string> warnings,
        DeepFriedChickenService? deepFriedChicken = null)
    {
        var hostDirectory = Path.Combine(root, "host64");
        var requiredRuntimeNames = new[] { "nvngx_dlssnr.dll", "nvngx_dlss.dll" };
        var missingRuntimeNames = requiredRuntimeNames
            .Where(name => !File.Exists(Path.Combine(hostDirectory, name))
                && !File.Exists(Path.Combine(root, name)))
            .ToArray();
        if (missingRuntimeNames.Length > 0)
            throw new FileNotFoundException(
                $"The 32-bit Feeder host requires {string.Join(" and ", missingRuntimeNames)}. " +
                "Import the local Streamline runtime package before installing or repairing this game.",
                Path.Combine(hostDirectory, missingRuntimeNames[0]));

        Directory.CreateDirectory(hostDirectory);

        void InstallHostFile(string source, string name)
        {
            var destination = Path.Combine(hostDirectory, name);
            InstallTrackedFile(source, destination, root, record);
            installed.Add(destination);
        }

        if (compatibilityPlan.PatchFeederForUnifiedName)
        {
            var patchedHost = Path.Combine(Path.GetTempPath(), $"adas-feed-host-{Guid.NewGuid():N}.exe");
            try
            {
                File.WriteAllBytes(patchedHost, PatchRenoDxAddonProbeName(File.ReadAllBytes(stagedFiles[FeederHost64])));
                InstallHostFile(patchedHost, FeederHost64);
            }
            finally { DeleteIfExists(patchedHost); }
        }
        else
        {
            InstallHostFile(stagedFiles[FeederHost64], FeederHost64);
        }
        if (!File.Exists(reshadeRuntimePath))
            throw new FileNotFoundException(
                "The 64-bit ReShade runtime needed by the 32-bit Feeder host is not staged. Import the full ReShade add-on installer and retry.",
                reshadeRuntimePath);
        InstallHostFile(reshadeRuntimePath, "dxgi.dll");
        if (deepFriedChicken is { IsImported: true })
        {
            // Deep Fried Chicken replaces the RenoDX consumer inside the Feeder host folder.
            foreach (var name in deepFriedChicken.DeployFiles(includeDx11Bridge: false))
                InstallHostFile(deepFriedChicken.CachedFile(name), name);
            foreach (var stale in new[] { RenoDxDeploymentName, Renodx5AddonService.AddonFileName })
            {
                var stalePath = Path.Combine(hostDirectory, stale);
                if (File.Exists(stalePath)) RetireComponentFiles(root, new[] { stalePath }, record);
            }
        }
        else
        {
            InstallHostFile(
                renoDxPath,
                compatibilityPlan.UsesExperimentalUnified
                    ? Renodx5AddonService.AddonFileName
                    : RenoDxDeploymentName);
            // RenoDX is the consumer: retire any Deep Fried Chicken files a prior DFC install
            // left in the host folder so the two never stack.
            foreach (var stale in DeepFriedChickenService.RequiredFiles)
            {
                var stalePath = Path.Combine(hostDirectory, stale);
                if (File.Exists(stalePath)) RetireComponentFiles(root, new[] { stalePath }, record);
            }
        }

        foreach (var runtimeName in HostedRuntimeNames)
        {
            var rootSource = Path.Combine(root, runtimeName);
            var hostSource = Path.Combine(hostDirectory, runtimeName);
            if (File.Exists(rootSource))
                InstallHostFile(rootSource, runtimeName);
            else if (!File.Exists(hostSource) && requiredRuntimeNames.Contains(runtimeName, StringComparer.OrdinalIgnoreCase))
                throw new FileNotFoundException($"The 32-bit Feeder host requires {runtimeName}.", hostSource);
        }

        RetireTrackedRootRuntimeFiles(root, hostDirectory, record, warnings);
    }

    private static void RetireTrackedRootRuntimeFiles(
        string root,
        string hostDirectory,
        Dlss5InstallRecord record,
        ICollection<string> warnings)
    {
        foreach (var runtimeName in HostedRuntimeNames)
        {
            var rootPath = Path.Combine(root, runtimeName);
            if (!record.InstalledHashes.TryGetValue(rootPath, out var installedHash)
                || !File.Exists(rootPath)
                || !File.Exists(Path.Combine(hostDirectory, runtimeName)))
                continue;

            if (!FileHelper.ComputeSha256(rootPath).Equals(installedHash, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Adas left the modified {runtimeName} in the game folder; only the original suite-owned copy can be moved safely to host64.");
                continue;
            }

            File.Delete(rootPath);
            if (record.OriginalBackups.TryGetValue(rootPath, out var backup) && backup != null)
            {
                File.Move(backup, rootPath);
            }
            record.InstalledHashes.Remove(rootPath);
            record.OriginalBackups.Remove(rootPath);
            SaveRecord(root, record);
        }
    }

    internal static IReadOnlyList<string> MigrateLegacyLaunchPad(string deploymentPath)
    {
        var record = LoadRecord(deploymentPath) ?? new Dlss5InstallRecord();
        return MigrateLegacyLaunchPad(deploymentPath, record, new List<string>());
    }

    private static IReadOnlyList<string> MigrateLegacyLaunchPad(
        string deploymentPath,
        Dlss5InstallRecord record,
        ICollection<string> migratedSources)
    {
        var shaderDirectory = Path.Combine(deploymentPath, "reshade-shaders", "Shaders");
        var legacyPaths = new[]
        {
            Path.Combine(shaderDirectory, "MartysMods_LAUNCHPAD.fx"),
            Path.Combine(shaderDirectory, "MartysMods"),
        };
        if (!legacyPaths.Any(path => File.Exists(path) || Directory.Exists(path)))
            return Array.Empty<string>();

        var backupRoot = Path.Combine(
            deploymentPath,
            ".adas",
            "legacy-launchpad-backup",
            $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupRoot);
        var preserved = new List<string>();
        foreach (var source in legacyPaths)
        {
            if (!File.Exists(source) && !Directory.Exists(source)) continue;
            if (record.LegacyLaunchPadBackups.ContainsKey(source))
                throw new InvalidDataException($"The ownership record already tracks a LaunchPad migration for {source}.");
            var destination = Path.Combine(backupRoot, Path.GetFileName(source));
            record.LegacyLaunchPadBackups[source] = destination;
            SaveRecord(deploymentPath, record);
            try
            {
                MoveFileOrDirectory(source, destination);
                migratedSources.Add(source);
                preserved.Add(destination);
            }
            catch
            {
                record.LegacyLaunchPadBackups.Remove(source);
                SaveRecord(deploymentPath, record);
                RollbackLegacyLaunchPad(deploymentPath, record, migratedSources);
                throw;
            }
        }
        return preserved;
    }

    internal static void RollbackLegacyLaunchPad(
        string deploymentPath,
        Dlss5InstallRecord record,
        IEnumerable<string> migratedSources)
    {
        foreach (var source in migratedSources.Reverse().ToArray())
        {
            if (!record.LegacyLaunchPadBackups.TryGetValue(source, out var backup)) continue;
            RestoreLegacyLaunchPadPath(source, backup);
            record.LegacyLaunchPadBackups.Remove(source);
            SaveRecord(deploymentPath, record);
        }
    }

    private static void RestoreLegacyLaunchPadPath(string source, string backup)
    {
        var sourceExists = File.Exists(source) || Directory.Exists(source);
        var backupExists = File.Exists(backup) || Directory.Exists(backup);
        if (sourceExists && backupExists)
            throw new IOException($"The original LaunchPad path is occupied and its backup still exists: {source}");
        if (!sourceExists && !backupExists)
            throw new FileNotFoundException("The tracked LaunchPad backup is missing.", backup);
        if (sourceExists) return;

        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        MoveFileOrDirectory(backup, source);
    }

    private static void MoveFileOrDirectory(string source, string destination)
    {
        Dlss5SwitchJournal.Current?.CaptureMove(source, destination);
        if (File.Exists(source)) File.Move(source, destination);
        else if (Directory.Exists(source)) Directory.Move(source, destination);
        else throw new FileNotFoundException("The tracked file or directory was not found.", source);
    }

    internal static void InstallTrackedFile(string source, string destination, string root, Dlss5InstallRecord record)
    {
        Dlss5SwitchJournal.Current?.Capture(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!record.OriginalBackups.ContainsKey(destination))
        {
            string? backup = null;
            if (File.Exists(destination))
            {
                var backupDirectory = Path.Combine(root, ".adas", "backups", "dlss5");
                Directory.CreateDirectory(backupDirectory);
                backup = Path.Combine(backupDirectory, $"{Path.GetFileName(destination)}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.bak");
                Dlss5SwitchJournal.Current?.Capture(backup);
                File.Copy(destination, backup, overwrite: false);
            }
            record.OriginalBackups[destination] = backup;
        }

        var sourceHash = FileHelper.ComputeSha256(source);
        record.InstalledHashes[destination] = sourceHash;
        SaveRecord(root, record);

        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(source, temporary, overwrite: false);
            File.Move(temporary, destination, overwrite: true);
            if (!FileHelper.ComputeSha256(destination).Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Post-install verification failed for {destination}.");
        }
        finally { DeleteIfExists(temporary); }
    }

    private static void InstallTrackedFeederBinary(
        string source,
        string destination,
        string root,
        Dlss5InstallRecord record,
        bool patchForUnifiedName)
    {
        if (!patchForUnifiedName)
        {
            InstallTrackedFile(source, destination, root, record);
            return;
        }

        var temporary = Path.Combine(Path.GetTempPath(), $"adas-feeder-{Guid.NewGuid():N}.addon64");
        try
        {
            File.WriteAllBytes(temporary, PatchRenoDxAddonProbeName(File.ReadAllBytes(source)));
            ValidatePortableExecutable(temporary, 64 * 1024, FeederAddon, expectedMachine: 0x8664);
            InstallTrackedFile(temporary, destination, root, record);
        }
        finally { DeleteIfExists(temporary); }
    }

    internal static byte[] PatchRenoDxAddonProbeName(byte[] bytes)
    {
        var oldName = System.Text.Encoding.ASCII.GetBytes("renodx-dlss5.addon64\0");
        var newName = System.Text.Encoding.ASCII.GetBytes("renodx-dlss.addon64\0");
        var match = -1;
        for (var index = 0; index <= bytes.Length - oldName.Length; index++)
        {
            if (!bytes.AsSpan(index, oldName.Length).SequenceEqual(oldName)) continue;
            if (match >= 0)
                throw new InvalidDataException("The Feeder contains multiple ambiguous RenoDX filename probes.");
            match = index;
        }
        if (match < 0)
            throw new InvalidDataException("The Feeder no longer contains the expected RenoDX filename probe; refusing an unsafe binary adaptation.");

        Array.Clear(bytes, match, oldName.Length);
        newName.CopyTo(bytes, match);
        return bytes;
    }

    internal static void RemoveIncompatibleDlssAddons(
        string root,
        string addonDeployPath,
        Dlss5CompatibilityPlan compatibilityPlan,
        Dlss5InstallRecord record,
        bool useDeepFriedChicken = false)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // With Deep Fried Chicken as the consumer, RenoDX must be removed (DFC replaces it), and
        // DFC's own files are deployed separately and kept. With RenoDX as the consumer the reverse
        // holds: RenoDX is kept and any Deep Fried Chicken files from a prior install are retired.
        if (!useDeepFriedChicken)
            keep.Add(compatibilityPlan.UsesExperimentalUnified
                ? Renodx5AddonService.AddonFileName
                : RenoDxDeploymentName);
        if (compatibilityPlan.InstallDx11Bridge)
            keep.Add(BridgeAddon);
        if (compatibilityPlan.InstallOpenGlBridge)
            keep.Add(OpenGlBridgeAddon);

        var managedNames = new[]
        {
            Renodx5AddonService.AddonFileName,
            RenoDxDeploymentName,
            "renodx-dlss5(2).addon64",
            BridgeAddon,
            OpenGlBridgeAddon,
            ObsoleteBridgeAddon,
        };
        // Deep Fried Chicken files are managed only when RenoDX is taking over as the consumer.
        var deepFriedChickenNames = useDeepFriedChicken
            ? Array.Empty<string>()
            : DeepFriedChickenService.RequiredFiles;
        var paths = managedNames.Concat(deepFriedChickenNames)
            .Where(name => !keep.Contains(name))
            .SelectMany(name => new[] { Path.Combine(addonDeployPath, name), Path.Combine(root, name) })
            .Concat(new[]
            {
                Path.Combine(root, ObsoleteBridgeConfig),
                Path.Combine(root, ObsoleteBridgeLog),
            })
            .Concat(compatibilityPlan.InstallDx11Bridge
                ? Array.Empty<string>()
                : new[] { Path.Combine(root, BridgeConfig), Path.Combine(root, BridgeLog) });
        RetireComponentFiles(root, paths, record);
    }

    private static void RemoveFeederComponent(
        string root,
        string addonDeployPath,
        Dlss5InstallRecord record)
    {
        var paths = new[]
        {
            Path.Combine(addonDeployPath, FeederAddon), Path.Combine(root, FeederAddon),
            Path.Combine(addonDeployPath, FeederAddon32), Path.Combine(root, FeederAddon32),
            Path.Combine(root, FeederConfig),
            Path.Combine(root, "reshade-shaders", "Shaders", FeederShader),
        };
        RetireComponentFiles(root, paths, record);
    }

    private static void RetireComponentFiles(
        string root,
        IEnumerable<string> paths,
        Dlss5InstallRecord record)
    {
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
        {
            Dlss5SwitchJournal.Current?.Capture(path);
            if (!record.OriginalBackups.ContainsKey(path))
            {
                var backupDirectory = Path.Combine(root, ".adas", "backups", "conflicts");
                Directory.CreateDirectory(backupDirectory);
                var backup = Path.Combine(backupDirectory, $"{Path.GetFileName(path)}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.bak");
                Dlss5SwitchJournal.Current?.Capture(backup);
                File.Move(path, backup);
                record.OriginalBackups[path] = backup;
            }
            else if (record.InstalledHashes.TryGetValue(path, out var installedHash)
                     && FileHelper.ComputeSha256(path).Equals(installedHash, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
            }
            else
            {
                PreserveModifiedFile(root, path);
            }
            record.InstalledHashes.Remove(path);
            SaveRecord(root, record);
        }
    }

    internal static void DisableObsolete32BitRenoDx(
        string root,
        string addonDeployPath,
        Dlss5InstallRecord record)
    {
        var obsoletePaths = new[]
        {
            Path.Combine(root, "renodx-dlss.addon32"),
            Path.Combine(addonDeployPath, "renodx-dlss.addon32"),
            Path.Combine(root, "renodx-dlss5.addon32"),
            Path.Combine(addonDeployPath, "renodx-dlss5.addon32"),
            Path.Combine(root, "dlss5-dx11-bridge.addon32"),
            Path.Combine(addonDeployPath, "dlss5-dx11-bridge.addon32"),
        };
        foreach (var path in obsoletePaths.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
        {
            if (record.OriginalBackups.ContainsKey(path))
            {
                PreserveModifiedFile(root, path);
                continue;
            }

            var backupDirectory = Path.Combine(root, ".adas", "backups", "obsolete-addons");
            Directory.CreateDirectory(backupDirectory);
            var backup = Path.Combine(
                backupDirectory,
                $"{Path.GetFileName(path)}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.bak");
            Dlss5SwitchJournal.Current?.CaptureMove(path, backup);
            File.Move(path, backup);
            record.OriginalBackups[path] = backup;
            SaveRecord(root, record);
        }
    }

    private static void EnsureTrackedConfig(
        string path,
        IReadOnlyDictionary<string, string> defaults,
        string root,
        Dlss5InstallRecord record)
    {
        if (File.Exists(path)) return;
        var temporary = Path.Combine(Path.GetTempPath(), $"adas-config-{Guid.NewGuid():N}.cfg");
        try
        {
            WriteConfig(temporary, defaults);
            InstallTrackedFile(temporary, path, root, record);
        }
        finally { DeleteIfExists(temporary); }
    }

    private static void ExtractArchiveSafely(
        string archivePath,
        string destinationRoot,
        long maxEntryBytes = MaxArchiveEntryBytes,
        long maxTotalBytes = MaxExtractedBytes,
        int maxEntries = MaxArchiveEntries,
        string packageLabel = "Archive")
    {
        var canonicalRoot = Path.GetFullPath(destinationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > maxEntries)
            throw new InvalidOperationException($"{packageLabel} archive contains more than {maxEntries:N0} entries.");

        long totalLength = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > maxEntryBytes)
                throw new InvalidOperationException($"Archive entry {entry.FullName} exceeds the per-file safety limit.");
            totalLength += entry.Length;
            if (totalLength > maxTotalBytes)
                throw new InvalidOperationException($"{packageLabel} archive expands beyond the {maxTotalBytes / 1024 / 1024} MB safety limit.");

            var destination = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!destination.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Archive entry escapes the extraction folder: {entry.FullName}");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: false);
        }
    }

    internal static Dlss5InstallRecord? LoadRecord(string deploymentPath)
    {
        var path = Path.Combine(deploymentPath, RecordRelativePath);
        if (!File.Exists(path)) return null;
        try
        {
            EnsureNoReparsePoints(Path.GetFullPath(deploymentPath), Path.GetFullPath(path));
            var record = JsonSerializer.Deserialize<Dlss5InstallRecord>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("The Adas install record was empty.");
            ValidateRecord(deploymentPath, record);
            return record;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException(
                $"The Adas ownership record is damaged. No files were changed: {path}", ex);
        }
    }

    internal static void SaveRecord(string deploymentPath, Dlss5InstallRecord record)
    {
        ValidateRecord(deploymentPath, record);
        var path = Path.Combine(deploymentPath, RecordRelativePath);
        Dlss5SwitchJournal.BeforeWrite(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(record, JsonOptions));
        File.Move(temporary, path, overwrite: true);
        DeploymentPathCache.Clear();
    }

    internal static IReadOnlyList<string> UninstallTrackedFiles(string deploymentPath, ICrashReporter crashReporter)
    {
        var record = LoadRecord(deploymentPath);
        if (record == null) return Array.Empty<string>();

        var errors = new List<string>();
        var trackedPaths = record.InstalledHashes.Keys
            .Concat(record.OriginalBackups.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var destination in trackedPaths)
        {
            try
            {
                if (record.InstalledHashes.TryGetValue(destination, out var installedHash)
                    && File.Exists(destination))
                {
                    if (FileHelper.ComputeSha256(destination).Equals(installedHash, StringComparison.OrdinalIgnoreCase))
                        File.Delete(destination);
                    else
                    {
                        var preserved = PreserveModifiedFile(deploymentPath, destination);
                        crashReporter.Log($"[Dlss5ComponentService.Uninstall] Preserved user-modified file as '{preserved}'");
                    }
                }

                if (record.OriginalBackups.TryGetValue(destination, out var backup) && backup != null)
                {
                    if (!File.Exists(backup))
                        throw new FileNotFoundException("The tracked original backup is missing.", backup);
                    if (File.Exists(destination))
                        throw new IOException("The destination could not be cleared before restoring its original file.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(backup, destination);
                }

                record.InstalledHashes.Remove(destination);
                record.OriginalBackups.Remove(destination);
                SaveRecord(deploymentPath, record);
            }
            catch (Exception ex)
            {
                var error = $"{destination}: {ex.Message}";
                errors.Add(error);
                crashReporter.Log($"[Dlss5ComponentService.Uninstall] {error}");
            }
        }

        foreach (var pair in record.LegacyLaunchPadBackups.ToArray())
        {
            try
            {
                RestoreLegacyLaunchPadPath(pair.Key, pair.Value);
                record.LegacyLaunchPadBackups.Remove(pair.Key);
                SaveRecord(deploymentPath, record);
            }
            catch (Exception ex)
            {
                var error = $"{pair.Key}: {ex.Message}";
                errors.Add(error);
                crashReporter.Log($"[Dlss5ComponentService.Uninstall] {error}");
            }
        }

        foreach (var backup in record.IniSettingBackups.AsEnumerable().Reverse().ToArray())
        {
            try
            {
                RestoreTrackedIniSetting(deploymentPath, backup, crashReporter);
                record.IniSettingBackups.Remove(backup);
                SaveRecord(deploymentPath, record);
            }
            catch (Exception ex)
            {
                var error = $"{backup.Path} [{backup.Section}] {backup.Key}: {ex.Message}";
                errors.Add(error);
                crashReporter.Log($"[Dlss5ComponentService.Uninstall] {error}");
            }
        }

        if (record.InstalledHashes.Count == 0
            && record.OriginalBackups.Count == 0
            && record.LegacyLaunchPadBackups.Count == 0
            && record.IniSettingBackups.Count == 0)
            DeleteIfExists(Path.Combine(deploymentPath, RecordRelativePath));
        else
            SaveRecord(deploymentPath, record);

        crashReporter.Log(errors.Count == 0
            ? $"[Dlss5ComponentService] Uninstalled suite-managed DLSS 5 files from '{deploymentPath}'"
            : $"[Dlss5ComponentService] Uninstall retained its recovery record after {errors.Count} error(s).");
        DeploymentPathCache.Clear();
        return errors;
    }

    private static void ValidateRecord(string deploymentPath, Dlss5InstallRecord record)
    {
        var root = NormalizeCanonicalPath(deploymentPath, "deployment root");
        if (!Directory.Exists(root))
            throw new InvalidDataException($"The deployment root does not exist: {root}");
        EnsureNoReparsePoints(root, root);

        if (!Enum.IsDefined(record.Mode))
            throw new InvalidDataException("The ownership record contains an unknown deployment mode.");
        if (!Enum.IsDefined(record.Profile))
            throw new InvalidDataException("The ownership record contains an unknown DLSS 5 install profile.");
        if (record.InstalledHashes == null
            || record.OriginalBackups == null
            || record.LegacyLaunchPadBackups == null
            || record.IniSettingBackups == null)
            throw new InvalidDataException("The ownership record contains a null path collection.");

        var adasRoot = Path.Combine(root, ".adas");
        var originalBackupRoot = Path.Combine(adasRoot, "backups");
        var legacyBackupRoot = Path.Combine(adasRoot, "legacy-launchpad-backup");
        var installedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in record.InstalledHashes)
        {
            var destination = ValidateManagedDestination(root, adasRoot, pair.Key, "installed destination");
            if (!installedDestinations.Add(destination))
                throw new InvalidDataException($"The ownership record aliases an installed destination: {pair.Key}");
            if (pair.Value.Length != 64 || pair.Value.Any(value => !Uri.IsHexDigit(value)))
                throw new InvalidDataException($"The ownership record contains an invalid SHA-256 for {pair.Key}.");
        }

        var originalDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var backupPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in record.OriginalBackups)
        {
            var destination = ValidateManagedDestination(root, adasRoot, pair.Key, "original destination");
            if (!originalDestinations.Add(destination))
                throw new InvalidDataException($"The ownership record aliases an original destination: {pair.Key}");
            if (pair.Value == null) continue;

            var backup = ValidateBackupPath(root, originalBackupRoot, pair.Value, "original backup");
            if (!backupPaths.Add(backup))
                throw new InvalidDataException($"The ownership record reuses a backup path: {pair.Value}");
        }

        var shaderRoot = Path.Combine(root, "reshade-shaders", "Shaders");
        var allowedLegacySources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(shaderRoot, "MartysMods_LAUNCHPAD.fx"),
            Path.Combine(shaderRoot, "MartysMods"),
        };
        var legacySources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in record.LegacyLaunchPadBackups)
        {
            var source = ValidateManagedDestination(root, adasRoot, pair.Key, "legacy LaunchPad source");
            if (!allowedLegacySources.Contains(source) || !legacySources.Add(source))
                throw new InvalidDataException($"The ownership record contains an invalid legacy LaunchPad source: {pair.Key}");
            var backup = ValidateBackupPath(root, legacyBackupRoot, pair.Value, "legacy LaunchPad backup");
            if (!backupPaths.Add(backup))
                throw new InvalidDataException($"The ownership record reuses a backup path: {pair.Value}");
        }

        var allowedIniPath = Path.Combine(root, "ReShade.ini");
        var iniKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in record.IniSettingBackups)
        {
            var path = ValidateManagedDestination(root, adasRoot, setting.Path, "INI setting path");
            if (!path.Equals(allowedIniPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The ownership record contains an unsupported INI setting path: {setting.Path}");
            if (string.IsNullOrWhiteSpace(setting.Key)
                || setting.Key.Contains('=')
                || setting.Key.Length > 128
                || setting.Section.Contains(']')
                || setting.Section.Length > 128
                || setting.InstalledValue.Length > 32_768
                || (setting.OriginalValue?.Length ?? 0) > 32_768)
                throw new InvalidDataException($"The ownership record contains an invalid INI setting entry for {setting.Key}.");
            var identity = $"{path}\0{setting.Section}\0{setting.Key}";
            if (!iniKeys.Add(identity))
                throw new InvalidDataException($"The ownership record contains a duplicate INI setting entry for {setting.Key}.");
        }
    }

    private static void RestoreTrackedIniSetting(
        string root,
        Dlss5IniSettingBackup backup,
        ICrashReporter crashReporter)
    {
        if (!File.Exists(backup.Path))
        {
            if (backup.Existed)
                throw new FileNotFoundException("The INI file containing the original setting is missing.", backup.Path);
            return;
        }

        var document = IniTextDocument.Load(backup.Path);
        var exists = document.TryGetValue(backup.Section, backup.Key, out var current);
        if (!exists)
        {
            if (backup.Existed)
                crashReporter.Log($"[Dlss5ComponentService.Uninstall] Kept user removal of [{backup.Section}] {backup.Key} in '{backup.Path}'");
            return;
        }
        if (!current.Text.Equals(backup.InstalledValue, StringComparison.Ordinal))
        {
            crashReporter.Log($"[Dlss5ComponentService.Uninstall] Kept user-modified [{backup.Section}] {backup.Key} in '{backup.Path}'");
            return;
        }

        if (backup.Existed)
            document.SetValue(backup.Section, backup.OriginalKey ?? backup.Key, backup.OriginalValue ?? "");
        else
            document.RemoveValue(backup.Section, backup.Key);
        document.Save(backup.Path);
    }

    private static string ValidateManagedDestination(
        string root,
        string adasRoot,
        string path,
        string label)
    {
        var canonical = NormalizeCanonicalPath(path, label);
        if (!IsPathBelow(root, canonical) || IsPathAtOrBelow(adasRoot, canonical))
            throw new InvalidDataException($"The ownership record {label} escapes the managed deployment: {path}");
        EnsureNoReparsePoints(root, canonical);
        return canonical;
    }

    private static string ValidateBackupPath(string root, string backupRoot, string path, string label)
    {
        var canonical = NormalizeCanonicalPath(path, label);
        if (!IsPathBelow(backupRoot, canonical))
            throw new InvalidDataException($"The ownership record {label} is outside its .adas backup root: {path}");
        EnsureNoReparsePoints(root, canonical);
        return canonical;
    }

    private static string NormalizeCanonicalPath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException($"The ownership record {label} is not an absolute path: {path}");
        string canonical;
        try { canonical = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidDataException($"The ownership record {label} is invalid: {path}", ex);
        }
        if (!string.Equals(TrimDirectorySeparators(path), TrimDirectorySeparators(canonical), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The ownership record {label} is not canonical: {path}");
        return TrimDirectorySeparators(canonical);
    }

    private static string TrimDirectorySeparators(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsPathBelow(string parent, string candidate)
        => !candidate.Equals(TrimDirectorySeparators(parent), StringComparison.OrdinalIgnoreCase)
           && IsPathAtOrBelow(parent, candidate);

    private static bool IsPathAtOrBelow(string parent, string candidate)
    {
        var prefix = TrimDirectorySeparators(parent) + Path.DirectorySeparatorChar;
        return candidate.Equals(TrimDirectorySeparators(parent), StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparsePoints(string root, string path)
    {
        if (!IsPathAtOrBelow(root, path))
            throw new InvalidDataException($"The ownership record path escapes the deployment root: {path}");

        var current = TrimDirectorySeparators(root);
        RejectReparsePoint(current);
        var relative = Path.GetRelativePath(current, path);
        if (relative == ".") return;
        foreach (var segment in relative.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            RejectReparsePoint(current);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"The ownership record traverses a reparse point: {path}");
    }

    private static string PreserveModifiedFile(string root, string path)
    {
        var preservedDirectory = Path.Combine(root, ".adas", "preserved");
        Directory.CreateDirectory(preservedDirectory);
        var preservedPath = Path.Combine(
            preservedDirectory,
            $"{Path.GetFileName(path)}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.modified");
        Dlss5SwitchJournal.Current?.CaptureMove(path, preservedPath);
        File.Move(path, preservedPath);
        return preservedPath;
    }

    private static string GetComponentStagingPath(string? channel = null)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "Adas", "DLSS5", "Feeder");
        return string.IsNullOrWhiteSpace(channel) ? root : Path.Combine(root, channel);
    }

    private static string GetBundledComponentDirectory()
        => Path.Combine(AppContext.BaseDirectory, "Assets", "DLSS5");

    private static bool HasBundledReShadeFrameworkHeaders()
        => ReShadeFrameworkHeaders.All(header =>
            File.Exists(Path.Combine(GetBundledComponentDirectory(), header)));

    private static string GetBundledFeederAssetName(string canonicalName, bool useBeta, bool is64Bit)
    {
        if (!useBeta) return canonicalName;
        return canonicalName switch
        {
            FeederAddon => $"dlss5-feed-{BundledFeederBetaVersion}.addon64",
            FeederAddon32 => $"dlss5-feed-{BundledFeederBetaVersion}.addon32",
            FeederHost64 => $"dlss5-feed-host64-{BundledFeederBetaVersion}.exe",
            FeederShader => $"DLSS5_Feed-{BundledFeederBetaVersion}.fx",
            FeederVulkanLayer => $"feed-vk-layer-{BundledFeederBetaVersion}-{(is64Bit ? "x64" : "x86")}.zip",
            _ => canonicalName,
        };
    }

    private static void ValidateComponent(string name, string path)
    {
        if (name.Equals(FeederVulkanLayer, StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(path);
            var expected64 = new[] { "run-with-feed-layer.bat", "VkLayer_feed_vk.dll", "VkLayer_feed_vk.json" };
            var expected32 = new[] { "run-with-feed-layer32.bat", "VkLayer_feed_vk32.dll", "VkLayer_feed_vk32.json" };
            bool ContainsAll(IEnumerable<string> expected) => expected.All(required => archive.Entries.Any(entry =>
                entry.Name.Equals(required, StringComparison.OrdinalIgnoreCase)));
            if (!ContainsAll(expected64) && !ContainsAll(expected32))
                throw new InvalidOperationException($"{name} is missing required Vulkan fallback files.");
        }
        else if (name.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase))
            ValidatePortableExecutable(path, 64 * 1024, name, expectedMachine: 0x8664);
        else if (name.Equals(FeederHost64, StringComparison.OrdinalIgnoreCase))
            ValidatePortableExecutable(path, 32 * 1024, name, expectedMachine: 0x8664);
        else if (name.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase))
            ValidatePortableExecutable(path, 16 * 1024, name, expectedMachine: 0x014c);
        else if (!File.ReadAllText(path).Contains("technique", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{name} is not a plausible ReShade shader.");
    }

    private static void ValidatePortableExecutable(
        string path,
        long minimumBytes,
        string label,
        ushort? expectedMachine = null)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length <= minimumBytes || stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidOperationException($"{label} is not a plausible Windows executable file.");
        if (expectedMachine != null)
        {
            stream.Position = 0x3c;
            Span<byte> offsetBytes = stackalloc byte[4];
            if (stream.Read(offsetBytes) != offsetBytes.Length)
                throw new InvalidOperationException($"{label} has an invalid PE header.");
            var peOffset = BitConverter.ToInt32(offsetBytes);
            if (peOffset < 0 || peOffset > stream.Length - 6)
                throw new InvalidOperationException($"{label} has an invalid PE header offset.");
            stream.Position = peOffset;
            Span<byte> header = stackalloc byte[6];
            if (stream.Read(header) != header.Length
                || header[0] != 'P' || header[1] != 'E' || header[2] != 0 || header[3] != 0
                || BitConverter.ToUInt16(header[4..]) != expectedMachine.Value)
                throw new InvalidOperationException($"{label} has the wrong Windows architecture.");
        }
    }

    private static void CopyAtomically(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(source, temporary, overwrite: false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { DeleteIfExists(temporary); }
    }

    private static void DeleteIfExists(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static readonly Dictionary<string, string> FeederDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enabled"] = "1",
        ["mode"] = "2",
        ["hdr"] = "-1",
        ["depth_inverted"] = "-1",
        ["flags"] = "-1",
        ["reset_every"] = "0",
        ["warmup_rebuild"] = "0",
        ["rebuild"] = "0",
        ["log_frames"] = "3",
        ["create_delay"] = "60",
        ["preset"] = "0",
        ["host_window"] = "1",
        ["work_resolution"] = "100",
        ["mv_scale_x"] = "1.0",
        ["mv_scale_y"] = "1.0",
    };

    private static readonly Dictionary<string, string> FeederBetaDefaults = new(FeederDefaults, StringComparer.OrdinalIgnoreCase)
    {
        ["gpu_timeout_ms"] = "2000",
        ["work_upscale"] = "0",
        ["work_sharpness"] = "0.3",
    };

    private static readonly Dictionary<string, string> NativeVulkanBridgeDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vk_mirror"] = "1",
        ["source"] = "mirror",
        ["synth_after"] = "0",
        ["ofa_grid"] = "2",
        ["ofa_perf"] = "20",
        ["stage"] = "3",
        ["mode"] = "2",
    };

    private sealed record StagedProviderFile(string SourcePath, string RelativeGamePath);
    private sealed record StagedComponent(string Version, IReadOnlyDictionary<string, string> Files);
}
