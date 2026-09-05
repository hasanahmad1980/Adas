using System.Text.RegularExpressions;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Converts the installed file state and the game's DLSS/ReShade logs into a
/// short, user-facing health report. This deliberately diagnoses only the
/// Adas-managed DLSS 5 route and never edits game files.
/// </summary>
internal static partial class Dlss5DiagnosticService
{
    private const int MaximumLogCharacters = 2_000_000;

    public static Dlss5DiagnosticReport Diagnose(
        string deploymentPath,
        Dlss5DeploymentMode mode,
        bool is64Bit)
    {
        var problems = new List<string>();
        var notes = new List<string>();
        problems.AddRange(GraphicsEnvironmentService.CheckInstallation(deploymentPath));
        problems.AddRange(VerifyInstallation(deploymentPath, mode, is64Bit));

        Dlss5InstallRecord? installedRecord;
        try { installedRecord = Dlss5ComponentService.LoadRecord(deploymentPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(true, false, "The installation record could not be read.", Distinct(problems.Append(ex.Message)));
        }
        if (Dlss5ComponentService.IsOptiScalerNrProfile(installedRecord?.Profile))
            return new(problems.Count > 0, false, problems.Count > 0 ? "OptiScaler NR needs attention." : "OptiScaler NR files verified; picture not yet verified.",
                Distinct(problems.Append("Enable the game's DLSS, press Insert and check DLSS Neural Rendering. Use OptiScaler.log from this game for runtime diagnosis. ReShade/Feeder log messages do not verify this separate pipeline.")));
        if (installedRecord?.Profile == Dlss5InstallProfile.StandaloneAio)
        {
            // AIO's log is shared across games. Never attribute another game's frames to this one.
            notes.Add("File checks cannot confirm a visible or correct picture. Restart this game and check the Standalone DLSS-NR + SR status in ReShade; compare with F10.");
            notes.Add("NR, DLAA/upscaling and frame generation are independent. Native resolution means DLAA; upscaling requires a smaller game backbuffer. Disable the game's own DLSS, frame generation and antialiasing.");
            notes.Add("AIO's shared diagnostic log is at %LOCALAPPDATA%\\RHI\\Logs\\standalone-dlssnr.log. It is not automatically treated as evidence for this game.");
            return new(problems.Count > 0, false, problems.Count > 0 ? "Standalone AIO needs attention." : "Standalone AIO files verified; picture not yet verified.", Distinct(problems.Concat(notes)));
        }

        var logs = ReadLogs(deploymentPath);
        var working = SuccessfulFramesRegex().IsMatch(logs);
        AddKnownLogFindings(logs, problems, notes);
        if (installedRecord?.Profile == Dlss5InstallProfile.ExperimentalUnified)
            notes.Add("The direct RenoDX method does not use ReShade effect files. The Home tab may say no effect files; configure neural rendering from the RenoDX DLSS tab.");

        if (problems.Count > 0)
            return new(true, working, "Adas found a DLSS 5 setup problem.", Distinct(problems.Concat(notes)));
        if (working)
            return new(false, true, "The logs report DLSS 5 processing. This does not verify the visible picture or its quality.", Distinct(notes));

        if (string.IsNullOrWhiteSpace(logs))
            notes.Add("The files are complete. Start the game and load into gameplay once so Adas can verify the live DLSS session.");
        else if (ContainsAny(logs, "NO DLSS CREATE SEEN", "WAITING FOR NGX MODULES", "No frames delivered yet"))
            notes.Add("The add-ons loaded, but the game has not created a usable DLSS feature yet. Enable the game's upscaler and load into gameplay.");
        else
            notes.Add("The files are complete, but the logs do not yet show a successful neural-rendered frame.");

        return new(false, false, "The installation is complete and ready to test in game.", Distinct(notes));
    }

    public static IReadOnlyList<string> VerifyInstallation(
        string deploymentPath,
        Dlss5DeploymentMode mode,
        bool is64Bit)
    {
        var problems = new List<string>();
        VerifyFiles(deploymentPath, mode, is64Bit, problems);
        return Distinct(problems);
    }

    private static void VerifyFiles(
        string root,
        Dlss5DeploymentMode mode,
        bool is64Bit,
        ICollection<string> problems)
    {
        Dlss5InstallRecord? record;
        try { record = Dlss5ComponentService.LoadRecord(root); }
        catch (Exception ex)
        {
            problems.Add($"The Adas installation record is damaged: {ex.Message}");
            return;
        }

        if (record == null)
        {
            problems.Add("Adas cannot find its DLSS 5 installation record. Run Repair automatically.");
            return;
        }

        if (Dlss5ComponentService.IsOptiScalerNrProfile(record.Profile))
        {
            if (!is64Bit) problems.Add("OptiScaler NR cannot run inside a 32-bit game.");
            foreach (var name in new[] { Dlss5ComponentService.OptiScalerNrProxy(mode), "nvngx.dll_dlssnr.dll", "nvngx_dlssnr.dll", "OptiScaler.ini" })
                if (!File.Exists(Path.Combine(root, name))) problems.Add($"Missing OptiScaler NR file: {name}");
            foreach (var (file, hash) in record.InstalledHashes.Where(item => item.Key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                if (!File.Exists(file) || !FileHelper.ComputeSha256(file).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    problems.Add($"Changed or missing OptiScaler NR file: {Path.GetRelativePath(root, file)}");
                else if (!AddonPackService.IsAddonArchitectureCompatible(file, false))
                    problems.Add($"Wrong architecture: {Path.GetFileName(file)}");
            }
            return;
        }
        if (record.Profile == Dlss5InstallProfile.StandaloneAio)
        {
            VerifyAioFiles(root, mode, is64Bit, record, problems);
            return;
        }

        var required = new List<string>();
        var plan = Dlss5ComponentService.GetCompatibilityPlan(mode, is64Bit, record.Profile);
        if (plan.InstallFeeder && mode is (Dlss5DeploymentMode.Dx8Feeder or Dlss5DeploymentMode.Dx9Feeder))
        {
            required.Add(Path.Combine(root, mode == Dlss5DeploymentMode.Dx8Feeder ? "D3D8.dll" : "D3D9.dll"));
            required.Add(Path.Combine(root, "dgVoodoo.conf"));
        }
        var addonPath = ModInstallService.GetAddonDeployPath(root);
        if (mode is not (Dlss5DeploymentMode.Dx10ViaDxvkFeeder or Dlss5DeploymentMode.Dx9ViaDxvkFeeder))
            required.Add(Path.Combine(root, Dlss5ComponentService.GetReShadeFileName(mode, record.Profile)));

        if (mode == Dlss5DeploymentMode.Dx9ViaDxvkFeeder)
        {
            required.Add(Path.Combine(root, "d3d9.dll"));
            required.Add(Path.Combine(root, "reshade.ini"));
            required.Add(Path.Combine(root, VulkanFootprintService.FootprintFileName));
        }

        if (plan.InstallFeeder)
        {
            required.Add(Path.Combine(addonPath, is64Bit
                ? Dlss5ComponentService.FeederAddon
                : Dlss5ComponentService.FeederAddon32));
            required.Add(Path.Combine(root, "reshade-shaders", "Shaders", Dlss5ComponentService.FeederShader));
            required.AddRange(new[] { "ReShade.fxh", "ReShadeUI.fxh", "DrawText.fxh" }
                .Select(name => Path.Combine(root, "reshade-shaders", "Shaders", name)));

            if (!is64Bit)
            {
                var host = Path.Combine(root, "host64");
                required.Add(Path.Combine(host, Dlss5ComponentService.FeederHost64));
                required.Add(Path.Combine(host, "dxgi.dll"));
                required.Add(Path.Combine(host, "renodx-dlss5.addon64"));
                required.Add(Path.Combine(host, "nvngx_dlss.dll"));
                required.Add(Path.Combine(host, "nvngx_dlssnr.dll"));
            }
            else
            {
                required.Add(Path.Combine(addonPath, record.Profile == Dlss5InstallProfile.ExperimentalUnified
                    ? Renodx5AddonService.AddonFileName
                    : "renodx-dlss5.addon64"));
                required.Add(Path.Combine(root, "nvngx_dlss.dll"));
                required.Add(Path.Combine(root, "nvngx_dlssnr.dll"));
            }
        }
        else
        {
            var renoDxName = record.Profile == Dlss5InstallProfile.ExperimentalUnified
                ? Renodx5AddonService.AddonFileName
                : "renodx-dlss5.addon64";
            required.Add(Path.Combine(addonPath, renoDxName));
            if (mode is Dlss5DeploymentMode.NativeDirectX11 or Dlss5DeploymentMode.NativeVulkan)
                required.Add(Path.Combine(addonPath, Dlss5ComponentService.BridgeAddon));
            if (plan.InstallOpenGlBridge)
                required.Add(Path.Combine(addonPath, Dlss5ComponentService.OpenGlBridgeAddon));
        }

        foreach (var path in required.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var isRuntime = Path.GetFileName(path).StartsWith("nvngx_", StringComparison.OrdinalIgnoreCase);
            if (!File.Exists(path) || isRuntime && !Dlss5ComponentService.IsUsableRuntimeFile(path))
            {
                problems.Add($"A required file is missing or may have been quarantined: {Path.GetRelativePath(root, path)}. Run Repair automatically.");
                continue;
            }

            if (record.InstalledHashes.TryGetValue(path, out var expected)
                && !FileHelper.ComputeSha256(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
                problems.Add($"A managed file changed after installation: {Path.GetRelativePath(root, path)}. Run Repair automatically.");
        }

        if (!is64Bit && Dlss5CompatibilityService.IsFeederMode(mode))
        {
            var feeder32 = Path.Combine(addonPath, Dlss5ComponentService.FeederAddon32);
            var host64 = Path.Combine(root, "host64", Dlss5ComponentService.FeederHost64);
            if (File.Exists(feeder32) && !AddonPackService.IsAddonArchitectureCompatible(feeder32, is32Bit: true))
                problems.Add("The game-side Feeder has the wrong architecture. Repair will restore the matched 32-bit add-on.");
            if (File.Exists(host64) && AddonPackService.IsAddonArchitectureCompatible(host64, is32Bit: true))
                problems.Add("The Feeder helper has the wrong architecture. Repair will restore the matched 64-bit host.");

            var expectedVersion = record.ComponentVersion ?? "";
            if (!expectedVersion.Contains("Feeder 0.7.0", StringComparison.OrdinalIgnoreCase)
                && !expectedVersion.Contains("Feeder 0.11.0-beta.2", StringComparison.OrdinalIgnoreCase)
                && !expectedVersion.Contains($"Feeder {Dlss5ComponentService.BundledFeederBetaVersion}", StringComparison.OrdinalIgnoreCase)
                && !expectedVersion.Contains("local-user-import", StringComparison.OrdinalIgnoreCase))
                problems.Add("The 32-bit add-on and 64-bit host are not recorded as one supported matched Feeder release. Run Repair automatically.");
        }
    }

    private static string ReadLogs(string root)
    {
        var candidates = new[]
        {
            Path.Combine(root, "ReShade.log"),
            Path.Combine(root, Dlss5ComponentService.FeederLog),
            Path.Combine(root, Dlss5ComponentService.BridgeLog),
            Path.Combine(root, "host64", "ReShade.log"),
            Path.Combine(root, "host64", Dlss5ComponentService.FeederLog),
            Path.Combine(root, "host64", "dlss5-feed-host.log"),
        };
        var parts = new List<string>();
        var installedAt = Dlss5ComponentService.LoadRecord(root)?.InstalledAtUtc ?? DateTime.MinValue;
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < installedAt) continue;
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                file.Seek(Math.Max(0, file.Length - MaximumLogCharacters), SeekOrigin.Begin);
                using var reader = new StreamReader(file);
                parts.Add(reader.ReadToEnd());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return string.Join(Environment.NewLine, parts);
    }

    private static void AddKnownLogFindings(string log, ICollection<string> problems, ICollection<string> notes)
    {
        if (ContainsAny(log, "error code 193", "ERROR_BAD_EXE_FORMAT"))
            problems.Add("ReShade tried to load a 64-bit add-on in a 32-bit process, or the reverse. Run Repair automatically.");
        if (log.Contains("the 64-bit host went away", StringComparison.OrdinalIgnoreCase))
            problems.Add("The 32-bit Feeder lost its 64-bit helper. Repair the matched Feeder pair, then retry with antivirus exclusions if the helper disappears again.");
        if (ContainsAny(log, "different releases", "protocol mismatch", "mixed halves refuse"))
            problems.Add("The 32-bit add-on and 64-bit helper came from different Feeder releases. Run Repair automatically to deploy one matched pair.");
        if (log.Contains("could not open included file 'ReShade", StringComparison.OrdinalIgnoreCase))
            problems.Add("The standard ReShade shader headers are missing. Run Repair automatically.");
        if (log.Contains("cannot sample from texture that is also used as render target", StringComparison.OrdinalIgnoreCase))
            problems.Add("The selected motion-vector shader is incompatible with this ReShade path. Repair will restore the supported LumeniteFX provider.");
        if (log.Contains("DLSS is not available on this GPU/driver", StringComparison.OrdinalIgnoreCase))
            problems.Add("The DLSS runtime rejected the current GPU or driver. Update the NVIDIA driver and verify that the selected runtime supports this RTX generation.");
        if (log.Contains("616.64", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(log, "v4.6", "v4.7")
            && ContainsAny(log, "D3D12Core.dll", "evaluate raised 0xC0000005", "nothing submitted"))
            problems.Add("NVIDIA driver 616.64 is failing inside D3D12Core with the RenoDX v4.6/v4.7 neural consumer. Repair automatically to restore Adas's compatible v4.55 Feeder consumer, or use driver 616.56.");
        if (log.Contains("dlss5-feed.addon64", StringComparison.OrdinalIgnoreCase)
            && log.Contains("host64", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(log, "stays inert", "must not", "stray"))
            problems.Add("A game-side 64-bit Feeder add-on is inside host64. Repair automatically removes it; host64 needs only the helper, ReShade, one neural consumer, and the NVIDIA runtimes.");
        if (log.Contains("Failed to find NVSDK_NGX_D3D12_EvaluateFeature_C", StringComparison.OrdinalIgnoreCase))
        {
            if (SuccessfulFramesRegex().IsMatch(log))
                notes.Add("The optional EvaluateFeature_C hook is absent, but this log also reports successful processing. Its absence alone does not prove a broken runtime.");
            else
                notes.Add("The EvaluateFeature_C hook is absent. Check whether the normal EvaluateFeature hook succeeds; this warning alone does not establish the cause of a black screen.");
        }
        if (log.Contains("DLSS5_Feed.fx is not loaded", StringComparison.OrdinalIgnoreCase))
            problems.Add("DLSS 5 Feed did not compile or load. Run Repair automatically, then review the first shader error in ReShade.log.");
        if (log.Contains("Failed to load add-on", StringComparison.OrdinalIgnoreCase)
            && !problems.Any(item => item.Contains("64-bit add-on", StringComparison.OrdinalIgnoreCase)))
            problems.Add(log.Contains("error code 1114", StringComparison.OrdinalIgnoreCase)
                ? "An add-on failed during initialization (1114). Restart with one DLSS pipeline only; disabling NR is not the same as unloading its add-on. Check the first initialization error in ReShade.log."
                : "ReShade failed to load at least one add-on. Check the named file and error code in ReShade.log before replacing files.");
        if (ContainsAny(log, "signed runtime sha256", "custom runtime accepted"))
            notes.Add("A community-patched DLSS-NR runtime is active; failures can be specific to that runtime build.");
    }

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> Distinct(IEnumerable<string> values)
        => values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    [GeneratedRegex(@"(?:(?:Successful NR frames|Frames delivered)\s*[:=]\s*[1-9][0-9]*|frame\s+[1-9][0-9]*\s+delivered|inline feature 18 evaluation succeeded\s*\(count=[1-9][0-9]*\))", RegexOptions.IgnoreCase)]
    private static partial Regex SuccessfulFramesRegex();

    private static void VerifyAioFiles(string root, Dlss5DeploymentMode mode, bool is64Bit, Dlss5InstallRecord record, ICollection<string> problems)
    {
        if (!Dlss5ComponentService.SupportsAio(mode, is64Bit))
            problems.Add("AIO requires a supported 64-bit renderer. Use the recommended setup for this game.");
        if (Dlss5ComponentService.IsAioVulkan(mode) && !VulkanLayerService.IsLayerInstalled())
            problems.Add("The 64-bit ReShade Vulkan layer is missing. Install it with the ReShade installer first.");
        var addonRoot = ModInstallService.GetAddonDeployPath(root);
        foreach (var name in Dlss5ComponentService.AioAssetHashes.Keys)
        {
            var path = name == Dlss5ComponentService.AioShader
                ? Path.Combine(root, "reshade-shaders", "Shaders", name) : Path.Combine(addonRoot, name);
            try { Dlss5ComponentService.ValidateAioAsset(path, name); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { problems.Add(ex.Message); }
        }
        var required = new List<string> { Path.Combine(root, "nvngx_dlssnr.dll"), Path.Combine(root, "nvngx_dlss.dll") };
        if (!Dlss5ComponentService.IsAioVulkan(mode)) required.Add(Path.Combine(root, Dlss5ComponentService.AioProxyName(mode)));
        required.AddRange(new[] { "ReShade.fxh", "ReShadeUI.fxh", "DrawText.fxh" }
            .Select(name => Path.Combine(root, "reshade-shaders", "Shaders", name)));
        foreach (var path in required.Concat(record.InstalledHashes.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) { problems.Add($"AIO file missing: {Path.GetRelativePath(root, path)}. Run Repair."); continue; }
            if (!path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                && record.InstalledHashes.TryGetValue(path, out var hash) && !FileHelper.ComputeSha256(path).Equals(hash, StringComparison.OrdinalIgnoreCase))
                problems.Add($"AIO managed file changed: {Path.GetRelativePath(root, path)}. Review it before Repair.");
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !AddonPackService.IsAddonArchitectureCompatible(path, false))
                problems.Add($"Wrong architecture: {Path.GetRelativePath(root, path)}. AIO requires 64-bit files.");
        }
        var vort = Path.Combine(root, "reshade-shaders", "Shaders", "VortShaders");
        if (!Directory.Exists(vort) || !Directory.EnumerateFiles(vort, "*.fx", SearchOption.AllDirectories).Any())
            problems.Add("The AIO VORT motion provider is missing. Run Repair.");
    }
}
