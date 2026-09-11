using RenoDXCommander.Models;

namespace RenoDXCommander;

/// <summary>
/// Framework-neutral helpers that were historically parked as static methods on the WinUI
/// <c>MainWindow</c>. They are pure logic (process-launch info, status-text formatting) with no
/// UI dependency, so they live in the engine where both the shell and the tests can use them.
/// </summary>
internal static class ShellHelpers
{
    /// <summary>Builds a shell-execute launch descriptor for a game executable.</summary>
    internal static System.Diagnostics.ProcessStartInfo CreateDirectLaunchInfo(string executable, string? arguments)
        => new(executable)
        {
            Arguments = arguments ?? "",
            WorkingDirectory = Path.GetDirectoryName(executable) ?? "",
            UseShellExecute = true,
        };

    /// <summary>Maps a DLSS 5 diagnostic report to the simple-view title/summary shown to the user.</summary>
    internal static (string Title, string Summary) DescribeSimpleInstalledStatus(Dlss5DiagnosticReport? report)
    {
        if (report == null)
            return ("DLSS 5 files are installed", "Adas found an installation record, but has not checked the live neural-rendering session yet.");

        var findings = report.Findings.Count == 0 ? "" : " " + string.Join(" ", report.Findings);
        if (report.HasProblems)
            return ("Repair recommended", report.Summary + " Adas can replace wrong or missing managed files automatically." + findings);
        if (report.IsWorking)
            return ("DLSS 5 is working", report.Summary + findings);

        return (
            "DLSS 5 files are installed",
            "The required files match, but live neural rendering is not yet confirmed." + findings);
    }
}
