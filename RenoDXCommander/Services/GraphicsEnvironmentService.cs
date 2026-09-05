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
    bool OpenXrDetected = false);

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
    private static GraphicsApiType ReadRuntimeApi(string exe)
    {
        var root = Path.GetDirectoryName(exe)!;
        var since = ConfigurationFiles(exe).Prepend(exe).Select(File.GetLastWriteTimeUtc).Max();
        // Old logs are not evidence of the current launch/configuration.
        since = new[] { since, DateTime.UtcNow.AddDays(-1) }.Max();
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
