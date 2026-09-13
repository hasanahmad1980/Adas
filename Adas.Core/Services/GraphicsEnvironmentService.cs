using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>Evidence about one executable, never a game-name or engine-default lookup.</summary>
internal sealed record GraphicsEnvironment(
    string? Executable, MachineType Machine, GraphicsApiType Api,
    HashSet<GraphicsApiType> SupportedApis, string? ReShadeProxy, string Evidence,
    bool OpenXrDetected = false, bool IsBestGuess = false);

internal static class GraphicsEnvironmentService
{
    private static readonly PeHeaderService Pe = new();
    private static readonly string[] ProxyNames =
        ["dxgi.dll", "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll", "opengl32.dll", "ReShade32.dll", "ReShade64.dll"];
    private static readonly Dictionary<string, GraphicsApiType> RuntimeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["d3d8.dll"] = GraphicsApiType.DirectX8, ["d3d9.dll"] = GraphicsApiType.DirectX9,
        ["d3d10.dll"] = GraphicsApiType.DirectX10, ["d3d10_1.dll"] = GraphicsApiType.DirectX10,
        ["d3d11.dll"] = GraphicsApiType.DirectX11, ["d3d12.dll"] = GraphicsApiType.DirectX12,
        ["opengl32.dll"] = GraphicsApiType.OpenGL, ["vulkan-1.dll"] = GraphicsApiType.Vulkan,
    };
    private sealed record Observation(string Fingerprint, GraphicsApiType Api, DateTime ObservedUtc, string? Schema = null);
    private const string ObservationSchema = "renderer-inputs-v2";
    private static string ObservationDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "GraphicsObservations");

    public static GraphicsEnvironment Detect(string root, string? executable = null, string? observationDirectory = null)
    {
        var supported = new HashSet<GraphicsApiType>();
        try
        {
            var exe = executable ?? Pe.FindGameExe(root);
            if (exe == null || !File.Exists(exe))
                return new(null, MachineType.Native, GraphicsApiType.Unknown, supported, null,
                    "The game executable could not be identified. Select its executable, not a launcher.");
            exe = Path.GetFullPath(exe);
            var directory = Path.GetDirectoryName(exe)!;
            var binaries = new[] { exe, Path.Combine(directory, "UnityPlayer.dll"), Path.Combine(directory, "GameAssembly.dll") };
            foreach (var binary in binaries.Where(File.Exists))
                supported.UnionWith(GraphicsApiDetector.DetectAllApis(binary));
            var machine = Pe.DetectArchitecture(exe);
            var xr = File.Exists(Path.Combine(directory, "openxr_loader.dll"));
            // Actual engine output outranks supported imports. ReShade helper/host logs are excluded.
            var api = ReadRuntimeApi(exe);
            var evidence = "Detected from this game's current runtime log.";
            if (api == GraphicsApiType.Unknown)
            {
                api = ReadObservation(exe, observationDirectory);
                evidence = "Observed a single rendering runtime during this game's launch (not a frame-quality check).";
            }
            if (api == GraphicsApiType.Unknown)
            {
                api = GraphicsApiDetector.DetectUnityFromBootConfig(directory);
                evidence = "Selected by the game's explicit Unity renderer configuration.";
            }
            if (api == GraphicsApiType.Unknown && supported.Count == 1)
            {
                api = supported.Single();
                evidence = "Only one rendering API was found in the executable/engine imports; not yet verified in gameplay.";
            }
            if (api == GraphicsApiType.Unknown)
                evidence = "The active renderer is not confirmed. Launch the game from Adas, reach gameplay, then return to Review / Repair. Adas will not choose the highest DirectX version.";
            var proxy = ProxyFor(api);
            if (api == GraphicsApiType.Unknown && supported.Count > 0 && supported.All(IsDxgi))
                proxy = "dxgi.dll"; // ReShade shares one hook; DLSS still needs the precise API.
            return new(exe, machine, api, supported, proxy, evidence, xr);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(null, MachineType.Native, GraphicsApiType.Unknown, supported, null,
                "Adas could not read the renderer evidence: " + ex.Message);
        }
    }

    /// <summary>
    /// <see cref="Detect"/> plus every softer evidence layer, so setup always has an answer the user can see and
    /// correct instead of a dead end. In order: Unreal Engine RHI logs and settings, the last-run Unity/ReShade
    /// logs (any age), renderer runtimes the game ships (DirectX 12 Agility SDK, FidelityFX/XeSS backends), and
    /// finally the executable's own imports. Anything short of a log is flagged <see cref="GraphicsEnvironment.IsBestGuess"/>.
    /// </summary>
    public static GraphicsEnvironment DetectWithBestGuess(string root, string? executable = null,
        string? observationDirectory = null, string? localAppData = null, string? documents = null)
    {
        var environment = Detect(root, executable, observationDirectory);
        if (environment.Executable == null || environment.Api != GraphicsApiType.Unknown)
            return environment;

        try
        {
            var exe = environment.Executable;
            var (api, evidence, guess) = ReadUnrealRhi(exe, localAppData, documents);
            if (api == GraphicsApiType.Unknown)
            {
                api = ReadRuntimeApi(exe, anyAge: true);
                (evidence, guess) = ("Detected from the log of the last time this game ran.", false);
            }
            if (api == GraphicsApiType.Unknown)
            {
                (api, evidence) = ReadShippedRuntimeHints(Path.GetDirectoryName(exe)!, environment.SupportedApis);
                guess = true;
            }
            if (api == GraphicsApiType.Unknown)
            {
                api = PickLikelyApi(exe, environment.SupportedApis);
                evidence = environment.SupportedApis.Count > 1
                    ? $"Best guess: this game can use {string.Join(", ", environment.SupportedApis.OrderBy(a => a).Select(GraphicsApiDetector.GetLabel))}; "
                      + $"Adas picked {GraphicsApiDetector.GetLabel(api)}. If the game runs on something else, change Graphics API."
                    : $"Best guess from the game's program file: {GraphicsApiDetector.GetLabel(api)}. If that's wrong, change Graphics API.";
            }
            if (api == GraphicsApiType.Unknown)
                return environment;

            return environment with
            {
                Api = api,
                SupportedApis = new HashSet<GraphicsApiType>(environment.SupportedApis) { api },
                ReShadeProxy = ProxyFor(api),
                Evidence = evidence,
                IsBestGuess = guess,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return environment;
        }
    }

    /// <summary>The Unreal project folder for a <c>&lt;Project&gt;\Binaries\Win64\*.exe</c> layout, else null.</summary>
    internal static string? UnrealProjectDirectory(string exe)
    {
        var binaries = Directory.GetParent(Path.GetDirectoryName(exe)!);
        return binaries is { Parent: { } project } && binaries.Name.Equals("Binaries", StringComparison.OrdinalIgnoreCase)
            ? project.FullName
            : null;
    }

    private static (GraphicsApiType Api, string Evidence, bool Guess) ReadUnrealRhi(string exe, string? localAppData, string? documents)
    {
        var project = UnrealProjectDirectory(exe);
        if (project == null) return (GraphicsApiType.Unknown, "", true);
        var name = Path.GetFileName(project);
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        documents ??= Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var savedFolders = new[]
            {
                Path.Combine(localAppData, name, "Saved"),
                Path.Combine(project, "Saved"),
                Path.Combine(documents, "My Games", name, "Saved"),
            }
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // What the engine actually initialised last time outranks what its settings ask for.
        foreach (var saved in savedFolders)
        {
            var logs = Path.Combine(saved, "Logs");
            if (!Directory.Exists(logs)) continue;
            var newest = Directory.EnumerateFiles(logs, "*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (newest == null) continue;
            var api = UnrealRhiFromLog(ReadLog(newest, DateTime.MinValue));
            if (api != GraphicsApiType.Unknown)
                return (api, $"Unreal Engine log from the last time the game ran ({File.GetLastWriteTime(newest):d MMM yyyy}) shows {GraphicsApiDetector.GetLabel(api)}.", false);
        }

        foreach (var saved in savedFolders)
        {
            foreach (var platform in new[] { "Windows", "WindowsNoEditor", "WinGDK", "WindowsClient" })
            {
                foreach (var file in new[] { "GameUserSettings.ini", "Engine.ini" })
                {
                    var path = Path.Combine(saved, "Config", platform, file);
                    if (!File.Exists(path)) continue;
                    var api = UnrealRhiFromConfig(ReadLog(path, DateTime.MinValue));
                    if (api != GraphicsApiType.Unknown)
                        return (api, $"The game's Unreal Engine settings ({file}) select {GraphicsApiDetector.GetLabel(api)}.", true);
                }
            }
        }

        return (GraphicsApiType.Unknown, "", true);
    }

    /// <summary>Reads the renderer an Unreal Engine 4/5 log says it initialised.</summary>
    internal static GraphicsApiType UnrealRhiFromLog(string log)
    {
        if (string.IsNullOrEmpty(log)) return GraphicsApiType.Unknown;

        // UE5: "LogRHI: RHI D3D12 with Feature Level SM6 is supported and will be used."
        var chosen = Regex.Matches(log, @"RHI\s+(D3D12|D3D11|Vulkan|OpenGL)\b[^\r\n]*?\bwill be used", RegexOptions.IgnoreCase);
        if (chosen.Count > 0) return UnrealRhiName(chosen[^1].Groups[1].Value);

        // "LogRHI: Loading RHI module D3D12RHI" — the last module loaded is the one that stuck after any fallback.
        var loaded = Regex.Matches(log, @"Loading RHI module\s+(D3D12RHI|D3D11RHI|VulkanRHI|OpenGLDrv)", RegexOptions.IgnoreCase);
        if (loaded.Count > 0) return UnrealRhiName(loaded[^1].Groups[1].Value);

        // UE4: per-RHI log categories only appear for the RHI that is running.
        var counts = new[]
            {
                (Api: GraphicsApiType.DirectX12, Count: Regex.Matches(log, @"\bLogD3D12RHI:").Count),
                (Api: GraphicsApiType.DirectX11, Count: Regex.Matches(log, @"\bLogD3D11RHI:").Count),
                (Api: GraphicsApiType.Vulkan, Count: Regex.Matches(log, @"\bLogVulkanRHI:").Count),
                (Api: GraphicsApiType.OpenGL, Count: Regex.Matches(log, @"\bLogOpenGL(?:RHI)?:").Count),
            }
            .Where(entry => entry.Count > 0)
            .OrderByDescending(entry => entry.Count)
            .ToArray();
        return counts.Length > 0 ? counts[0].Api : GraphicsApiType.Unknown;
    }

    /// <summary>Reads <c>PreferredRHI=dx12</c> (UE5 GameUserSettings) or <c>DefaultGraphicsRHI=DefaultGraphicsRHI_DX12</c>.</summary>
    internal static GraphicsApiType UnrealRhiFromConfig(string ini)
    {
        if (string.IsNullOrEmpty(ini)) return GraphicsApiType.Unknown;
        var preferred = Regex.Match(ini, @"(?im)^\s*PreferredRHI\s*=\s*(dx12|dx11|vulkan|d3d12|d3d11)\s*$");
        if (preferred.Success) return UnrealRhiName(preferred.Groups[1].Value);
        var standard = Regex.Match(ini, @"(?im)^\s*DefaultGraphicsRHI\s*=\s*DefaultGraphicsRHI_(DX12|DX11|Vulkan)\s*$");
        return standard.Success ? UnrealRhiName(standard.Groups[1].Value) : GraphicsApiType.Unknown;
    }

    private static GraphicsApiType UnrealRhiName(string value) => value.ToLowerInvariant() switch
    {
        "d3d12" or "d3d12rhi" or "dx12" => GraphicsApiType.DirectX12,
        "d3d11" or "d3d11rhi" or "dx11" => GraphicsApiType.DirectX11,
        "vulkan" or "vulkanrhi" => GraphicsApiType.Vulkan,
        "opengl" or "opengldrv" => GraphicsApiType.OpenGL,
        _ => GraphicsApiType.Unknown,
    };

    // Renderer back-ends games ship next to the executable. Each one only exists for a single graphics API.
    private static readonly (string RelativePath, GraphicsApiType Api)[] ShippedRuntimeHints =
    {
        (Path.Combine("D3D12", "D3D12Core.dll"), GraphicsApiType.DirectX12),
        ("amd_fidelityfx_dx12.dll", GraphicsApiType.DirectX12),
        ("ffx_fsr2_api_dx12_x64.dll", GraphicsApiType.DirectX12),
        ("libxess_dx12.dll", GraphicsApiType.DirectX12),
        ("amd_fidelityfx_vk.dll", GraphicsApiType.Vulkan),
        ("ffx_fsr2_api_vk_x64.dll", GraphicsApiType.Vulkan),
        ("libxess_dx11.dll", GraphicsApiType.DirectX11),
    };

    private static (GraphicsApiType Api, string Evidence) ReadShippedRuntimeHints(string directory, HashSet<GraphicsApiType> supported)
    {
        var found = ShippedRuntimeHints
            .Where(hint => File.Exists(Path.Combine(directory, hint.RelativePath)))
            .Where(hint => supported.Count == 0 || supported.Contains(hint.Api))
            .ToArray();
        var apis = found.Select(hint => hint.Api).Distinct().ToArray();
        if (apis.Length != 1) return (GraphicsApiType.Unknown, "");
        var file = found[0].RelativePath;
        return (apis[0], apis[0] == GraphicsApiType.DirectX12 && file.StartsWith("D3D12", StringComparison.OrdinalIgnoreCase)
            ? "The game ships Microsoft's DirectX 12 Agility SDK (D3D12\\D3D12Core.dll), so it renders with DirectX 12."
            : $"The game ships a {GraphicsApiDetector.GetLabel(apis[0])}-only renderer component ({file}).");
    }

    /// <summary>The executable's import-table pick, else the most common default among the APIs it can use.</summary>
    internal static GraphicsApiType PickLikelyApi(string exe, HashSet<GraphicsApiType> supported)
    {
        var imported = GraphicsApiDetector.Detect(exe);
        if (imported != GraphicsApiType.Unknown && (supported.Count == 0 || supported.Contains(imported)))
            return imported;
        foreach (var api in new[]
                 {
                     GraphicsApiType.DirectX11, GraphicsApiType.DirectX12, GraphicsApiType.Vulkan, GraphicsApiType.DirectX9,
                     GraphicsApiType.OpenGL, GraphicsApiType.DirectX10, GraphicsApiType.DirectX8,
                 })
            if (supported.Contains(api)) return api;
        return GraphicsApiType.Unknown;
    }

    internal static bool IsDxgi(GraphicsApiType api) => api is GraphicsApiType.DirectX10 or GraphicsApiType.DirectX11 or GraphicsApiType.DirectX12;
    internal static string? ProxyFor(GraphicsApiType api) => api switch
    {
        GraphicsApiType.DirectX9 => "d3d9.dll",
        GraphicsApiType.OpenGL => "opengl32.dll",
        GraphicsApiType.DirectX10 or GraphicsApiType.DirectX11 or GraphicsApiType.DirectX12 => "dxgi.dll",
        _ => null,
    };

    internal static GraphicsEnvironment ApplyUserOverride(
        GraphicsEnvironment environment,
        GraphicsApiType? userOverride)
    {
        if (userOverride is null or GraphicsApiType.Unknown)
            return environment;

        var supported = new HashSet<GraphicsApiType>(environment.SupportedApis) { userOverride.Value };
        return environment with
        {
            Api = userOverride.Value,
            SupportedApis = supported,
            ReShadeProxy = ProxyFor(userOverride.Value),
            Evidence = "Renderer selected manually for this game; the override is being used for installation.",
        };
    }

    // A config or executable change invalidates previous launch evidence. No full-drive scanning.
    private static IEnumerable<string> ConfigurationFiles(string exe)
    {
        var root = Path.GetDirectoryName(exe)!;
        var data = Path.Combine(root, Path.GetFileNameWithoutExtension(exe) + "_Data");
        return Directory.EnumerateFiles(root).Where(path =>
                new[] { ".ini", ".cfg", ".config" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                && IsGameRendererConfiguration(Path.GetFileName(path)))
            .Concat(new[] { Path.Combine(data, "boot.config"), Path.Combine(root, "UnityPlayer.dll"), Path.Combine(root, "GameAssembly.dll") })
            .Where(File.Exists).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(256);
    }
    private static bool IsGameRendererConfiguration(string name)
        => !name.StartsWith("ReShade", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("dlss", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("renodx", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("OptiScaler", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("dgVoodoo", StringComparison.OrdinalIgnoreCase);
    private static string Fingerprint(string exe) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("|", ConfigurationFiles(exe).Prepend(exe).Select(path =>
        {
            var info = new FileInfo(path);
            return $"{path.ToUpperInvariant()}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        })))));
    private static string ObservationPath(string exe, string? directory) => Path.Combine(directory ?? ObservationDirectory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(exe).ToUpperInvariant()))) + ".json");

    internal static void SaveObservation(string exe, GraphicsApiType api, string? observationDirectory = null)
    {
        var path = ObservationPath(exe, observationDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Observation(Fingerprint(exe), api, DateTime.UtcNow, ObservationSchema)));
    }
    private static GraphicsApiType ReadObservation(string exe, string? directory)
    {
        try
        {
            var path = ObservationPath(exe, directory);
            if (!File.Exists(path)) return GraphicsApiType.Unknown;
            var observation = JsonSerializer.Deserialize<Observation>(File.ReadAllText(path));
            // Short-lived: launch options and user-profile settings can change outside the install directory.
            if (observation == null) return GraphicsApiType.Unknown;
            var age = DateTime.UtcNow - observation.ObservedUtc;
            if (observation.Fingerprint == Fingerprint(exe) && age < TimeSpan.FromDays(30))
                return observation.Api;
            // v2.6.22 included ReShade's own INI in this fingerprint. Migrate only
            // a very recent record and only when the executable itself supports it.
            if (observation.Schema == null && age < TimeSpan.FromHours(2)
                && GraphicsApiDetector.DetectAllApis(exe).Contains(observation.Api))
            {
                SaveObservation(exe, observation.Api, directory);
                return observation.Api;
            }
            return GraphicsApiType.Unknown;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return GraphicsApiType.Unknown; }
    }

    private static string ReadLog(string path, DateTime minimumTime)
    {
        if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < minimumTime) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var buffer = new char[2_000_000];
        var length = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, length);
    }
    private static GraphicsApiType ReadRuntimeApi(string exe, bool anyAge = false)
    {
        var root = Path.GetDirectoryName(exe)!;
        var since = ConfigurationFiles(exe).Prepend(exe).Select(File.GetLastWriteTimeUtc).Max();
        // Old logs are not evidence of the current launch/configuration — but they are still the best
        // record of how the game last rendered, which DetectWithBestGuess uses when nothing newer exists.
        since = anyAge ? DateTime.MinValue : new[] { since, DateTime.UtcNow.AddDays(-1) }.Max();
        var data = Path.Combine(root, Path.GetFileNameWithoutExtension(exe) + "_Data");
        var appInfo = Path.Combine(data, "app.info");
        if (File.Exists(appInfo))
        {
            var identity = File.ReadLines(appInfo).Take(2).ToArray();
            if (identity.Length == 2 && identity.All(IsSafeSegment))
            {
                var low = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", identity[0], identity[1]);
                var unity = ReadLog(Path.Combine(low, "Player.log"), since);
                // Anchor to engine initialization, not mentions in add-on diagnostics.
                var match = Regex.Match(unity, @"(?m)^\s*(Direct3D|OpenGL|Vulkan)\s*:\s*\r?\n\s*Version:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var family = match.Groups[1].Value;
                    if (family.Equals("Vulkan", StringComparison.OrdinalIgnoreCase)) return GraphicsApiType.Vulkan;
                    if (family.Equals("OpenGL", StringComparison.OrdinalIgnoreCase)) return GraphicsApiType.OpenGL;
                    var version = match.Groups[2].Value;
                    foreach (var number in new[] { 9, 10, 11, 12 })
                        if (Regex.IsMatch(version, $@"\bDirect3D\s*{number}(?:\.|\s|$)", RegexOptions.IgnoreCase))
                            return GraphicsApiDetector.ParseApiString("DX" + number);
                }
            }
        }
        var log = ReadLog(Path.Combine(root, "ReShade.log"), since);
        if (!log.Contains("into '" + exe + "'", StringComparison.OrdinalIgnoreCase)
            || !log.Contains("Recreated runtime environment", StringComparison.OrdinalIgnoreCase)) return GraphicsApiType.Unknown;
        var apis = new HashSet<GraphicsApiType>();
        foreach (var (marker, api) in new[]
        {
            ("Direct3DCreate9", GraphicsApiType.DirectX9), ("D3D10CreateDevice", GraphicsApiType.DirectX10),
            ("D3D11CreateDevice", GraphicsApiType.DirectX11), ("D3D12CreateDevice", GraphicsApiType.DirectX12),
            ("wglCreateContext", GraphicsApiType.OpenGL), ("vkCreateDevice", GraphicsApiType.Vulkan),
        })
            if (log.Contains("Redirecting " + marker + "(", StringComparison.OrdinalIgnoreCase)) apis.Add(api);
        return apis.Count == 1 ? apis.Single() : GraphicsApiType.Unknown;
    }
    private static bool IsSafeSegment(string value) => !string.IsNullOrWhiteSpace(value)
        && value != "." && value != ".." && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>Bounded background observation of the actual executable, never injects or changes the game.</summary>
    public static async Task ObserveLaunchAsync(string root, Action<GraphicsEnvironment> completed)
    {
        try
        {
            var initial = Detect(root);
            if (initial.Executable == null) return;
            var launchedUtc = DateTime.UtcNow;
            GraphicsApiType previous = GraphicsApiType.Unknown;
            var repetitions = 0;
            for (var attempt = 0; attempt < 15; attempt++)
            {
                await Task.Delay(2000).ConfigureAwait(false);
                var result = Detect(root);
                var localReShadeLog = Path.Combine(Path.GetDirectoryName(initial.Executable)!, "ReShade.log");
                if (result.Evidence.Contains("runtime log", StringComparison.Ordinal)
                    && File.Exists(localReShadeLog) && File.GetLastWriteTimeUtc(localReShadeLog) >= launchedUtc)
                { completed(result); return; }
                var apis = new HashSet<GraphicsApiType>();
                var running = false;
                foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(initial.Executable)))
                {
                    using (process)
                    {
                        try
                        {
                            if (!string.Equals(process.MainModule?.FileName, initial.Executable, StringComparison.OrdinalIgnoreCase)
                                || process.MainWindowHandle == IntPtr.Zero) continue;
                            running = true;
                            foreach (ProcessModule module in process.Modules)
                                if (RuntimeNames.TryGetValue(module.ModuleName, out var api)) apis.Add(api);
                        }
                        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { running = false; }
                    }
                }
                var candidate = running && apis.Count == 1 ? apis.Single() : GraphicsApiType.Unknown;
                repetitions = candidate != GraphicsApiType.Unknown && candidate == previous ? repetitions + 1 : 0;
                previous = candidate;
                if (repetitions < 2) continue;
                SaveObservation(initial.Executable, candidate);
                completed(Detect(root));
                return;
            }
            completed(Detect(root));
        }
        catch (Exception ex) { CrashReporter.Log("[GraphicsEnvironment] Launch observation: " + ex.Message); }
    }

    internal static bool IsReShade(string path)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(path);
            return (version.FileDescription ?? "").Contains("ReShade", StringComparison.OrdinalIgnoreCase)
                || (version.ProductName ?? "").Contains("ReShade", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return false; }
    }

    public static IReadOnlyList<string> CheckInstallation(
        string root,
        GraphicsApiType? userOverride = null)
    {
        var issues = new List<string>();
        var environment = ApplyUserOverride(Detect(root), userOverride);
        if (environment.Executable == null) return issues;
        root = Path.GetDirectoryName(environment.Executable)!;
        var proxies = ProxyNames.Select(name => Path.Combine(root, name)).Where(File.Exists).Where(IsReShade).ToArray();
        string[] addons;
        try { addons = Directory.GetFiles(root, "*.addon*"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { addons = []; issues.Add("Ada could not inspect add-ons: " + ex.Message); }
        var files = addons.Concat(proxies);
        foreach (var path in files)
        {
            var machine = Pe.DetectArchitecture(path);
            if (machine is MachineType.I386 or MachineType.x64 && environment.Machine is MachineType.I386 or MachineType.x64 && machine != environment.Machine)
                issues.Add($"{Path.GetFileName(path)} is {(machine == MachineType.I386 ? "32-bit" : "64-bit")}, but this game executable is {(environment.Machine == MachineType.I386 ? "32-bit" : "64-bit")}.");
        }
        Dlss5InstallRecord? record = null;
        try { record = Dlss5ComponentService.LoadRecord(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { issues.Add("The DLSS installation record is unreadable: " + ex.Message); }
        // Translation deliberately puts the hook on the translated API; don't flag the wrapper as the game's original API.
        var translated = record?.Mode is Dlss5DeploymentMode.Dx8Feeder or Dlss5DeploymentMode.Dx9Feeder
            or Dlss5DeploymentMode.Dx10ViaDxvkFeeder or Dlss5DeploymentMode.Dx9ViaDxvkFeeder;
        if (!translated)
        {
            foreach (var path in proxies.Where(path => !Path.GetFileName(path).StartsWith("ReShade", StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileName(path);
                if (environment.ReShadeProxy != null && !name.Equals(environment.ReShadeProxy, StringComparison.OrdinalIgnoreCase)
                    && !(IsDxgi(environment.Api) && name is "d3d10.dll" or "d3d11.dll" or "d3d12.dll"))
                    issues.Add($"ReShade is installed as {name}, but the detected renderer needs {environment.ReShadeProxy}.");
            }
            if (proxies.Length > 1) issues.Add("Multiple ReShade runtime DLLs are present. Review and repair the duplicate hooks before launching.");
        }
        if (record != null && environment.Api != GraphicsApiType.Unknown && ApiForMode(record.Mode) != environment.Api)
            issues.Add($"The installed DLSS route ({record.Mode}) does not match the detected {GraphicsApiDetector.GetLabel(environment.Api)} renderer. Repair will select the current route.");
        return issues;
    }

    internal static GraphicsApiType ApiForMode(Dlss5DeploymentMode mode) => mode switch
    {
        Dlss5DeploymentMode.NativeDirectX12 or Dlss5DeploymentMode.Dx12Feeder => GraphicsApiType.DirectX12,
        Dlss5DeploymentMode.NativeDirectX11 or Dlss5DeploymentMode.Dx11Feeder => GraphicsApiType.DirectX11,
        Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.NativeVulkan => GraphicsApiType.Vulkan,
        Dlss5DeploymentMode.OpenGlFeeder => GraphicsApiType.OpenGL,
        Dlss5DeploymentMode.Dx9Feeder => GraphicsApiType.DirectX9,
        Dlss5DeploymentMode.Dx9ViaDxvkFeeder => GraphicsApiType.DirectX9,
        Dlss5DeploymentMode.Dx8Feeder => GraphicsApiType.DirectX8,
        Dlss5DeploymentMode.Dx10ViaDxvkFeeder => GraphicsApiType.DirectX10,
        Dlss5DeploymentMode.Dx10Feeder => GraphicsApiType.DirectX10,
        _ => GraphicsApiType.Unknown,
    };
}
