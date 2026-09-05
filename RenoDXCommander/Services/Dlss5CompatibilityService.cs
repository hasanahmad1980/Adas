using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Produces a conservative, reviewable DLSS 5 deployment recommendation. The
/// classifier deliberately refuses ambiguous paths and any anti-cheat or
/// multiplayer evidence; it never silently chooses between equally likely
/// game binary folders.
/// </summary>
public sealed class Dlss5CompatibilityService
{
    internal const string AmbiguousDeploymentPathReason =
        "Multiple equally likely game binary folders were found. Select the exact executable folder before installing.";
    internal const string MissingDeploymentPathReason =
        "The game binary folder could not be resolved.";

    private static readonly Regex SupportedRtxSeries = new(@"\bRTX\s*(?:20|30|40|50)\d{2}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Ada = GeForce RTX 40-series (4050–4090, incl. Laptop and RTX 4000 Ada workstation parts).
    // MFG Ada Unlock only applies here: RTX 30 lacks the required machine code and RTX 50 already ships MFG natively.
    private static readonly Regex AdaRtxSeries = new(@"\bRTX\s*40\d{2}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Lazy<string> CachedGpuName = new(DetectGpuNameCore, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly IPeHeaderService _peHeaderService;

    public Dlss5CompatibilityService(IPeHeaderService peHeaderService)
    {
        _peHeaderService = peHeaderService;
    }

    public static IReadOnlyList<string> AntiCheatMarkers { get; } = new[]
    {
        "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "BattlEye", "BEClient",
        "EOSAntiCheat", "vgk", "RiotClient", "FACEIT", "XIGNCODE", "mhyprot",
        "nProtect", "GameGuard",
    };

    private static readonly string[] MultiplayerMarkers =
    {
        "dedicatedserver", "server_launcher", "multiplayer.block", "online-only.block",
    };

    private static readonly string[] ExcludedDirectoryNames =
    {
        "_CommonRedist", "redist", "redistributable", "installer", "support", "crashreporter",
        "EasyAntiCheat", "BattlEye", "Engine", "Prerequisites", "Uninstall", "Uninstaller",
    };

    public Dlss5Probe Probe(
        GameCardViewModel card,
        GraphicsApiType? userApiOverride = null)
    {
        var emulator = Dlss5EmulatorService.FindInstallation(card.InstallPath);
        var resolution = emulator == null ? ResolveDeploymentPath(card.InstallPath)
            : new Dlss5PathResolution(Dlss5PathResolutionKind.Resolved, Path.GetDirectoryName(emulator.Executable), new[] { Path.GetDirectoryName(emulator.Executable)! });
        var path = resolution.Path;
        var environment = emulator == null
            ? GraphicsEnvironmentService.Detect(path ?? card.InstallPath)
            : GraphicsEnvironmentService.Detect(path!, emulator.Executable);
        if (emulator == null)
            environment = GraphicsEnvironmentService.ApplyUserOverride(environment, userApiOverride);
        var selectedApi = emulator == null ? environment.Api : Dlss5EmulatorService.LoadRenderer(emulator) ?? GraphicsApiType.Unknown;
        var files = path == null ? Array.Empty<string>() : EnumerateFilesSafe(path, maxDepth: 3).ToArray();
        var safetyFiles = Directory.Exists(card.InstallPath)
            ? EnumerateFilesSafe(card.InstallPath, maxDepth: 5, skipExcludedDirectories: false).ToArray()
            : files;

        bool HasFile(string name) => files.Any(file =>
            string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase));

        var antiCheat = safetyFiles
            .Where(file => AntiCheatMarkers.Any(marker => Path.GetFileName(file).Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Select(Path.GetFileName)
            .Where(name => name != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

        var multiplayer = safetyFiles
            .Where(file => MultiplayerMarkers.Any(marker => Path.GetFileName(file).Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Select(Path.GetFileName)
            .Where(name => name != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

        var shadersRoot = path == null ? null : Path.Combine(path, "reshade-shaders", "Shaders");
        var hasMotionVectorProvider = shadersRoot != null && HasMotionVectorProvider(shadersRoot);
        var installRecord = path == null ? null : Dlss5ComponentService.LoadRecord(path);
        var detectedMachine = emulator != null ? _peHeaderService.DetectArchitecture(emulator.Executable)
            : path == null ? MachineType.Native : _peHeaderService.DetectGameArchitecture(path);
        var is64Bit = ResolveActualIs64Bit(detectedMachine, card.Is32Bit);
        if (path != null && installRecord != null && !is64Bit)
            Dlss5LaunchRecoveryService.TryRecordRecentWindowsCrash(path, installRecord, selectedApi);
        var preferDxvkForDirectX9 = selectedApi == GraphicsApiType.DirectX9
            && !is64Bit
            && (installRecord?.PreferDxvkForDirectX9 == true
                || installRecord?.Mode == Dlss5DeploymentMode.Dx9ViaDxvkFeeder);
        var hasNativeDlss = emulator == null && path != null
            && (HasOriginalRuntime(path, files, installRecord, "nvngx_dlss.dll", "sl.dlss.dll")
                || HasNativeRuntimeInUnrealPlugins(path, "nvngx_dlss.dll", "sl.dlss.dll"));
        var hasLegacyTranslation = path != null && selectedApi switch
        {
            GraphicsApiType.DirectX8 => HasFile("d3d8.dll") && HasFile("dgVoodoo.conf"),
            GraphicsApiType.DirectX9 => HasFile("d3d9.dll")
                                              && (preferDxvkForDirectX9
                                                  ? VulkanFootprintService.Exists(path)
                                                  : HasFile("dgVoodoo.conf")),
            GraphicsApiType.DirectX10 => HasFile("d3d10core.dll")
                                                && HasFile("dxgi.dll")
                                                && VulkanFootprintService.Exists(path),
            _ => true,
        };
        if (detectedMachine is MachineType.I386 or MachineType.x64
            && is64Bit == card.Is32Bit)
            CrashReporter.Log($"[Dlss5CompatibilityService] Corrected stale card architecture for '{card.GameName}' from {(card.Is32Bit ? "32" : "64")}-bit to {(is64Bit ? "64" : "32")}-bit");

        return new Dlss5Probe
        {
            GameName = card.GameName,
            DeploymentPath = path,
            HasAmbiguousDeploymentPath = resolution.Kind == Dlss5PathResolutionKind.Ambiguous,
            GraphicsApi = selectedApi,
            GraphicsApiEvidence = environment.Evidence,
            SupportedGraphicsApis = environment.SupportedApis.ToArray(),
            InstallationIssues = path == null ? Array.Empty<string>() : GraphicsEnvironmentService.CheckInstallation(path, emulator == null ? userApiOverride : null),
            OpenXrDetected = environment.OpenXrDetected,
            Is64Bit = is64Bit,
            GpuName = CachedGpuName.Value,
            HasNativeDlss = hasNativeDlss,
            HasReShadeAddonSupport = selectedApi == GraphicsApiType.Vulkan
                || preferDxvkForDirectX9
                || (selectedApi == GraphicsApiType.DirectX10 && is64Bit && path != null && VulkanFootprintService.Exists(path))
                ? VulkanLayerService.IsLayerInstalled(require32Bit: !is64Bit) && HasFile("reshade.ini")
                : path != null && HasCompatibleReShadeRuntime(path, files, is32Bit: !is64Bit)
                    && (HasFile("ReShade.ini") || HasFile("reshade.ini")),
            HasRenoDx5Addon = HasFile(Renodx5AddonService.AddonFileName)
                || HasFile("renodx-dlss5.addon64")
                || HasFile("renodx-dlss5(2).addon64"),
            HasNvngxDlssNr = HasFile("nvngx_dlssnr.dll")
                || (!is64Bit && path != null && File.Exists(Path.Combine(path, "host64", "nvngx_dlssnr.dll"))),
            HasNvngxDlss = HasFile("nvngx_dlss.dll")
                || (!is64Bit && path != null && File.Exists(Path.Combine(path, "host64", "nvngx_dlss.dll"))),
            HasMotionVectorProvider = hasMotionVectorProvider,
            HasLegacyTranslation = hasLegacyTranslation,
            PreferDxvkForDirectX9 = preferDxvkForDirectX9,
            AntiCheatEvidence = antiCheat,
            MultiplayerEvidence = multiplayer,
            MissingRuntimeArchitectures = path == null ? Array.Empty<string>() : Dlss5RuntimePrerequisites.MissingArchitectures(path, is64Bit),
        };
    }

    internal static bool ResolveActualIs64Bit(MachineType detectedMachine, bool cardIs32Bit)
        => detectedMachine switch
        {
            MachineType.I386 => false,
            MachineType.x64 => true,
            _ => !cardIs32Bit,
        };

    private bool HasCompatibleReShadeRuntime(string deploymentPath, IEnumerable<string> files, bool is32Bit)
    {
        var expectedMachine = is32Bit ? MachineType.I386 : MachineType.x64;
        foreach (var file in files)
        {
            if (!string.Equals(Path.GetDirectoryName(file), deploymentPath, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsReShadeProxyName(Path.GetFileName(file)))
                continue;

            try
            {
                var description = FileVersionInfo.GetVersionInfo(file).FileDescription;
                if (description?.Contains("ReShade", StringComparison.OrdinalIgnoreCase) == true
                    && _peHeaderService.DetectArchitecture(file) == expectedMachine)
                    return true;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[Dlss5CompatibilityService] Failed to validate ReShade runtime '{file}': {ex.Message}");
            }
        }

        return false;
    }

    internal static bool HasOriginalRuntime(
        string deploymentPath,
        IEnumerable<string> files,
        Dlss5InstallRecord? record,
        params string[] runtimeNames)
    {
        foreach (var runtimeName in runtimeNames)
        {
            var runtimePath = files.FirstOrDefault(file =>
                string.Equals(Path.GetFileName(file), runtimeName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetDirectoryName(file), deploymentPath, StringComparison.OrdinalIgnoreCase));
            if (runtimePath == null)
                continue;

            if (record?.InstalledHashes.ContainsKey(runtimePath) != true)
                return true;
            if (record.OriginalBackups.TryGetValue(runtimePath, out var backupPath)
                && !string.IsNullOrWhiteSpace(backupPath))
                return true;
        }

        return false;
    }

    internal static bool HasNativeRuntimeInUnrealPlugins(string deploymentPath, params string[] runtimeNames)
    {
        var searchRoot = DlssStreamlineService.ResolveSearchRoot(deploymentPath);
        var pluginRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(searchRoot, "Engine", "Plugins"),
            Path.Combine(searchRoot, "Plugins"),
        };

        var current = new DirectoryInfo(deploymentPath);
        while (current != null)
        {
            if (string.Equals(current.Name, "Binaries", StringComparison.OrdinalIgnoreCase)
                && current.Parent != null)
            {
                pluginRoots.Add(Path.Combine(current.Parent.FullName, "Plugins"));
                break;
            }
            current = current.Parent;
        }

        foreach (var pluginRoot in pluginRoots.Where(Directory.Exists))
        {
            foreach (var file in EnumerateFilesSafe(pluginRoot, maxDepth: 10, skipExcludedDirectories: false))
            {
                if (!runtimeNames.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
                    continue;
                if (File.Exists(Path.Combine(Path.GetDirectoryName(file)!, "OptiScaler.ini")))
                    continue;
                return true;
            }
        }

        return false;
    }

    private static bool IsReShadeProxyName(string name)
        => name.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase)
            || name.Equals("d3d8.dll", StringComparison.OrdinalIgnoreCase)
            || name.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase)
            || name.Equals("d3d10.dll", StringComparison.OrdinalIgnoreCase)
            || name.Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase)
            || name.Equals("d3d12.dll", StringComparison.OrdinalIgnoreCase)
            || name.Equals("opengl32.dll", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ReShade32.dll", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ReShade64.dll", StringComparison.OrdinalIgnoreCase);

    public static Dlss5Assessment Assess(Dlss5Probe probe, bool singlePlayerConfirmed = false)
    {
        var blocks = new List<string>();
        var missing = new List<string>();

        if (!IsSupportedGpu(probe.GpuName))
            blocks.Add("This package requires an NVIDIA GeForce RTX 20-, 30-, 40-, or 50-series GPU.");
        if (probe.AntiCheatEvidence.Count > 0)
            blocks.Add($"Detected anti-cheat software: {string.Join(", ", probe.AntiCheatEvidence)}. Adas will not modify this game.");
        if (probe.MultiplayerEvidence.Count > 0)
            blocks.Add($"Detected multiplayer/online-only evidence: {string.Join(", ", probe.MultiplayerEvidence)}. Adas will not modify this game.");
        else if (!singlePlayerConfirmed)
            blocks.Add("Online status is not verified. Confirm single-player/offline use before Adas can modify this game.");
        if (probe.HasAmbiguousDeploymentPath)
            blocks.Add(AmbiguousDeploymentPathReason);
        else if (string.IsNullOrWhiteSpace(probe.DeploymentPath))
            blocks.Add(MissingDeploymentPathReason);

        var mode = probe.GraphicsApi switch
        {
            GraphicsApiType.DirectX12 when probe.HasNativeDlss && probe.Is64Bit => Dlss5DeploymentMode.NativeDirectX12,
            GraphicsApiType.DirectX12 => Dlss5DeploymentMode.Dx12Feeder,
            GraphicsApiType.DirectX11 when probe.HasNativeDlss && probe.Is64Bit => Dlss5DeploymentMode.NativeDirectX11,
            GraphicsApiType.DirectX11 => Dlss5DeploymentMode.Dx11Feeder,
            GraphicsApiType.DirectX10 => probe.Is64Bit
                ? Dlss5DeploymentMode.Dx10ViaDxvkFeeder
                : Dlss5DeploymentMode.Dx10Feeder,
            GraphicsApiType.Vulkan when probe.HasNativeDlss && probe.Is64Bit => Dlss5DeploymentMode.NativeVulkan,
            GraphicsApiType.Vulkan => Dlss5DeploymentMode.VulkanFeeder,
            GraphicsApiType.DirectX9 when probe.PreferDxvkForDirectX9 && !probe.Is64Bit => Dlss5DeploymentMode.Dx9ViaDxvkFeeder,
            GraphicsApiType.DirectX9 => Dlss5DeploymentMode.Dx9Feeder,
            GraphicsApiType.DirectX8 when !probe.Is64Bit => Dlss5DeploymentMode.Dx8Feeder,
            GraphicsApiType.OpenGL => Dlss5DeploymentMode.OpenGlFeeder,
            _ => Dlss5DeploymentMode.None,
        };

        if (mode == Dlss5DeploymentMode.None)
        {
            blocks.Add(probe.GraphicsApiEvidence);
            if (probe.SupportedGraphicsApis.Count > 1)
                blocks.Add("This executable can use: " + string.Join(", ", probe.SupportedGraphicsApis.Select(GraphicsApiDetector.GetLabel)) + ". Ada will wait for launch evidence instead of guessing.");
            else
                blocks.Add("Supported: 32-bit DirectX 8, DirectX 9–12, Vulkan and OpenGL. DirectX 8 requires a 32-bit executable.");
        }
        foreach (var architecture in probe.MissingRuntimeArchitectures)
            blocks.Add($"Microsoft Visual C++ 2015–2022 runtime ({architecture}) is missing. Use the Microsoft runtime button below, then reopen this review. Game files have not been changed.");
        if (mode is Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.NativeVulkan
            && !probe.HasReShadeAddonSupport)
            blocks.Add("Install the Vulkan ReShade layer and this game's reshade.ini from the ReShade row before installing the DLSS 5 suite.");

        if (!probe.HasReShadeAddonSupport)
            missing.Add("ReShade 6.8+ with full add-on support");
        if (!probe.HasRenoDx5Addon)
            missing.Add("RenoDX DLSS 5 neural-rendering add-on");
        if (!probe.HasNvngxDlssNr)
            missing.Add("nvngx_dlssnr.dll model/runtime");
        if (IsFeederMode(mode))
        {
            if (!probe.HasNvngxDlss)
                missing.Add("nvngx_dlss.dll Super Resolution runtime");
            if (!probe.HasMotionVectorProvider)
                missing.Add("a supported motion-vector provider (Adas installs LumeniteFX Kernel)");
            if (mode is Dlss5DeploymentMode.Dx9Feeder or Dlss5DeploymentMode.Dx8Feeder && !probe.HasLegacyTranslation)
                missing.Add("dgVoodoo2 2.87.3+ translation to DirectX 11 (installed automatically)");
            if ((mode is Dlss5DeploymentMode.Dx10ViaDxvkFeeder or Dlss5DeploymentMode.Dx9ViaDxvkFeeder)
                && !probe.HasLegacyTranslation)
                missing.Add("DXVK translation to Vulkan and the Vulkan ReShade layer (installed automatically)");
        }

        return new Dlss5Assessment(mode, probe.DeploymentPath, blocks, missing, singlePlayerConfirmed, probe.Is64Bit);
    }

    internal static Dlss5Assessment ConfirmDeploymentPath(Dlss5Assessment assessment, string deploymentPath)
    {
        if (string.IsNullOrWhiteSpace(deploymentPath))
            throw new ArgumentException("Choose a game executable folder.", nameof(deploymentPath));

        var remainingBlocks = assessment.BlockingReasons
            .Where(reason => !reason.Equals(AmbiguousDeploymentPathReason, StringComparison.Ordinal)
                             && !reason.Equals(MissingDeploymentPathReason, StringComparison.Ordinal))
            .ToArray();
        return assessment with
        {
            DeploymentPath = Path.GetFullPath(deploymentPath),
            BlockingReasons = remainingBlocks,
        };
    }

    internal static bool CanConfirmDeploymentPath(Dlss5Assessment assessment)
        => assessment.Mode != Dlss5DeploymentMode.None
           && assessment.BlockingReasons.Count > 0
           && assessment.BlockingReasons.All(reason =>
               reason.Equals(AmbiguousDeploymentPathReason, StringComparison.Ordinal)
               || reason.Equals(MissingDeploymentPathReason, StringComparison.Ordinal));

    public static bool IsSupportedGpu(string? gpuName)
        => !string.IsNullOrWhiteSpace(gpuName) && SupportedRtxSeries.IsMatch(gpuName);

    /// <summary>The detected primary GPU name (cached), e.g. "NVIDIA GeForce RTX 4080".</summary>
    public static string DetectedGpuName => CachedGpuName.Value;

    /// <summary>True when the given GPU name is a GeForce RTX 40-series (Ada) part — the only
    /// GPUs MFG Ada Unlock applies to.</summary>
    public static bool IsAdaGpu(string? gpuName)
        => !string.IsNullOrWhiteSpace(gpuName) && AdaRtxSeries.IsMatch(gpuName);

    /// <summary>True when the detected primary GPU is a GeForce RTX 40-series (Ada) part.</summary>
    public static bool IsAdaGpuDetected => IsAdaGpu(CachedGpuName.Value);

    internal static bool IsFeederMode(Dlss5DeploymentMode mode)
        => mode is Dlss5DeploymentMode.Dx11Feeder
            or Dlss5DeploymentMode.Dx12Feeder
            or Dlss5DeploymentMode.VulkanFeeder
            or Dlss5DeploymentMode.Dx9Feeder
            or Dlss5DeploymentMode.Dx8Feeder
            or Dlss5DeploymentMode.OpenGlFeeder
            or Dlss5DeploymentMode.Dx10ViaDxvkFeeder
            or Dlss5DeploymentMode.Dx9ViaDxvkFeeder
            or Dlss5DeploymentMode.Dx10Feeder;

    private static bool HasMotionVectorProvider(string shaderDirectory)
    {
        if (!Directory.Exists(shaderDirectory)) return false;
        foreach (var file in EnumerateFilesSafe(shaderDirectory, maxDepth: 3))
        {
            var name = Path.GetFileName(file);
            if (name.Equals(Dlss5ComponentService.FeederShader, StringComparison.OrdinalIgnoreCase)
                || (!name.EndsWith(".fx", StringComparison.OrdinalIgnoreCase)
                    && !name.EndsWith(".fxh", StringComparison.OrdinalIgnoreCase)))
                continue;
            try
            {
                if (new FileInfo(file).Length <= 2 * 1024 * 1024
                    && (File.ReadAllText(file).Contains("texMotionVectors", StringComparison.Ordinal)
                        || name.Equals("lumenite_Kernel.fx", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("lumenite_QuantMotion.fx", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            catch { }
        }
        return false;
    }

    public static Dlss5PathResolution ResolveDeploymentPath(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            return new(Dlss5PathResolutionKind.Missing, null, Array.Empty<string>());

        var root = Path.GetFullPath(installPath);
        var gameExecutable = new PeHeaderService().FindGameExe(root);
        var executableDirectory = gameExecutable == null ? null : Path.GetDirectoryName(gameExecutable);
        if (executableDirectory != null
            && !executableDirectory.Equals(root, StringComparison.OrdinalIgnoreCase)
            && IsPathInsideRoot(root, executableDirectory))
        {
            return new(
                Dlss5PathResolutionKind.Resolved,
                executableDirectory,
                new[] { executableDirectory });
        }

        var installedDeploymentPath = Dlss5ComponentService.FindInstalledDeploymentPath(root);
        if (installedDeploymentPath != null)
            return new(
                Dlss5PathResolutionKind.Resolved,
                installedDeploymentPath,
                new[] { installedDeploymentPath });

        var scored = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in EnumerateFilesSafe(root, maxDepth: 5))
        {
            var name = Path.GetFileName(file);
            var dir = Path.GetDirectoryName(file);
            if (dir == null) continue;

            var score = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? 20 : 0;
            if (name.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("opengl32.dll", StringComparison.OrdinalIgnoreCase)) score = 100;
            else if (name.StartsWith("nvngx_dlss", StringComparison.OrdinalIgnoreCase)) score = 90;
            else if (name.Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase)) score = 80;
            else if (name.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)) score = 70;

            if (score > 0)
                scored[dir] = scored.TryGetValue(dir, out var current) ? Math.Max(current, score) : score;
        }

        if (scored.Count == 0)
            return new(Dlss5PathResolutionKind.Missing, null, Array.Empty<string>());

        var bestScore = scored.Values.Max();
        var candidates = scored.Where(pair => pair.Value == bestScore)
            .Select(pair => pair.Key)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return candidates.Length == 1
            ? new(Dlss5PathResolutionKind.Resolved, candidates[0], candidates)
            : new(Dlss5PathResolutionKind.Ambiguous, null, candidates);
    }

    private static bool IsPathInsideRoot(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return !Path.IsPathRooted(relative)
            && !relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    internal static IEnumerable<string> EnumerateFilesSafe(
        string root,
        int maxDepth,
        bool skipExcludedDirectories = true)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));

        while (pending.Count > 0)
        {
            var (current, depth) = pending.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(current); }
            catch { continue; }

            foreach (var file in files)
                yield return file;

            if (depth >= maxDepth) continue;

            IEnumerable<string> directories;
            try { directories = Directory.EnumerateDirectories(current); }
            catch { continue; }

            foreach (var directory in directories)
            {
                try
                {
                    var info = new DirectoryInfo(directory);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (skipExcludedDirectories
                        && ExcludedDirectoryNames.Any(name => info.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                    pending.Push((directory, depth + 1));
                }
                catch { }
            }
        }
    }

    private static string DetectGpuNameCore()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "nvidia-smi.exe",
                Arguments = "--query-gpu=name --format=csv,noheader",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            var output = process?.StandardOutput.ReadLine();
            process?.WaitForExit(2500);
            if (!string.IsNullOrWhiteSpace(output)) return output.Trim();
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
            if (key == null) return "";
            foreach (var adapterId in key.GetSubKeyNames())
            foreach (var instance in new[] { "0000", "0001", "0002" })
            {
                using var adapter = key.OpenSubKey($@"{adapterId}\{instance}");
                var description = adapter?.GetValue("Device Description") as string
                    ?? adapter?.GetValue("DriverDesc") as string;
                if (!string.IsNullOrWhiteSpace(description) && description.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    return description;
            }
        }
        catch { }

        return "";
    }
}
