using System;
using System.IO;
using System.Text.Json;

namespace Adas.App.Shell;

/// <summary>
/// Persists small, purely-cosmetic shell layout state (the library/detail split ratio, the setup-pane
/// Simple/Advanced mode, and the first-run flag) to a per-user JSON file. This is a UI concern of the
/// Avalonia shell, so it lives here rather than in the engine's settings plumbing. All operations are
/// best-effort — a missing or corrupt file just yields defaults.
/// </summary>
internal static class UiLayoutStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "ui-layout.json");

    private sealed class Layout
    {
        public double SplitRatio { get; set; }
        public string? SetupMode { get; set; }
        public bool FirstRunDone { get; set; }
    }

    private static Layout Load()
    {
        try
        {
            if (File.Exists(FilePath) &&
                JsonSerializer.Deserialize<Layout>(File.ReadAllText(FilePath)) is { } layout)
                return layout;
        }
        catch { /* corrupt/absent — fall through to defaults */ }
        return new Layout();
    }

    private static void Save(Layout layout)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(layout));
        }
        catch { /* layout persistence is best-effort */ }
    }

    /// <summary>
    /// The saved fraction of the body width given to the left (library) pane, clamped to a sane
    /// range, or null when nothing valid is stored.
    /// </summary>
    public static double? LoadSplitRatio()
    {
        var r = Load().SplitRatio;
        if (r is > 0.15 and < 0.85) return r;
        return null;
    }

    public static void SaveSplitRatio(double ratio)
    {
        var layout = Load();
        layout.SplitRatio = ratio;
        Save(layout);
    }

    /// <summary>The persisted setup-pane mode: "Advanced" when the user chose it, else "Simple".</summary>
    public static bool LoadAdvancedSetupMode()
        => string.Equals(Load().SetupMode, "Advanced", StringComparison.OrdinalIgnoreCase);

    public static void SaveAdvancedSetupMode(bool advanced)
    {
        var layout = Load();
        layout.SetupMode = advanced ? "Advanced" : "Simple";
        Save(layout);
    }

    /// <summary>True once the first-run onboarding overlay has been dismissed.</summary>
    public static bool LoadFirstRunDone() => Load().FirstRunDone;

    public static void SaveFirstRunDone(bool done)
    {
        var layout = Load();
        layout.FirstRunDone = done;
        Save(layout);
    }
}
