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

    /// <summary>
    /// Probes with the user's Graphics API choice applied — every probe in the shell must honour it, otherwise
    /// the UI shows one API while install assesses another.
    /// </summary>
    internal static Dlss5Probe ProbeFor(MainViewModel main, Dlss5CompatibilityService compat, GameCardViewModel card)
        => compat.Probe(card, main.GetSingleApiOverride(card.GameName, card.Source ?? ""));

    /// <summary>
    /// Probes against a captured operation snapshot — used throughout a running operation so a
    /// background card refresh (paths, store key, API choice) cannot change what we assess or install.
    /// </summary>
    internal static Dlss5Probe ProbeFor(Dlss5CompatibilityService compat, Dlss5GameOperation op)
        => compat.Probe(op, op.ApiOverride);

    /// <param name="forceProfile">The user picked a route Adas doesn't recommend for this game — install it as chosen.</param>
    /// <param name="deploymentPath">A folder the user picked when detection found none or several.</param>
    /// <param name="risksConfirmed">The caller already showed the warnings and the user chose to continue.</param>
    public static async Task<Outcome> InstallAsync(
        MainViewModel main,
        Window owner,
        GameCardViewModel card,
        Dlss5InstallProfile profile,
        IProgress<(string message, double percent)> progress,
        bool deepFriedChicken = false,
        bool forceProfile = false,
        bool bridgeSubstitute = false,
        string? deploymentPath = null,
        bool risksConfirmed = false)
    {
        var services = AppServices.Services;
        var compat = services.GetService<Dlss5CompatibilityService>();
        var components = services.GetService<Dlss5ComponentService>();
        if (compat is null || components is null)
            return new Outcome(false, "Engine services are unavailable.");

        // Freeze the game's identity, paths and Graphics API choice for the whole operation. Background
        // discovery may refresh the card mid-flow; every guard, message and engine call below reads this
        // snapshot instead of the live card, so the operation can't silently retarget another game.
        var op = Dlss5GameOperation.Capture(card, main.GetSingleApiOverride(card.GameName, card.Source ?? ""));

        Task<(Dlss5Probe probe, Dlss5Assessment assessment, IReadOnlyList<string> accepted)> AssessAsync()
            => Task.Run(() =>
            {
                var probed = ProbeFor(compat, op);
                var assessed = Dlss5CompatibilityService.Assess(probed, singlePlayerConfirmed: true);
                if (!string.IsNullOrWhiteSpace(deploymentPath))
                    assessed = Dlss5CompatibilityService.ConfirmDeploymentPath(assessed, deploymentPath);
                var (cleared, risks) = Dlss5CompatibilityService.AcceptRisks(assessed);
                if (bridgeSubstitute) cleared = Dlss5ComponentService.ApplyBridgeSubstitute(cleared);
                return (probed, cleared, risks);
            });

        var (probe, assessment, accepted) = await AssessAsync();

        // Only two questions can't be skipped, and the setup page asks both before calling us.
        if (assessment.Mode == Dlss5DeploymentMode.None)
            return new Outcome(false, "Choose the game's Graphics API first (Graphics API box at the top).");
        if (string.IsNullOrWhiteSpace(assessment.DeploymentPath) || assessment.BlockingReasons.Count > 0)
            return new Outcome(false, Dlss5ReadinessText.Describe(assessment, op.InstallPath).ToMessage());

        if (!risksConfirmed)
        {
            var warnings = accepted.Select(Dlss5ReadinessText.FriendlyBlocker).OfType<string>()
                .Where(w => !w.Contains("Adas installs it", StringComparison.Ordinal)
                            && !w.Contains("Adas sets it up", StringComparison.Ordinal))
                .Distinct().ToArray();
            if (warnings.Length > 0
                && !await DialogHost.ConfirmAsync(owner, "Install anyway?",
                    string.Join("\n\n", warnings.Select(w => "⚠ " + w)), "Install anyway", "Cancel"))
                return new Outcome(false, "Cancelled.");
        }

        // ── Prerequisites Adas can fix itself ─────────────────────────────────
        if (probe.MissingRuntimeArchitectures.Count > 0)
        {
            foreach (var architecture in probe.MissingRuntimeArchitectures)
            {
                if (await Dlss5RuntimePrerequisites.DownloadAndInstallAsync(architecture, progress) is { } runtimeError)
                    return new Outcome(false, runtimeError);
            }
            (probe, assessment, accepted) = await AssessAsync();
        }

        if (assessment.Mode is Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.NativeVulkan
            && !probe.HasReShadeAddonSupport)
        {
            progress.Report(("Setting up the Vulkan ReShade layer…", 4));
            await main.InstallReShadeAsync(card);
            (probe, assessment, accepted) = await AssessAsync();
            if (!probe.HasReShadeAddonSupport)
                return new Outcome(false, "The Vulkan ReShade layer isn't set up yet"
                    + (string.IsNullOrWhiteSpace(card.RsActionMessage) ? "." : $": {card.RsActionMessage}")
                    + " Adas needs it for Vulkan games — try Install again (Windows asks for permission).");
        }

        var root = assessment.DeploymentPath!;

        if (await ElevationGuard.EnsureWritableAsync(owner, "Installation", root, op.InstallPath) is { } denied)
            return new Outcome(false, denied);

        // ── Known-bad-driver pre-flight (route-aware) ────────────────────────
        var driverWarning = await Task.Run(() =>
            Dlss5CompatibilityService.GetDriverPreflightWarning(assessment.Mode, profile));
        if (!string.IsNullOrWhiteSpace(driverWarning)
            && !await DialogHost.ConfirmAsync(owner, "Driver may be incompatible with this route",
                   driverWarning, "Install anyway", "Cancel"))
            return new Outcome(false, "Cancelled.");

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
        if (await EnsureGameClosedAsync(owner, op) is { } blocked)
            return blocked;

        // ── Install ──────────────────────────────────────────────────────────
        // Per-game Feeder build channel and motion-vector provider (Advanced options).
        var (feederTag, motionProvider) = Dlss5GamePreferences.ResolveInstallChoices(
            Dlss5GamePreferences.Get(op.GameName, op.Source), assessment.Mode);
        var overrides = new Dlss5ManualOverrides(DeepFriedChicken: deepFriedChicken, ForceProfile: forceProfile,
            FeederReleaseTag: feederTag, MotionProvider: motionProvider, BridgeSubstitute: bridgeSubstitute);
        var channel = main.ResolveReShadeChannel(op.GameName, op.Source);
        var removedPrevious = false;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                progress.Report(("Preparing…", 2));
                var relocationErrors = await Task.Run(() =>
                    components.RemoveOtherManagedDeployments(op.InstallPath, root));
                if (relocationErrors.Count > 0)
                    return new Outcome(false, "Adas could not remove the previous launcher-folder deployment:\n• "
                        + string.Join("\n• ", relocationErrors));

                var current = assessment;
                var currentCleanup = cleanup;
                var result = await Task.Run(() => components.InstallAsync(
                    op.GameName,
                    current,
                    progress,
                    reShadeChannel: channel,
                    store: op.Source,
                    profile: profile,
                    cleanupApproval: currentCleanup,
                    overrides: overrides));

                var text = result.Message;
                if (removedPrevious)
                    text = "Removed the previous DLSS pipeline, then installed.\n" + text;
                if (result.Warnings.Count > 0)
                    text += "\n\nSetup notes:\n• " + string.Join("\n• ", result.Warnings.Distinct(StringComparer.OrdinalIgnoreCase));
                return new Outcome(true, text);
            }
            catch (Dlss5RecoveredInterruptedSwitchException) when (attempt == 0)
            {
                // The engine rolled back a half-finished switch; the game is consistent again, so just retry.
                (probe, assessment, accepted) = await AssessAsync();
            }
            catch (Dlss5ConflictingPipelineException ex) when (attempt == 0)
            {
                // Two pipelines can't be stacked. Instead of making the user find a × button, offer to do it.
                if (!await DialogHost.ConfirmAsync(owner, "Replace the current DLSS setup?",
                        "This game already has a different DLSS pipeline installed. Adas will remove it (restoring the original files) and then install your selection.\n\n"
                        + ex.Message, "Replace it", "Cancel"))
                    return new Outcome(false, "Cancelled.");
                progress.Report(("Removing the previous DLSS pipeline…", 3));
                var errors = await Task.Run(() => components.Uninstall(root));
                if (errors.Count > 0)
                    return new Outcome(false, "Could not remove the previous DLSS pipeline:\n• "
                        + string.Join("\n• ", errors.Distinct(StringComparer.OrdinalIgnoreCase)));
                removedPrevious = true;
                (probe, assessment, accepted) = await AssessAsync();
                try { cleanup = await Task.Run(() => Dlss5ComponentService.GetCleanupPlan(root, assessment.Mode, profile)); }
                catch (Exception cleanupEx) { return new Outcome(false, $"Could not review conflicting components: {cleanupEx.Message}"); }
            }
            catch (Exception ex)
            {
                if (ElevationGuard.IsAccessDenied(ex) && !ElevationGuard.IsElevated)
                    return new Outcome(false, await ElevationGuard.OfferElevationAsync(owner, "Installation", root));
                return new Outcome(false, $"Installation failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Removes the DLSS 5 install for this game: closes a running game if needed, then runs the engine's
    /// <see cref="Dlss5ComponentService.Uninstall"/>, which restores tracked originals and clears the record.
    /// </summary>
    public static async Task<Outcome> RemoveAsync(MainViewModel main, Window owner, GameCardViewModel card,
        IProgress<(string message, double percent)> progress)
    {
        var services = AppServices.Services;
        var compat = services.GetService<Dlss5CompatibilityService>();
        var components = services.GetService<Dlss5ComponentService>();
        if (compat is null || components is null)
            return new Outcome(false, "Engine services are unavailable.");

        var op = Dlss5GameOperation.Capture(card, main.GetSingleApiOverride(card.GameName, card.Source ?? ""));

        // Resolve the folder that actually holds the install record (install root or addon deploy path).
        var root = await Task.Run(() => ResolveInstalledRoot(compat, op));
        if (root is null)
            return new Outcome(false, "No DLSS 5 install was found for this game.");

        if (await ElevationGuard.EnsureWritableAsync(owner, "Removal", root) is { } denied)
            return new Outcome(false, denied);

        if (!await DialogHost.ConfirmAsync(owner, $"Remove DLSS 5 from {op.GameName}?",
                "Adas will restore the game's original files from its recovery copies and remove the DLSS 5 components it installed. Your saved routes and settings are unaffected.",
                "Remove and restore", "Cancel"))
            return new Outcome(false, "Cancelled.");

        if (await EnsureGameClosedAsync(owner, op) is { } blocked)
            return blocked;

        try
        {
            progress.Report(("Removing…", 10));
            var errors = await Task.Run(() => components.Uninstall(root));
            progress.Report(("Done.", 100));
            if (errors.Count > 0)
                return new Outcome(true, "Removed with some issues:\n• "
                    + string.Join("\n• ", errors.Distinct(StringComparer.OrdinalIgnoreCase)));
            return new Outcome(true, $"DLSS 5 removed from {op.GameName}; original files restored.");
        }
        catch (Exception ex)
        {
            if (ElevationGuard.IsAccessDenied(ex) && !ElevationGuard.IsElevated)
                return new Outcome(false, await ElevationGuard.OfferElevationAsync(owner, "Removal", root));
            return new Outcome(false, $"Removal failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Repairs a DLSS 5 install: re-normalises the ReShade search paths and prunes stale/disabled DLSS
    /// add-on entries a crash can leave behind, via <see cref="Dlss5ComponentService.RepairReShadeConfiguration"/>.
    /// </summary>
    public static async Task<Outcome> RepairAsync(MainViewModel main, Window owner, GameCardViewModel card,
        IProgress<(string message, double percent)> progress)
    {
        var services = AppServices.Services;
        var compat = services.GetService<Dlss5CompatibilityService>();
        if (compat is null)
            return new Outcome(false, "Engine services are unavailable.");

        var op = Dlss5GameOperation.Capture(card, main.GetSingleApiOverride(card.GameName, card.Source ?? ""));

        var root = await Task.Run(() => ResolveInstalledRoot(compat, op));
        if (root is null)
            return new Outcome(false, "No DLSS 5 install was found for this game.");

        var record = await Task.Run(() => Dlss5ComponentService.LoadRecord(root));
        if (record is null)
            return new Outcome(false, "No DLSS 5 install record was found to repair.");

        if (await ElevationGuard.EnsureWritableAsync(owner, "Repair", root) is { } denied)
            return new Outcome(false, denied);

        if (await EnsureGameClosedAsync(owner, op) is { } blocked)
            return blocked;

        try
        {
            progress.Report(("Repairing…", 20));
            await Task.Run(() => Dlss5ComponentService.RepairReShadeConfiguration(root, record));
            progress.Report(("Done.", 100));
            return new Outcome(true, $"Repaired the ReShade/DLSS 5 configuration for {op.GameName}.");
        }
        catch (Exception ex)
        {
            if (ElevationGuard.IsAccessDenied(ex) && !ElevationGuard.IsElevated)
                return new Outcome(false, await ElevationGuard.OfferElevationAsync(owner, "Repair", root));
            return new Outcome(false, $"Repair failed: {ex.Message}");
        }
    }

    /// <summary>Resolves the folder that holds this game's DLSS 5 install record, or null if none.</summary>
    private static string? ResolveInstalledRoot(Dlss5CompatibilityService compat, Dlss5GameOperation op)
    {
        var assessment = Dlss5CompatibilityService.Assess(ProbeFor(compat, op), singlePlayerConfirmed: true);
        foreach (var candidate in new[]
                 {
                     assessment.DeploymentPath,
                     op.InstallPath,
                     string.IsNullOrWhiteSpace(op.InstallPath) ? null : ModInstallService.GetAddonDeployPath(op.InstallPath),
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Dlss5ComponentService.LoadRecord(candidate) is not null)
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Ensures the game is not running before touching its locked add-on files. Returns a cancel/failure
    /// <see cref="Outcome"/> to bubble up, or null when it is safe to proceed. Shared by install/remove/repair.
    /// </summary>
    private static async Task<Outcome?> EnsureGameClosedAsync(Window owner, Dlss5GameOperation op)
    {
        var result = await GameCloseGuard.EnsureClosedAsync(owner, op.GameName, op.InstallPath);
        if (result.CanProceed) return null;
        return new Outcome(false, result.WasCancelled ? "Cancelled." : result.Error!);
    }
}
