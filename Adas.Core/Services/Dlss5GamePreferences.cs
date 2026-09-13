using System.Text.Json;
using System.Text.RegularExpressions;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>Which DLSS5-Feeder build a game installs.</summary>
public enum Dlss5FeederChannel
{
    /// <summary>The build packaged with Adas for the chosen route (default).</summary>
    Bundled,
    /// <summary>The newest pre-release published on jlrouzies-fr/DLSS5-Feeder.</summary>
    NewestPrerelease,
    /// <summary>One exact release tag.</summary>
    ExactRelease,
}

/// <summary>Where the Feeder reads its motion vectors from (<c>DLSS5_MV_PROVIDER</c>).</summary>
public enum Dlss5MotionProvider
{
    /// <summary>LumeniteFX Kernel — <c>DLSS5_MV_PROVIDER=3</c>, the provider the Feeder is tuned on.</summary>
    LumeniteKernel,
    /// <summary>VORT Motion optical flow — <c>DLSS5_MV_PROVIDER=2</c>; the working choice on OpenGL.</summary>
    VortMotion,
}

/// <summary>Adas-side choices for one game that are not stored in the game folder.</summary>
public sealed class Dlss5GamePreference
{
    public Dlss5FeederChannel FeederChannel { get; set; } = Dlss5FeederChannel.Bundled;
    public string? FeederReleaseTag { get; set; }
    public Dlss5MotionProvider? MotionProvider { get; set; }
    public bool OptiScalerFsrFrameGeneration { get; set; }
    /// <summary>Last tuning preset applied: Quality, Balanced, Performance or Custom.</summary>
    public string? TuningPreset { get; set; }
    /// <summary>The user's own saved tuning (setting name → value), restored with "My profile".</summary>
    public Dictionary<string, string>? CustomTuning { get; set; }
}

/// <summary>
/// Small JSON store for per-game DLSS 5 choices (Feeder channel, motion-vector provider, OptiScaler
/// FSR frame generation, tuning profiles). Keyed by game name + store so two copies stay separate.
/// </summary>
public static class Dlss5GamePreferences
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex TagPattern = new("^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$", RegexOptions.CultureInvariant);

    /// <summary>Overridable for tests.</summary>
    internal static string StorePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "Adas", "dlss5-game-preferences.json");

    public static bool IsValidReleaseTag(string? tag) => tag != null && TagPattern.IsMatch(tag);

    private static string Key(string gameName, string? store)
        => string.IsNullOrWhiteSpace(store) ? gameName : $"{gameName}|{store}";

    public static Dlss5GamePreference Get(string gameName, string? store = null)
    {
        lock (Gate)
        {
            var all = Load();
            return all.TryGetValue(Key(gameName, store), out var value) ? value : new Dlss5GamePreference();
        }
    }

    public static void Update(string gameName, string? store, Action<Dlss5GamePreference> change)
    {
        lock (Gate)
        {
            var all = Load();
            var key = Key(gameName, store);
            if (!all.TryGetValue(key, out var value)) all[key] = value = new Dlss5GamePreference();
            change(value);
            if (value.FeederChannel == Dlss5FeederChannel.ExactRelease && !IsValidReleaseTag(value.FeederReleaseTag))
                value.FeederChannel = Dlss5FeederChannel.Bundled;
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            var temporary = StorePath + $".{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(all, JsonOptions));
            File.Move(temporary, StorePath, overwrite: true);
        }
    }

    /// <summary>
    /// Turns the stored choices into install overrides. <paramref name="mode"/> picks the automatic motion
    /// provider: VORT on OpenGL (LumeniteFX reads nothing there), LumeniteFX Kernel everywhere else.
    /// </summary>
    public static (string? FeederReleaseTag, Dlss5MotionProvider Provider) ResolveInstallChoices(
        Dlss5GamePreference preference, Dlss5DeploymentMode mode)
    {
        var tag = preference.FeederChannel switch
        {
            Dlss5FeederChannel.NewestPrerelease => Dlss5ComponentService.NewestPrereleaseTag,
            Dlss5FeederChannel.ExactRelease when IsValidReleaseTag(preference.FeederReleaseTag) => preference.FeederReleaseTag,
            _ => null,
        };
        var provider = preference.MotionProvider ?? DefaultMotionProvider(mode);
        return (tag, provider);
    }

    public static Dlss5MotionProvider DefaultMotionProvider(Dlss5DeploymentMode mode)
        => mode == Dlss5DeploymentMode.OpenGlFeeder ? Dlss5MotionProvider.VortMotion : Dlss5MotionProvider.LumeniteKernel;

    private static Dictionary<string, Dlss5GamePreference> Load()
    {
        try
        {
            if (File.Exists(StorePath) && new FileInfo(StorePath).Length < 2 * 1024 * 1024)
                return JsonSerializer.Deserialize<Dictionary<string, Dlss5GamePreference>>(File.ReadAllText(StorePath))
                       is { } loaded
                    ? new Dictionary<string, Dlss5GamePreference>(loaded, StringComparer.OrdinalIgnoreCase)
                    : new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[Dlss5GamePreferences] Ignoring unreadable preferences: {ex.Message}");
        }
        return new(StringComparer.OrdinalIgnoreCase);
    }
}
