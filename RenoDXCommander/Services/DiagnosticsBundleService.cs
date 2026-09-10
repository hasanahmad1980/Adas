using System.Diagnostics;
using System.Text;

namespace RenoDXCommander.Services;

/// <summary>
/// Builds a single, human-readable diagnostics report — the app's own session log,
/// the most recent crash report, the detected GPU and driver, and (when a game is
/// selected) that game's install manifest plus its ReShade and DLSS5-Feeder logs.
/// The report is assembled in memory and returned as text so the caller can show it
/// to the user before it is written anywhere (never uploaded). Modelled on the
/// "save diagnostics" workflow in rakanki911/DLSS5-Swapper.
/// </summary>
public sealed class DiagnosticsBundleService
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "logs");

    // ReShade and Feeder logs the games themselves write; only the tail is useful.
    private static readonly string[] GameLogNames =
    {
        "ReShade.log", "reshade.log", "dlss5-feeder.log", "DLSS5-Feeder.log",
        "dlss5_feeder.log", "renodx.log",
    };

    private const int MaxTailLines = 400;

    public async Task<string> BuildReportAsync(string? gameFolder, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════════════");
        sb.AppendLine($" Adas diagnostics — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($" App version : {CrashReporter.AppVersion}");
        sb.AppendLine($" OS          : {Environment.OSVersion}");
        sb.AppendLine($" GPU         : {Nz(Dlss5CompatibilityService.DetectedGpuName)}");
        sb.AppendLine($" NV driver   : {Nz(await DetectDriverVersionAsync(cancellationToken).ConfigureAwait(false))}");
        sb.AppendLine("═══════════════════════════════════════════════════════════");
        sb.AppendLine();

        AppendGameSection(sb, gameFolder);

        AppendFileTail(sb, "── Current session log", CrashReporter.CurrentSessionLogPath);
        AppendFileTail(sb, "── Most recent crash report", LatestFile("crash_*.txt"));

        return sb.ToString();
    }

    private static void AppendGameSection(StringBuilder sb, string? gameFolder)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
        {
            sb.AppendLine("── Selected game: (none — app-level diagnostics only)");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"── Selected game folder: {gameFolder}");
        sb.AppendLine();

        var recordPath = Path.Combine(gameFolder, ".adas", "dlss5-install.json");
        AppendFileTail(sb, "── Install manifest (.adas/dlss5-install.json)", File.Exists(recordPath) ? recordPath : null);

        var iniPath = Path.Combine(gameFolder, "reshade.ini");
        AppendFileTail(sb, "── reshade.ini", File.Exists(iniPath) ? iniPath : null);

        foreach (var name in GameLogNames)
        {
            var candidate = Path.Combine(gameFolder, name);
            if (File.Exists(candidate))
                AppendFileTail(sb, $"── Game log: {name}", candidate);
        }
    }

    private static void AppendFileTail(StringBuilder sb, string header, string? path)
    {
        sb.AppendLine(header);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            sb.AppendLine("  (not present)");
            sb.AppendLine();
            return;
        }

        try
        {
            var lines = File.ReadLines(path).ToArray();
            var tail = lines.Length > MaxTailLines ? lines[^MaxTailLines..] : lines;
            if (lines.Length > MaxTailLines)
                sb.AppendLine($"  … ({lines.Length - MaxTailLines} earlier lines omitted)");
            foreach (var line in tail)
                sb.AppendLine("  " + line);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  (could not read: {ex.Message})");
        }
        sb.AppendLine();
    }

    private static string? LatestFile(string pattern)
    {
        try
        {
            if (!Directory.Exists(LogDir)) return null;
            return Directory.GetFiles(LogDir, pattern)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch { return null; }
    }

    private static async Task<string> DetectDriverVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "nvidia-smi.exe",
                Arguments = "--query-gpu=driver_version --format=csv,noheader",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process == null) return "";
            var output = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            process.WaitForExit(2500);
            return output?.Trim() ?? "";
        }
        catch { return ""; }
    }

    private static string Nz(string? value) => string.IsNullOrWhiteSpace(value) ? "(unknown)" : value;
}
