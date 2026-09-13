using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Adas.App.Shell;

/// <summary>
/// Games under protected folders (e.g. <c>C:\Program Files (x86)</c>) grant standard users read-only
/// access. Adas runs as the invoking user, so an install written from an elevated session (for example
/// the installer's "Launch Adas" step) can later fail to Repair/Remove with "Access denied". This guard
/// probes write access up front and offers to restart Adas as administrator before anything is touched.
/// </summary>
public static class ElevationGuard
{
    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    /// <summary>True when the current process can create and delete a file in <paramref name="directory"/>.</summary>
    public static bool CanWrite(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return true; // nothing to probe; the engine reports missing paths itself
            var probe = Path.Combine(directory, $".adas-write-probe-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return true; } // locked/other IO: not a permissions problem
    }

    public static bool IsAccessDenied(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
            if (e is UnauthorizedAccessException) return true;
        return false;
    }

    /// <summary>
    /// Returns null when every folder is writable. Otherwise explains the problem and, if the user agrees,
    /// relaunches Adas elevated and shuts this instance down. The returned message is shown in place of the action.
    /// </summary>
    public static async Task<string?> EnsureWritableAsync(Window owner, string action, params string?[] directories)
    {
        string? blocked = null;
        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var target = directory;
            var adas = Path.Combine(directory, ".adas");
            if (Directory.Exists(adas)) target = adas;
            if (!await Task.Run(() => CanWrite(target))) { blocked = directory; break; }
        }
        if (blocked is null) return null;
        return await OfferElevationAsync(owner, action, blocked);
    }

    public static async Task<string> OfferElevationAsync(Window owner, string action, string folder)
    {
        if (IsElevated)
            return $"{action} failed: Windows denied write access to '{folder}' even as administrator. Check the folder's permissions or security software.";

        var restart = await DialogHost.ConfirmAsync(owner, "Administrator permission needed",
            $"This game is in a protected folder that Adas can't change without administrator rights:\n\n{folder}\n\n" +
            "Restart Adas as administrator, then run " + action.ToLowerInvariant() + " again.",
            "Restart as administrator", "Cancel");
        if (!restart) return $"{action} cancelled: administrator permission is required for '{folder}'.";

        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        try
        {
            Process.Start(new ProcessStartInfo(exe!) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Exception ex)
        {
            // ERROR_CANCELLED (1223) when the UAC prompt is declined.
            return $"{action} cancelled: Adas was not restarted as administrator ({ex.Message}).";
        }
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        return "Restarting Adas as administrator…";
    }
}
