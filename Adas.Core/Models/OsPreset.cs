namespace RenoDXCommander.Models;

/// <summary>
/// Holds captured OptiScaler cog settings for one user-configurable preset slot.
/// Null fields mean "not captured" — they are not applied when loading the preset.
/// </summary>
public class OsPreset
{
    public string? Name                   { get; set; }  // user-editable label
    public string? FgInput                { get; set; }  // INI value e.g. "upscaler", "auto"
    public string? FgOutput               { get; set; }  // INI value e.g. "dlssg", "auto"
    public string? FgNvngxReplacement     { get; set; }  // INI value e.g. "Arturs", "None"
    public bool?   DeployStreamline       { get; set; }
    public bool?   DeployDlssEnabler      { get; set; }
    public string? SrPreset               { get; set; }  // raw INI value e.g. "13", "auto"
    public string? RrPreset               { get; set; }  // raw INI value e.g. "4", "auto"
    public string? RenderScale            { get; set; }  // display name e.g. "Off", "67% Quality"
    public bool?   DisableFlipMetering    { get; set; }
    public string? HudFix                 { get; set; }  // "true", "false", "auto"
    public float?  FramerateLimit         { get; set; }  // fps value, 0 = Off
}
