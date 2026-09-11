using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Loads and saves the 4 global OptiScaler preset slots from/to local app data.
/// Presets are global (not per-game) — saved once, applicable to any game.
/// </summary>
public static class OsPresetService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "os_presets.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Loads the preset array from disk. Always returns exactly 4 entries (nulls for empty slots).
    /// If the file does not exist, returns a default array with slot 0 pre-populated as "DLSS Enabler".
    /// </summary>
    public static OsPreset?[] Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<OsPreset?[]>(json);
                if (loaded != null)
                {
                    // Ensure exactly 4 slots
                    var result = new OsPreset?[4];
                    for (int i = 0; i < 4 && i < loaded.Length; i++)
                        result[i] = loaded[i];
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OsPresetService.Load] {ex.Message}");
        }

        // First-run default: slot 0 = DLSS Enabler
        return new OsPreset?[]
        {
            new OsPreset
            {
                Name               = "DLSS Enabler",
                FgInput            = "upscaler",
                FgOutput           = "dlssg",
                FgNvngxReplacement = "Arturs",
                DeployStreamline   = true,
                DeployDlssEnabler  = true,
            },
            null,
            null,
            null,
        };
    }

    /// <summary>
    /// Saves all 4 preset slots to disk. Creates the directory if needed.
    /// </summary>
    public static void Save(OsPreset?[] presets)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // Always write exactly 4 entries
            var toWrite = new OsPreset?[4];
            for (int i = 0; i < 4; i++)
                toWrite[i] = i < presets.Length ? presets[i] : null;

            var json = JsonSerializer.Serialize(toWrite, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OsPresetService.Save] {ex.Message}");
        }
    }
}
