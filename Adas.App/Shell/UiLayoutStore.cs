using System;
using System.IO;
using System.Text.Json;

namespace Adas.App.Shell;

/// <summary>
/// Persists small, purely-cosmetic shell layout state (currently the library/detail split ratio) to
/// a per-user JSON file. This is a UI concern of the Avalonia shell, so it lives here rather than in
/// the engine's settings plumbing. All operations are best-effort — a missing or corrupt file just
/// yields defaults.
/// </summary>
internal static class UiLayoutStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "ui-layout.json");

    private sealed class Layout
    {
        public double SplitRatio { get; set; }
    }

    /// <summary>
    /// The saved fraction of the body width given to the left (library) pane, clamped to a sane
    /// range, or null when nothing valid is stored.
    /// </summary>
    public static double? LoadSplitRatio()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var layout = JsonSerializer.Deserialize<Layout>(File.ReadAllText(FilePath));
            if (layout is null) return null;
            var r = layout.SplitRatio;
            if (r is > 0.15 and < 0.85) return r;
            return null;
        }
        catch { return null; }
    }

    public static void SaveSplitRatio(double ratio)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(new Layout { SplitRatio = ratio });
            File.WriteAllText(FilePath, json);
        }
        catch { /* layout persistence is best-effort */ }
    }
}
