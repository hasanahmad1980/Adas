using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Abstractions;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace Adas.App.Shell;

/// <summary>
/// Drives a DLSS 5 route install from the Avalonia UI: probe/assess, confirm conflict cleanup, close a
/// running game if needed, then run the engine's <see cref="Dlss5ComponentService.InstallAsync"/>. This
/// is the Avalonia rebuild of the WinUI Manage-button flow (progress reported to the caller's bar).
/// </summary>
public static class Dlss5Installer
{
    public sealed record Outcome(bool Ran, string Message);

    public static async Task<Outcome> InstallAsync(
        MainViewModel main,
        Window owner,
        GameCardViewModel card,
        Dlss5InstallProfile profile,
        IProgress<(string message, double percent)> progress)
    {
        var services = AppServices.Services;
        var compat = services.GetService<Dlss5CompatibilityService>();
        var components = services.GetService<Dlss5ComponentService>();
        if (compat is null || components is null)
            return new Outcome(false, "Engine services are unavailable.");

        var assessment = await Task.Run(() =>
            Dlss5CompatibilityService.Assess(compat.Probe(card), singlePlayerConfirmed: true));

        if (!assessment.CanInstall)
        {
            var reasons = assessment.BlockingReasons.Concat(assessment.MissingRequirements)
                .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
            return new Outcome(false, reasons.Length > 0
                ? "This route is blocked:\n• " + string.Join("\n• ", reasons)
                : "This route is not available for this game.");
        }

        var root = assessment.DeploymentPath ?? card.InstallPath;
        if (string.IsNullOrWhiteSpace(root))
            return new Outcome(false, "No install path resolved for this game.");

        // ── Conflict cleanup approval ────────────────────────────────────────
        Dlss5CleanupPlan cleanup;
        try { cleanup = await Task.Run(() => Dlss5ComponentService.GetCleanupPlan(root, assessment.Mode, profile)); }
        catch (Exception ex) { return new Outcome(false, $"Could not review conflicting components: {ex.Message}"); }

        if (cleanup.RequiresConfirmation)
        {
            var explanation = "Adas will remove the previous components, keep recovery copies, and continue installing your selected setup.";
            if (cleanup.Files.Count > 0)
                explanation += "\n\nConflicting files moved into .adas\\preserved:\n• "
                    + string.Join("\n• ", cleanup.Files.Select(f => System.IO.Path.GetRelativePath(cleanup.Root, f.Path)));
            if (cleanup.SharedLayerReset)
                explanation += "\n\nVulkan uses a shared layer. Adas finishes removal before setting up the new route; if that setup fails, use Repair to continue.";

            if (!await DialogHost.ConfirmAsync(owner, "Remove conflicts and continue?", explanation,
                    "Remove conflicts and continue", "Cancel"))
                return new Outcome(false, "Cancelled.");
        }

        // ── Close a running game so its add-on files unlock ──────────────────
        var running = await Task.Run(() => GameProcessService.FindRunningProcesses(card.InstallPath));
        if (running.Count > 0)
        {
            if (!await DialogHost.ConfirmAsync(owner, $"Close {card.GameName} and continue?",
                    "Windows keeps active ReShade and DLSS add-ons locked while the game is running. Adas will close the game, wait for the files to release, then continue.",
                    "Close game and continue", "Cancel"))
                return new Outcome(false, "Cancelled.");

            var stopErrors = await GameProcessService.StopProcessesAsync(running);
            if (stopErrors.Count > 0)
                return new Outcome(false, $"Could not close {card.GameName}:\n• " + string.Join("\n• ", stopErrors));
            await Task.Delay(250);
        }

        // ── Install ──────────────────────────────────────────────────────────
        try
        {
            progress.Report(("Preparing…", 2));
            var relocationErrors = await Task.Run(() =>
                components.RemoveOtherManagedDeployments(card.InstallPath, root));
            if (relocationErrors.Count > 0)
                return new Outcome(false, "Adas could not remove the previous launcher-folder deployment:\n• "
                    + string.Join("\n• ", relocationErrors));

            var channel = main.ResolveReShadeChannel(card.GameName, card.Source ?? "");
            var result = await Task.Run(() => components.InstallAsync(
                card.GameName,
                assessment,
                progress,
                reShadeChannel: channel,
                store: card.Source,
                profile: profile,
                cleanupApproval: cleanup,
                overrides: null));

            var text = result.Message;
            if (result.Warnings.Count > 0)
                text += "\n\nSetup notes:\n• " + string.Join("\n• ", result.Warnings.Distinct(StringComparer.OrdinalIgnoreCase));
            return new Outcome(true, text);
        }
        catch (Exception ex)
        {
            return new Outcome(false, $"Installation failed: {ex.Message}");
        }
    }
}
