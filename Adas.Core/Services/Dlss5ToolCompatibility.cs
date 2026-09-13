using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>Optional tools the setup page can install alongside (or instead of) a DLSS 5 route.</summary>
public enum SetupTool
{
    OptiScaler,
    Dxvk,
    ReShade,
    DisplayCommander,
}

public enum ToolNoteLevel
{
    /// <summary>Fits this setup.</summary>
    Good,
    /// <summary>Installable, with something worth knowing.</summary>
    Caution,
    /// <summary>Known not to work with this game or another selection. Still selectable — the user decides.</summary>
    Conflict,
    /// <summary>Does nothing for this game.</summary>
    NoEffect,
}

public sealed record ToolNote(ToolNoteLevel Level, string Text);

/// <summary>Everything the compatibility rules look at. <see cref="Route"/> is null when no DLSS 5 route is selected.</summary>
public sealed record ToolContext(
    GraphicsApiType Api,
    bool Is64Bit,
    Dlss5DeploymentMode Mode,
    Dlss5InstallProfile? Route,
    IReadOnlyCollection<SetupTool> Selected,
    bool HasNativeUpscaler = false,
    bool HasAntiCheat = false,
    bool ReShadeInstalled = false,
    bool ReLimiterInstalled = false);

/// <summary>
/// Compatibility notes for each optional tool, taken from the upstream projects' own documentation:
/// <list type="bullet">
/// <item>DLSS5-Feeder: ReShade 6.8+ with add-ons; stock OptiScaler and a second neural add-on are incompatible;
/// Display Commander is a listed known limitation.</item>
/// <item>DLSS5-ReShade-AIO: 64-bit DX9/11/12/Vulkan through ReShade add-ons; the game's own DLSS/FG should be off.</item>
/// <item>OptiScaler: DX11/DX12/Vulkan only, and only replaces an upscaler the game already has (DLSS 2+, FSR 2+, XeSS);
/// not for games with anti-cheat.</item>
/// <item>Display Commander: a ReShade add-on (addon64/addon32) that needs ReShade 6.6.2+.</item>
/// <item>DXVK: translates Direct3D 8/9/10/11 to Vulkan — never Direct3D 12; anti-cheat can flag it and exclusive
/// fullscreen is unavailable.</item>
/// </list>
/// Rules only ever produce notes; nothing here stops the user from installing a combination.
/// </summary>
public static class Dlss5ToolCompatibility
{
    public static string Name(SetupTool tool) => tool switch
    {
        SetupTool.OptiScaler => "OptiScaler",
        SetupTool.Dxvk => "DXVK",
        SetupTool.ReShade => "ReShade",
        SetupTool.DisplayCommander => "Display Commander",
        _ => tool.ToString(),
    };

    public static string Description(SetupTool tool) => tool switch
    {
        SetupTool.OptiScaler => "Swap the game's DLSS / FSR / XeSS for another upscaler, with an in-game menu (Insert).",
        SetupTool.Dxvk => "Run DirectX 8–11 games on Vulkan. Can fix stutter or crashes in older games.",
        SetupTool.ReShade => "Post-processing shaders and the add-on host other tools plug into.",
        SetupTool.DisplayCommander => "ReShade add-on for frame limiting, window mode and display fixes.",
        _ => "",
    };

    public static IReadOnlyList<ToolNote> Notes(SetupTool tool, ToolContext context) => tool switch
    {
        SetupTool.ReShade => ReShadeNotes(context),
        SetupTool.DisplayCommander => DisplayCommanderNotes(context),
        SetupTool.OptiScaler => OptiScalerNotes(context),
        SetupTool.Dxvk => DxvkNotes(context),
        _ => Array.Empty<ToolNote>(),
    };

    public static ToolNoteLevel Worst(IEnumerable<ToolNote> notes)
    {
        var list = notes.ToList();
        if (list.Any(n => n.Level == ToolNoteLevel.Conflict)) return ToolNoteLevel.Conflict;
        if (list.Any(n => n.Level == ToolNoteLevel.Caution)) return ToolNoteLevel.Caution;
        if (list.Count > 0 && list.All(n => n.Level == ToolNoteLevel.NoEffect)) return ToolNoteLevel.NoEffect;
        return ToolNoteLevel.Good;
    }

    /// <summary>Notes for the DLSS 5 route itself that come from the tools selected next to it.</summary>
    public static IReadOnlyList<ToolNote> RouteNotes(ToolContext context)
    {
        var notes = new List<ToolNote>();
        if (context.Route is null) return notes;
        if (IsAioRoute(context))
            notes.Add(new(ToolNoteLevel.Caution, "Turn the game's own DLSS and frame generation off — AIO provides its own (per the AIO README)."));
        if (IsFeederRoute(context) && context.Api == GraphicsApiType.Vulkan)
            notes.Add(new(ToolNoteLevel.Caution, "NVIDIA Smooth Motion doesn't work with the Feeder on Vulkan games (Feeder known limitation)."));
        if (context.Route == Dlss5InstallProfile.NeuralUpstream)
            notes.Add(new(ToolNoteLevel.Caution, "Use the game's DLSS Quality mode; never combine with another NGX/neural tool."));
        if (context.ReLimiterInstalled)
            notes.Add(new(ToolNoteLevel.Caution, "ReLimiter is installed in this game; if the game misbehaves, remove it first."));
        return notes;
    }

    /// <summary>True when the game folder ships an upscaler OptiScaler can hook (DLSS, FSR 2+/3, XeSS).</summary>
    public static bool HasUpscalerRuntime(string? folder, bool hasNativeDlss = false)
    {
        if (hasNativeDlss) return true;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 4 };
            return Directory.EnumerateFiles(folder, "*.dll", options).Select(Path.GetFileName).Any(name =>
                name is not null
                && (name.Equals("nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("sl.dlss.dll", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("libxess", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("amd_fidelityfx_", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("ffx_fsr", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return false;
    }

    private static IReadOnlyList<ToolNote> ReShadeNotes(ToolContext c)
    {
        var notes = new List<ToolNote>();
        if (IsOptiScalerNrRoute(c))
        {
            notes.Add(new(ToolNoteLevel.Conflict, "The OptiScaler DLSS-NR route runs without ReShade — installing ReShade on top can break it."));
            return notes;
        }
        if (c.Route is not null)
        {
            notes.Add(new(ToolNoteLevel.NoEffect, "Already included — the DLSS 5 route installs ReShade with add-on support."));
            return notes;
        }
        if (c.Api == GraphicsApiType.Vulkan)
            notes.Add(new(ToolNoteLevel.Good, "Vulkan game: installs the Vulkan ReShade layer (Windows asks for permission)."));
        else if (c.Api == GraphicsApiType.Unknown)
            notes.Add(new(ToolNoteLevel.Caution, "Graphics API unknown — Adas will guess the ReShade file name; set Graphics API above if ReShade doesn't load."));
        if (c.Selected.Contains(SetupTool.OptiScaler))
            notes.Add(new(ToolNoteLevel.Good, "Works with OptiScaler — Adas loads ReShade through OptiScaler (ReShade64.dll)."));
        if (c.Selected.Contains(SetupTool.Dxvk) && c.Api is GraphicsApiType.DirectX8 or GraphicsApiType.DirectX9 or GraphicsApiType.DirectX10 or GraphicsApiType.DirectX11)
            notes.Add(new(ToolNoteLevel.Good, "With DXVK the game renders through Vulkan, so ReShade uses the Vulkan layer."));
        if (c.HasAntiCheat)
            notes.Add(new(ToolNoteLevel.Caution, "Anti-cheat detected — ReShade add-ons can be flagged in online modes."));
        if (notes.Count == 0)
            notes.Add(new(ToolNoteLevel.Good, "Works with this game."));
        return notes;
    }

    private static IReadOnlyList<ToolNote> DisplayCommanderNotes(ToolContext c)
    {
        var notes = new List<ToolNote>();
        var reShadePresent = c.ReShadeInstalled || c.Selected.Contains(SetupTool.ReShade) || (c.Route is not null && !IsOptiScalerNrRoute(c));
        if (IsOptiScalerNrRoute(c))
            notes.Add(new(ToolNoteLevel.Conflict, "Needs ReShade, and the OptiScaler DLSS-NR route runs without ReShade."));
        else if (!reShadePresent)
            notes.Add(new(ToolNoteLevel.Caution, "Display Commander is a ReShade add-on (ReShade 6.6.2+) — also tick ReShade, or pick a DLSS 5 route."));
        if (IsFeederRoute(c))
            notes.Add(new(ToolNoteLevel.Conflict, "The DLSS5-Feeder README lists Display Commander as a known limitation — it may not work with this route."));
        if (c.ReLimiterInstalled)
            notes.Add(new(ToolNoteLevel.Conflict, "ReLimiter is installed — the two frame limiters fight each other. Remove ReLimiter first."));
        if (notes.Count == 0)
            notes.Add(new(ToolNoteLevel.Good, c.Route is not null ? "Works — loads as an add-on of the route's ReShade." : "Works with this game."));
        return notes;
    }

    private static IReadOnlyList<ToolNote> OptiScalerNotes(ToolContext c)
    {
        var notes = new List<ToolNote>();
        if (c.Api is GraphicsApiType.DirectX8 or GraphicsApiType.DirectX9 or GraphicsApiType.DirectX10 or GraphicsApiType.OpenGL)
            notes.Add(new(ToolNoteLevel.Conflict, $"OptiScaler supports DirectX 11, DirectX 12 and Vulkan only — this game uses {GraphicsApiDetector.GetLabel(c.Api)}."));
        if (!c.Is64Bit)
            notes.Add(new(ToolNoteLevel.Conflict, "OptiScaler needs a 64-bit game."));
        if (!c.HasNativeUpscaler)
            notes.Add(new(ToolNoteLevel.Caution, "OptiScaler only replaces an upscaler the game already has (DLSS 2+, FSR 2+ or XeSS) — none was found in the game folder."));
        if (IsOptiScalerNrRoute(c))
            notes.Add(new(ToolNoteLevel.Conflict, "The selected OptiScaler DLSS-NR route already installs its own OptiScaler build."));
        else if (IsFeederRoute(c))
            notes.Add(new(ToolNoteLevel.Conflict, "Stock OptiScaler is incompatible with the DLSS5-Feeder (Feeder README)."));
        else if (IsAioRoute(c))
            notes.Add(new(ToolNoteLevel.Caution, "AIO provides its own DLSS/DLAA — running OptiScaler too usually means two upscalers fighting."));
        else if (c.Route == Dlss5InstallProfile.NeuralUpstream)
            notes.Add(new(ToolNoteLevel.Conflict, "Neural Upstream hooks the game's own DLSS — never combine it with another NGX consumer like OptiScaler."));
        else if (c.Route is not null)
            notes.Add(new(ToolNoteLevel.Caution, "Two things will hook the game's upscaler. If the image breaks, remove OptiScaler."));
        if (c.Selected.Contains(SetupTool.Dxvk) && c.Api is GraphicsApiType.DirectX11)
            notes.Add(new(ToolNoteLevel.Caution, "With DXVK the game becomes Vulkan; OptiScaler must then run in Vulkan mode."));
        if (c.HasAntiCheat)
            notes.Add(new(ToolNoteLevel.Caution, "Anti-cheat detected — OptiScaler is not meant for online games and can get you banned."));
        if (notes.Count == 0)
            notes.Add(new(ToolNoteLevel.Good, "Works with this game."));
        return notes;
    }

    private static IReadOnlyList<ToolNote> DxvkNotes(ToolContext c)
    {
        var notes = new List<ToolNote>();
        switch (c.Api)
        {
            case GraphicsApiType.DirectX12:
                notes.Add(new(ToolNoteLevel.NoEffect, "DXVK doesn't support DirectX 12 — it does nothing for this game."));
                return notes;
            case GraphicsApiType.Vulkan:
                notes.Add(new(ToolNoteLevel.NoEffect, "The game already runs on Vulkan — DXVK does nothing."));
                return notes;
            case GraphicsApiType.OpenGL:
                notes.Add(new(ToolNoteLevel.NoEffect, "DXVK only translates DirectX — it does nothing for OpenGL games."));
                return notes;
        }

        if (c.Route is not null)
        {
            if (c.Mode is Dlss5DeploymentMode.Dx9ViaDxvkFeeder or Dlss5DeploymentMode.Dx10ViaDxvkFeeder)
                notes.Add(new(ToolNoteLevel.NoEffect, "Already included — this DLSS 5 route installs DXVK itself."));
            else if (c.Mode is Dlss5DeploymentMode.Dx9Feeder or Dlss5DeploymentMode.Dx8Feeder)
                notes.Add(new(ToolNoteLevel.Conflict, "This DLSS 5 route translates through dgVoodoo2 to DirectX 11 — DXVK would replace the same DLLs."));
            else
                notes.Add(new(ToolNoteLevel.Conflict, "The DLSS 5 route is set up for DirectX; DXVK turns the game into Vulkan, so the route stops matching."));
        }
        if (c.Api == GraphicsApiType.Unknown)
            notes.Add(new(ToolNoteLevel.Caution, "Graphics API unknown — DXVK only helps DirectX 8–11 games."));
        if (c.HasAntiCheat)
            notes.Add(new(ToolNoteLevel.Conflict, "Anti-cheat detected — DXVK is known to trigger bans in online games."));
        notes.Add(new(ToolNoteLevel.Caution, "Exclusive fullscreen isn't available and the first runs can stutter while shaders compile."));
        return notes;
    }

    private static bool IsOptiScalerNrRoute(ToolContext c) => Dlss5ComponentService.IsOptiScalerNrProfile(c.Route);

    private static bool IsAioRoute(ToolContext c) => c.Route == Dlss5InstallProfile.StandaloneAio;

    internal static bool IsFeederRoute(ToolContext c)
    {
        if (c.Route is not { } route || IsOptiScalerNrRoute(c) || IsAioRoute(c)
            || route is Dlss5InstallProfile.NeuralUpstream or Dlss5InstallProfile.OpenGlBridge
            || c.Mode == Dlss5DeploymentMode.None)
            return false;
        try
        {
            return Dlss5ComponentService.GetCompatibilityPlan(c.Mode, c.Is64Bit, route).InstallFeeder;
        }
        catch (Exception)
        {
            return Dlss5CompatibilityService.IsFeederMode(c.Mode);
        }
    }
}
