using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using RenoDXCommander.Services;

namespace Adas.App.Shell;

/// <summary>
/// Shared "close the running game first" guard for any install/uninstall that writes DLLs Windows keeps
/// locked while the game is running (ReShade/DLSS add-ons, OptiScaler, DXVK, MFG proxies). Extracted from
/// <see cref="Dlss5Installer"/> so the DLSS 5 route flow, the Extras components, and the Tools window all
/// behave identically instead of each rolling its own (or, previously, skipping the check entirely).
/// </summary>
public static class GameCloseGuard
{
    /// <summary>
    /// If the game at <paramref name="installPath"/> is running, asks to close it, stops it, and waits for
    /// the file locks to release. Returns <c>Proceed</c> when it is safe to continue (nothing was running,
    /// or the game was closed), <c>Cancelled</c> when the user declined, or <c>Failed</c> with a reason when
    /// the processes could not be stopped.
    /// </summary>
    public static async Task<GuardResult> EnsureClosedAsync(Window owner, string gameName, string? installPath)
    {
        var running = await Task.Run(() => GameProcessService.FindRunningProcesses(installPath));
        if (running.Count == 0) return GuardResult.Proceed();

        if (!await DialogHost.ConfirmAsync(owner, $"Close {gameName} and continue?",
                "Windows keeps active ReShade and DLSS add-ons locked while the game is running. Adas will close the game, wait for the files to release, then continue.",
                "Close game and continue", "Cancel"))
            return GuardResult.Cancelled();

        var stopErrors = await GameProcessService.StopProcessesAsync(running);
        if (stopErrors.Count > 0)
            return GuardResult.Failed($"Could not close {gameName}:\n• " + string.Join("\n• ", stopErrors));

        await Task.Delay(250);
        return GuardResult.Proceed();
    }

    public sealed record GuardResult(bool CanProceed, bool WasCancelled, string? Error)
    {
        public static GuardResult Proceed() => new(true, false, null);
        public static GuardResult Cancelled() => new(false, true, null);
        public static GuardResult Failed(string error) => new(false, false, error);
    }
}
