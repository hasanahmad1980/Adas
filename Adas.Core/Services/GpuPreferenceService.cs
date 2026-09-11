using Microsoft.Win32;

namespace RenoDXCommander.Services;

/// <summary>
/// Writes the per-application graphics-preference key Windows honours when a machine has
/// both an integrated and a discrete GPU (Optimus / hybrid laptops). A DLSS 5 route deployed
/// beside a game that Windows decided to run on the integrated GPU renders no neural frames;
/// borrowed from DLSS5oneclick, which forces the game exe to "High performance" (the discrete
/// RTX GPU) via <c>HKCU\Software\Microsoft\DirectX\UserGpuPreferences</c>.
///
/// The value is a single REG_SZ per exe path, data <c>GpuPreference=2;</c> (2 = high performance).
/// Every operation is idempotent and removal only deletes the exact value Adas wrote, so a
/// preference the user set by hand is never clobbered on uninstall.
/// </summary>
internal static class GpuPreferenceService
{
    private const string SubKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>The value Adas writes for a managed exe: force the discrete/high-performance GPU.</summary>
    internal const string HighPerformanceValue = "GpuPreference=2;";

    /// <summary>
    /// Ensures <paramref name="exePath"/> is pinned to the high-performance GPU. Idempotent: a no-op
    /// when the value is already ours. Returns <c>true</c> when the registry now holds our value
    /// (whether it was already present or freshly written), <c>false</c> when the write could not be made.
    /// </summary>
    internal static bool Set(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;
        var name = Path.GetFullPath(exePath);
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SubKey, writable: true);
            if (key == null) return false;
            if (key.GetValue(name) as string == HighPerformanceValue) return true;
            key.SetValue(name, HighPerformanceValue, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes the preference for <paramref name="exePath"/> only when it still holds the exact value
    /// Adas wrote; a value the user has since changed is left untouched.
    /// </summary>
    internal static void Clear(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return;
        var name = Path.GetFullPath(exePath);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: true);
            if (key == null) return;
            if (key.GetValue(name) as string == HighPerformanceValue)
                key.DeleteValue(name, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // A preference we could not clear is not worth failing an uninstall over.
        }
    }
}
