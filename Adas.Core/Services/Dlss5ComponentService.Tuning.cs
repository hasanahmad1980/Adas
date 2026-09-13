using System.Globalization;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public enum Dlss5TuningPreset { Quality, Balanced, Performance }

/// <summary>Current tuning of an installed game, read from the files Adas manages.</summary>
public sealed record Dlss5TuningState(
    bool HasFeeder,
    bool WorkResolutionApplies,
    int WorkResolution,
    int WorkUpscale,
    double WorkSharpness,
    bool HasAio,
    bool AioFrameGenerationAvailable,
    IReadOnlyDictionary<string, string> Aio);

/// <summary>One row of the "What will happen?" preview.</summary>
public sealed record Dlss5PlannedChange(Dlss5PlannedAction Action, string Path, string Note);

public enum Dlss5PlannedAction { Write, BackUp, Remove, Setting }

public sealed partial class Dlss5ComponentService
{
    /// <summary>Sentinel tag: resolve the newest published pre-release at install time.</summary>
    public const string NewestPrereleaseTag = "newest-prerelease";

    public const int MinWorkResolution = 50;
    public const int MaxWorkResolution = 100;

    private const string VortProviderTechnique = "vort_MotionEffects@vort_Motion.fx";
    private const string LumeniteProviderTechnique = "Lumenite_Kernel@lumenite_Kernel.fx";

    internal static string MotionProviderDefinition(Dlss5MotionProvider provider)
        => provider == Dlss5MotionProvider.VortMotion ? "DLSS5_MV_PROVIDER=2" : "DLSS5_MV_PROVIDER=3";

    internal static string MotionProviderTechnique(Dlss5MotionProvider provider)
        => provider == Dlss5MotionProvider.VortMotion ? VortProviderTechnique : LumeniteProviderTechnique;

    // ── #3 Tuning ─────────────────────────────────────────────────────────────

    /// <summary>Feeder work-area percentage for a built-in preset. 75% costs about half, 50% about a quarter.</summary>
    public static int WorkResolutionFor(Dlss5TuningPreset preset) => preset switch
    {
        Dlss5TuningPreset.Performance => 50,
        Dlss5TuningPreset.Balanced => 75,
        _ => 100,
    };

    /// <summary>
    /// Estimated work area that reaches <paramref name="targetFps"/> from <paramref name="currentFps"/> measured at
    /// <paramref name="currentWorkResolution"/>. Neural cost scales with pixel count, i.e. the square of the axis
    /// percentage; the answer is rounded to 5% and clamped to the Feeder's 50–100% range.
    /// </summary>
    public static int EstimateWorkResolutionForTarget(double currentFps, double targetFps, int currentWorkResolution = 100)
    {
        if (!double.IsFinite(currentFps) || !double.IsFinite(targetFps) || currentFps <= 0 || targetFps <= 0)
            return Math.Clamp(currentWorkResolution, MinWorkResolution, MaxWorkResolution);
        var percent = Math.Clamp(currentWorkResolution, MinWorkResolution, MaxWorkResolution) * Math.Sqrt(currentFps / targetFps);
        var rounded = (int)(Math.Round(percent / 5d) * 5);
        return Math.Clamp(rounded, MinWorkResolution, MaxWorkResolution);
    }

    /// <summary>Approximate relative neural cost of a work area (1.0 at 100%).</summary>
    public static double RelativeWorkCost(int workResolution)
    {
        var fraction = Math.Clamp(workResolution, MinWorkResolution, MaxWorkResolution) / 100d;
        return fraction * fraction;
    }

    public static Dlss5TuningState ReadTuning(string root)
    {
        var record = LoadRecord(root);
        var cfgPath = Path.Combine(root, FeederConfig);
        var hasFeeder = record != null && IsFeederMode(record.Mode) && File.Exists(cfgPath);
        var cfg = hasFeeder ? ReadConfig(cfgPath) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int Int(string key, int fallback) => cfg.TryGetValue(key, out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        var sharpness = cfg.TryGetValue("work_sharpness", out var sharpText)
            && double.TryParse(sharpText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0.3;

        var hasAio = record?.Profile == Dlss5InstallProfile.StandaloneAio;
        var aio = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (hasAio)
        {
            var ini = IniTextDocument.Load(Path.Combine(root, "ReShade.ini"));
            foreach (var (key, fallback) in AioDefaults)
                aio[key] = ini.TryGetValue(AioSection, key, out var value) ? value.Text.Trim() : fallback;
        }
        return new Dlss5TuningState(
            hasFeeder,
            // The Feeder only honours work_resolution on its D3D11 transport (64- and 32-bit).
            hasFeeder && record!.Mode is Dlss5DeploymentMode.Dx11Feeder or Dlss5DeploymentMode.Dx9Feeder
                or Dlss5DeploymentMode.Dx8Feeder or Dlss5DeploymentMode.Dx10Feeder,
            Math.Clamp(Int("work_resolution", 100), MinWorkResolution, MaxWorkResolution),
            Math.Clamp(Int("work_upscale", 0), 0, 1),
            Math.Clamp(sharpness, 0, 1),
            hasAio,
            hasAio && File.Exists(Path.Combine(root, "nvngx_dlssg.dll")),
            aio);
    }

    /// <summary>
    /// Validates and writes Feeder tuning (<c>work_resolution</c> 50–100, <c>work_upscale</c> 0/1,
    /// <c>work_sharpness</c> 0–1) into the Adas-owned <c>dlss5-feed.cfg</c>, keeping its ownership hash current
    /// so Repair and Remove still recognise the file.
    /// </summary>
    public static void SaveFeederTuning(string root, IReadOnlyDictionary<string, string> settings)
    {
        var record = LoadRecord(root);
        var cfgPath = Path.Combine(root, FeederConfig);
        if (record == null || !IsFeederMode(record.Mode) || !File.Exists(cfgPath))
            throw new InvalidOperationException("This game does not have an Adas-managed DLSS5-Feeder setup.");
        foreach (var (key, value) in settings)
        {
            (double Min, double Max, bool Whole) range = key switch
            {
                "work_resolution" => (MinWorkResolution, MaxWorkResolution, true),
                "work_upscale" => (0, 1, true),
                "work_sharpness" => (0, 1, false),
                _ => throw new ArgumentException($"Unsupported Feeder setting: {key}"),
            };
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || !double.IsFinite(number) || number < range.Min || number > range.Max
                || (range.Whole && number != Math.Truncate(number)))
                throw new ArgumentException($"Invalid Feeder value for {key}.");
        }
        var document = IniTextDocument.Load(cfgPath);
        foreach (var (key, value) in settings) document.SetValue("", key, value);
        var temporary = Path.Combine(Path.GetTempPath(), $"adas-feed-tuning-{Guid.NewGuid():N}.cfg");
        try
        {
            document.Save(temporary);
            InstallTrackedFile(temporary, cfgPath, root, record);
        }
        finally { DeleteIfExists(temporary); }
    }

    // ── #4 Feeder build channel ───────────────────────────────────────────────

    /// <summary>
    /// Picks the release for a channel tag from a GitHub <c>/releases</c> array: the first non-draft pre-release
    /// for <see cref="NewestPrereleaseTag"/>, otherwise the release whose tag matches exactly.
    /// </summary>
    internal static JsonElement? SelectFeederRelease(JsonElement releases, string tag)
    {
        if (releases.ValueKind != JsonValueKind.Array) return null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
            var name = release.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() : null;
            if (tag == NewestPrereleaseTag)
            {
                if (release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
                    return release.Clone();
            }
            else if (string.Equals(name, tag, StringComparison.OrdinalIgnoreCase))
            {
                return release.Clone();
            }
        }
        return null;
    }

    /// <summary>Adas pairs Feeder builds before 0.8 with RenoDX 4.55 and pins OpenGL to its bundled bridge.</summary>
    public static string DescribeFeederPairing(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return $"Packaged builds: stable Feeder {BundledFeederVersion}, test Feeder {BundledFeederBetaVersion}. Both are paired with RenoDX 4.55.";
        if (tag == NewestPrereleaseTag)
            return "Adas downloads the newest Feeder pre-release from GitHub at install time and pairs it with RenoDX 4.55. OpenGL games keep the packaged bridge.";
        var numeric = tag.TrimStart('v', 'V');
        var old = Version.TryParse(numeric.Split('-', '+')[0], out var version) && version < new Version(0, 8);
        return old
            ? $"Feeder {tag} is older than 0.8 — Adas pairs it with RenoDX 4.55."
            : $"Feeder {tag} is downloaded from GitHub at install time and paired with RenoDX 4.55.";
    }

    // ── #13 What will happen? ────────────────────────────────────────────────

    /// <summary>
    /// Read-only preview of an install: the files Adas would write, the existing files it would back up first,
    /// the files it would remove or move aside, and the settings it would change. Nothing is written.
    /// </summary>
    public static IReadOnlyList<Dlss5PlannedChange> PreviewInstall(
        string root,
        Dlss5DeploymentMode mode,
        bool is64Bit,
        Dlss5InstallProfile profile,
        Dlss5MotionProvider provider = Dlss5MotionProvider.LumeniteKernel,
        bool deepFriedChicken = false)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        profile = NormalizeProfileForMode(mode, is64Bit, profile);
        var record = LoadRecord(root);
        var cleanup = GetCleanupPlan(root, mode, profile);
        var changes = new List<Dlss5PlannedChange>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (cleanup.RemoveRecordedInstall && record != null)
        {
            foreach (var path in record.InstalledHashes.Keys.Where(seen.Add))
                changes.Add(new(Dlss5PlannedAction.Remove, path,
                    record.OriginalBackups.TryGetValue(path, out var backup) && backup != null
                        ? "previous setup — original restored"
                        : "previous setup"));
        }
        foreach (var file in cleanup.Files.Where(file => seen.Add(file.Path)))
            changes.Add(new(Dlss5PlannedAction.Remove, file.Path, "conflicting file — moved to .adas\\preserved"));

        var addonRoot = ModInstallService.GetAddonDeployPath(root);
        var shaders = Path.Combine(root, "reshade-shaders", "Shaders");
        var writes = new List<(string Path, string Note)>();
        void Add(string path, string note) => writes.Add((path, note));

        if (profile == Dlss5InstallProfile.StandaloneAio)
        {
            if (!IsAioVulkan(mode)) Add(Path.Combine(root, AioProxyName(mode)), "ReShade 6.8");
            Add(Path.Combine(addonRoot, AioAddon), $"Standalone AIO {AioVersion}");
            Add(Path.Combine(addonRoot, "nvngx.dll"), "AIO caller");
            Add(Path.Combine(shaders, AioShader), "AIO shader");
            foreach (var runtime in new[] { "nvngx_dlssnr.dll", "nvngx_dlss.dll", "nvngx_dlssg.dll" })
                if (!File.Exists(Path.Combine(root, runtime))) Add(Path.Combine(root, runtime), "NVIDIA runtime (only if missing)");
            Add(Path.Combine(shaders, "VortShaders", "vort_Motion.fx"), "VORT motion shaders");
            Add(Path.Combine(root, "ReShadePreset.ini"), "preset");
        }
        else if (IsOptiScalerNrProfile(profile))
        {
            Add(Path.Combine(root, "OptiScaler.dll"), "OptiScaler build for this route (installed as the game's proxy DLL)");
            Add(Path.Combine(root, "OptiScaler.ini"), "OptiScaler settings");
            Add(Path.Combine(root, "nvngx.dll_dlssnr.dll"), "neural rendering runtime");
        }
        else
        {
            var plan = GetCompatibilityPlan(mode, is64Bit, profile);
            if (mode is not (Dlss5DeploymentMode.Dx10ViaDxvkFeeder or Dlss5DeploymentMode.Dx9ViaDxvkFeeder))
                Add(Path.Combine(root, GetReShadeFileName(mode, profile)), $"ReShade {BundledStableReShadeVersion}");
            if (mode is Dlss5DeploymentMode.Dx9Feeder or Dlss5DeploymentMode.Dx8Feeder)
                Add(Path.Combine(root, mode == Dlss5DeploymentMode.Dx8Feeder ? "d3d8.dll" : "d3d9.dll"), "dgVoodoo2 (DirectX → D3D11)");
            if (mode is Dlss5DeploymentMode.Dx10ViaDxvkFeeder or Dlss5DeploymentMode.Dx9ViaDxvkFeeder)
                Add(Path.Combine(root, mode == Dlss5DeploymentMode.Dx9ViaDxvkFeeder ? "d3d9.dll" : "d3d10core.dll"), "DXVK (DirectX → Vulkan)");
            if (is64Bit)
                Add(Path.Combine(addonRoot, deepFriedChicken ? "Deep Fried Chicken add-on" : RenoDxDeploymentName),
                    deepFriedChicken ? "neural consumer" : $"RenoDX neural consumer ({plan.RenoDxPackage})");
            if (plan.InstallDx11Bridge) Add(Path.Combine(addonRoot, BridgeAddon), $"DLSS 5 Bridge {BridgeVersion}");
            if (plan.InstallOpenGlBridge) Add(Path.Combine(addonRoot, OpenGlBridgeAddon), $"OpenGL Bridge {OpenGlBridgeVersion}");
            if (mode == Dlss5DeploymentMode.NativeVulkan) Add(Path.Combine(root, BridgeConfig), "bridge settings");
            if (plan.InstallFeeder)
            {
                Add(Path.Combine(addonRoot, is64Bit ? FeederAddon : FeederAddon32), "DLSS5-Feeder add-on");
                if (!is64Bit)
                {
                    Add(Path.Combine(root, "host64", FeederHost64), "64-bit Feeder helper");
                    Add(Path.Combine(root, "host64", GetReShadeFileName(mode)), "64-bit ReShade for the helper");
                }
                Add(Path.Combine(shaders, FeederShader), "Feeder shader");
                Add(Path.Combine(shaders, "ReShade.fxh"), "ReShade framework header");
                Add(Path.Combine(root, FeederConfig), "Feeder settings (kept if already present)");
                if (provider == Dlss5MotionProvider.VortMotion)
                    Add(Path.Combine(shaders, "VortShaders", "vort_Motion.fx"), "VORT Motion provider + includes (DLSS5_MV_PROVIDER=2)");
                else
                    Add(Path.Combine(shaders, "lumenite_Kernel.fx"), "LumeniteFX Kernel provider + includes (DLSS5_MV_PROVIDER=3, downloaded)");
                Add(Path.Combine(root, "ReShadePreset.ini"), "preset: provider above DLSS5_Feed");
                if (mode is Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.Dx10ViaDxvkFeeder or Dlss5DeploymentMode.Dx9ViaDxvkFeeder)
                    Add(Path.Combine(root, "DLSS5-Vulkan-Fallback"), "Vulkan fallback launcher folder");
            }
        }

        foreach (var (path, note) in writes)
        {
            if (!seen.Add(path)) { changes.Add(new(Dlss5PlannedAction.Write, path, note + " — replaces the previous setup's copy")); continue; }
            var owned = record?.InstalledHashes.ContainsKey(path) == true;
            if (File.Exists(path) && !owned)
                changes.Add(new(Dlss5PlannedAction.BackUp, path, "existing file — copied to .adas\\backups before it is replaced"));
            changes.Add(new(Dlss5PlannedAction.Write, path, note));
        }

        changes.Add(new(Dlss5PlannedAction.Setting, Path.Combine(root, "ReShade.ini"),
            profile == Dlss5InstallProfile.StandaloneAio
                ? $"[{AioSection}] defaults, shader search paths, preset path — every original value is recorded for Remove"
                : "add-on and shader search paths, preset path — every original value is recorded for Remove"));
        changes.Add(new(Dlss5PlannedAction.Write, Path.Combine(root, RecordRelativePath.Replace('/', Path.DirectorySeparatorChar)), "install record used by Repair and Remove"));
        return changes;
    }

    public static string FormatPreview(string root, IReadOnlyList<Dlss5PlannedChange> changes)
    {
        string Rel(string path)
        {
            try { return Path.GetRelativePath(root, path); } catch { return path; }
        }
        var lines = new List<string>();
        foreach (var (action, title) in new[]
                 {
                     (Dlss5PlannedAction.Remove, "Removes or moves aside"),
                     (Dlss5PlannedAction.BackUp, "Backs up first"),
                     (Dlss5PlannedAction.Write, "Writes"),
                     (Dlss5PlannedAction.Setting, "Changes settings in"),
                 })
        {
            var group = changes.Where(change => change.Action == action).ToList();
            if (group.Count == 0) continue;
            if (lines.Count > 0) lines.Add("");
            lines.Add($"{title} ({group.Count}):");
            lines.AddRange(group.Select(change => $"• {Rel(change.Path)} — {change.Note}"));
        }
        lines.Add("");
        lines.Add("Nothing has been changed. Remove restores every backed-up file and setting.");
        return string.Join(Environment.NewLine, lines);
    }

    // ── #15 Hotkeys and tips ─────────────────────────────────────────────────

    public static IReadOnlyList<string> GetPostInstallTips(Dlss5DeploymentMode mode, Dlss5InstallProfile profile, bool optiScalerInstalled = false)
    {
        var tips = new List<string>();
        if (IsOptiScalerNrProfile(profile))
        {
            tips.Add("Insert — open the OptiScaler menu (neural rendering and upscaler settings).");
        }
        else if (profile == Dlss5InstallProfile.StandaloneAio)
        {
            tips.Add("Home — open ReShade → Add-ons → Standalone DLSS-NR + SR.");
            tips.Add("F10 — compare the processed and original picture.");
            tips.Add("Hold F8 while the game starts to recover from a bad previous session.");
            tips.Add("Turn the game's own DLSS, frame generation and anti-aliasing off.");
        }
        else
        {
            tips.Add("F6 — toggle neural rendering on and off.");
            tips.Add("F5 — take an add-on screenshot.");
            tips.Add("Home — open ReShade → Add-ons → DLSS 5 Neural Rendering.");
        }
        if (optiScalerInstalled && !IsOptiScalerNrProfile(profile))
            tips.Add("Insert — open the OptiScaler menu.");
        if (IsFeederMode(mode))
            tips.Add("Use borderless or windowed mode: alt-tabbing out of exclusive fullscreen can crash the Feeder when it creates its DLSS feature.");
        tips.Add("Turn v-sync off in the game for the most consistent frame pacing.");
        return tips;
    }
}
