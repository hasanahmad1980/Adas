using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>One AIO ReShade.ini switch that fixes a known failure, offered from Diagnose.</summary>
public sealed record AioFix(string Key, string Value, string Title, string Explanation);

/// <summary>
/// Matches the AIO diagnostic log against known failure signatures and suggests the one-click setting
/// switch the author documents for each. Suggestions never apply themselves; the user clicks them.
/// </summary>
public static class Dlss5AioTroubleshooter
{
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "Logs", "standalone-dlssnr.log");

    public static readonly AioFix DpiCorrection = new("DpiPhysicalOutputCorrection", "1",
        "Fix output size on scaled displays",
        "The log shows the game's source is larger than the detected native resolution, which happens with Windows display scaling.");

    public static readonly AioFix SynchronousPresentation = new("SynchronousProxyPresentation", "1",
        "Use synchronous presentation",
        "The log shows a failed or unclean presentation start. Synchronous presentation is slower but more reliable.");

    public static readonly AioFix EarlyInitialization = new("EarlyProxyInitialization", "1",
        "Start the output window early",
        "The log shows AIO waiting for the game's first frame. D3D12 games that present late need early initialization.");

    public static readonly AioFix WindowedVirtualization = new("WindowedVirtualization", "1",
        "Virtualize exclusive fullscreen",
        "Exclusive fullscreen can hide AIO's output window. Virtualizing it keeps the output on top.");

    /// <summary>All switches, for a manual list when the log has no match.</summary>
    public static IReadOnlyList<AioFix> All(Dlss5DeploymentMode mode)
        => mode is Dlss5DeploymentMode.NativeDirectX12 or Dlss5DeploymentMode.Dx12Feeder
            ? new[] { SynchronousPresentation, DpiCorrection, WindowedVirtualization, EarlyInitialization }
            : new[] { SynchronousPresentation, DpiCorrection, WindowedVirtualization };

    /// <summary>Suggested fixes for the given log text; <paramref name="repairNeeded"/> is set when files are missing.</summary>
    public static IReadOnlyList<AioFix> Suggest(string? logText, Dlss5DeploymentMode mode, out bool repairNeeded)
    {
        repairNeeded = false;
        var fixes = new List<AioFix>();
        if (string.IsNullOrWhiteSpace(logText)) return fixes;
        bool Has(string text) => logText.Contains(text, StringComparison.OrdinalIgnoreCase);

        if (Has("Source exceeds detected native resolution")) fixes.Add(DpiCorrection);
        if (Has("previous game session did not shut down cleanly") || Has("proxy DXGI Present failed")
            || Has("native presentation initialization failed"))
            fixes.Add(SynchronousPresentation);
        if (Has("waiting for first game present")
            && mode is Dlss5DeploymentMode.NativeDirectX12 or Dlss5DeploymentMode.Dx12Feeder)
            fixes.Add(EarlyInitialization);
        if (Has("required private runtime dependency missing")) repairNeeded = true;
        return fixes;
    }

    /// <summary>Reads the tail of the shared AIO log (last 256 KB), or null when it doesn't exist.</summary>
    public static string? ReadLogTail(string? path = null)
    {
        try
        {
            path ??= LogPath;
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            const long tail = 256 * 1024;
            if (stream.Length > tail) stream.Seek(-tail, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch { return null; }
    }

    /// <summary>Keys already set to the fix value in the game's AIO settings.</summary>
    public static bool IsApplied(string root, AioFix fix)
    {
        try
        {
            var ini = IniTextDocument.Load(Dlss5ComponentService.AioSettingsIniPath(root));
            return ini.TryGetValue(Dlss5ComponentService.AioSection, fix.Key, out var value) && value.Text.Trim() == fix.Value;
        }
        catch { return false; }
    }
}
