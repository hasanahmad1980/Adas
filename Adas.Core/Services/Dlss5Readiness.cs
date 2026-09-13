using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public enum Dlss5ReadinessState
{
    /// <summary>Everything checks out — the user only has to press Install.</summary>
    Ready,
    /// <summary>Adas needs to know which folder holds the game; Install opens a folder picker.</summary>
    NeedsGameFolder,
    /// <summary>Adas couldn't tell the graphics API; Install asks the user to pick one.</summary>
    NeedsGraphicsApi,
    /// <summary>Installable, but there are risks the user should read first (anti-cheat, GPU, …). Never a hard stop.</summary>
    Warnings,
}

/// <summary>
/// A beginner-friendly reading of a <see cref="Dlss5Assessment"/>: one headline, the things to know in plain
/// language, and — kept separate so they never read as errors — the pieces Adas downloads and sets up itself.
/// Nothing here disables Install: folder and API questions are asked when Install is pressed, and warnings are
/// confirmed once.
/// </summary>
public sealed record Dlss5Readiness(
    Dlss5ReadinessState State,
    string Headline,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> AutoSetupItems)
{
    public bool NeedsGameFolder => State == Dlss5ReadinessState.NeedsGameFolder;

    /// <summary>Multi-line text suitable for a status message or dialog.</summary>
    public string ToMessage()
    {
        var lines = new List<string> { Headline };
        lines.AddRange(Problems.Select(p => "• " + p));
        return string.Join("\n", lines);
    }

    /// <summary>"Adas sets these up for you: …" or null when nothing needs setting up.</summary>
    public string? AutoSetupText => AutoSetupItems.Count == 0
        ? null
        : "Adas downloads and sets these up for you during install — nothing to do: " + string.Join(", ", AutoSetupItems) + ".";
}

/// <summary>Translates engine assessments into plain language for people new to modding.</summary>
public static class Dlss5ReadinessText
{
    public const string ChooseFolderHint =
        "Press Install (or \"Choose game folder…\") and select the folder that contains the game's .exe file.";

    public static Dlss5Readiness Describe(Dlss5Assessment assessment, string? installPath = null)
    {
        var autoSetup = assessment.MissingRequirements
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(FriendlyRequirement)
            .ToList();
        var reasons = assessment.BlockingReasons.Where(r => !string.IsNullOrWhiteSpace(r)).ToArray();
        if (reasons.Any(IsAutoFixed))
        {
            if (reasons.Any(r => r.StartsWith("Microsoft Visual C++", StringComparison.Ordinal)))
                autoSetup.Insert(0, "Microsoft Visual C++ runtime");
            if (reasons.Any(r => r.StartsWith("Install the Vulkan ReShade layer", StringComparison.Ordinal)))
                autoSetup.Insert(0, "the Vulkan ReShade layer");
        }
        var autoSetupItems = autoSetup.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (reasons.Any(r => r.Equals(Dlss5CompatibilityService.AmbiguousDeploymentPathReason, StringComparison.Ordinal)
                             || r.Equals(Dlss5CompatibilityService.MissingDeploymentPathReason, StringComparison.Ordinal)))
        {
            var ambiguous = reasons.Contains(Dlss5CompatibilityService.AmbiguousDeploymentPathReason);
            var gone = !ambiguous && !string.IsNullOrWhiteSpace(installPath) && !Directory.Exists(installPath);
            return new Dlss5Readiness(Dlss5ReadinessState.NeedsGameFolder,
                gone ? "The game folder Adas remembers no longer exists." : "One quick step first: show Adas where the game is installed.",
                new[]
                {
                    (ambiguous
                        ? "Adas found more than one folder that could be the game."
                        : gone
                            ? $"\"{installPath}\" is gone — the game was probably moved or uninstalled."
                            : "Adas couldn't find the game's program files.") + " " + ChooseFolderHint,
                },
                autoSetupItems);
        }

        var problems = new List<string>();
        foreach (var reason in reasons)
        {
            if (IsAutoFixed(reason)) continue; // listed under auto-setup, not as a problem
            var friendly = FriendlyBlocker(reason);
            if (friendly is null && assessment.Mode == Dlss5DeploymentMode.None) continue; // raw detector evidence; summarised below
            problems.Add(friendly ?? reason);
        }

        if (assessment.Mode == Dlss5DeploymentMode.None)
        {
            problems.Insert(0, "Adas couldn't tell which graphics technology (DirectX, Vulkan or OpenGL) this game uses. "
                               + "Pick it in \"Graphics API\" above, or just press Install and Adas will ask.");
            return new Dlss5Readiness(Dlss5ReadinessState.NeedsGraphicsApi,
                "Which graphics API does this game use?", problems.Distinct().ToArray(), autoSetupItems);
        }

        var renderer = $"{FriendlyRenderer(assessment.Mode)} game ({(assessment.Is64Bit ? "64-bit" : "32-bit")})";
        if (problems.Count == 0)
        {
            return new Dlss5Readiness(Dlss5ReadinessState.Ready,
                $"Ready to install. Adas detected a {renderer} and picked the best option for you — just click Install.",
                Array.Empty<string>(), autoSetupItems);
        }

        return new Dlss5Readiness(Dlss5ReadinessState.Warnings,
            $"Adas detected a {renderer}. You can install — read these first:",
            problems.Distinct().ToArray(), autoSetupItems);
    }

    private static bool IsAutoFixed(string reason)
        => reason.StartsWith("Microsoft Visual C++", StringComparison.Ordinal)
           || reason.StartsWith("Install the Vulkan ReShade layer", StringComparison.Ordinal);

    internal static string? FriendlyBlocker(string reason)
    {
        if (reason.Equals(Dlss5CompatibilityService.AmbiguousDeploymentPathReason, StringComparison.Ordinal))
            return "Adas found more than one folder that could be the game. " + ChooseFolderHint;
        if (reason.Equals(Dlss5CompatibilityService.MissingDeploymentPathReason, StringComparison.Ordinal))
            return "Adas couldn't find the game's program files. " + ChooseFolderHint;
        if (reason.StartsWith("This package requires an NVIDIA", StringComparison.Ordinal))
            return "Adas didn't find an NVIDIA GeForce RTX 20/30/40/50-series card. DLSS 5 neural rendering needs one, so it will likely do nothing on this PC.";
        if (reason.StartsWith("Detected anti-cheat software:", StringComparison.Ordinal))
            return "This game uses anti-cheat" + Between(reason, ":", ".") + ". Modding its files can get you banned online — only continue if you play offline.";
        if (reason.StartsWith("Detected multiplayer/online-only evidence:", StringComparison.Ordinal))
            return "This looks like an online game" + Between(reason, ":", ". Adas") + ". Modded files can get you banned in online modes.";
        if (reason.StartsWith("Online status is not verified", StringComparison.Ordinal))
            return "Only use this in single-player / offline — modded files can get you banned online.";
        if (reason.StartsWith("Microsoft Visual C++", StringComparison.Ordinal))
            return "Microsoft Visual C++ 2015–2022 runtime" + Between(reason, "runtime", " is missing")
                   + " is missing — Adas installs it for you when you press Install.";
        if (reason.StartsWith("Install the Vulkan ReShade layer", StringComparison.Ordinal))
            return "This Vulkan game needs the Vulkan ReShade layer — Adas sets it up for you when you press Install.";
        return null;
    }

    internal static string FriendlyRequirement(string requirement)
    {
        if (requirement.StartsWith("ReShade", StringComparison.OrdinalIgnoreCase)) return "ReShade";
        if (requirement.StartsWith("RenoDX", StringComparison.OrdinalIgnoreCase)) return "the RenoDX DLSS 5 add-on";
        if (requirement.StartsWith("nvngx_dlssnr", StringComparison.OrdinalIgnoreCase)) return "NVIDIA's neural rendering files";
        if (requirement.StartsWith("nvngx_dlss", StringComparison.OrdinalIgnoreCase)) return "NVIDIA DLSS upscaling files";
        if (requirement.Contains("motion-vector", StringComparison.OrdinalIgnoreCase)) return "LumeniteFX motion data";
        if (requirement.StartsWith("dgVoodoo2", StringComparison.OrdinalIgnoreCase)) return "dgVoodoo2 (lets older games use modern graphics)";
        if (requirement.StartsWith("DXVK", StringComparison.OrdinalIgnoreCase)) return "DXVK (lets this game use Vulkan)";
        var paren = requirement.IndexOf(" (", StringComparison.Ordinal);
        return paren > 0 ? requirement[..paren] : requirement;
    }

    internal static string FriendlyRenderer(Dlss5DeploymentMode mode) => mode switch
    {
        Dlss5DeploymentMode.NativeDirectX12 or Dlss5DeploymentMode.Dx12Feeder => "DirectX 12",
        Dlss5DeploymentMode.NativeDirectX11 or Dlss5DeploymentMode.Dx11Feeder => "DirectX 11",
        Dlss5DeploymentMode.Dx10Feeder or Dlss5DeploymentMode.Dx10ViaDxvkFeeder => "DirectX 10",
        Dlss5DeploymentMode.Dx9Feeder or Dlss5DeploymentMode.Dx9ViaDxvkFeeder => "DirectX 9",
        Dlss5DeploymentMode.Dx8Feeder => "DirectX 8",
        Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.NativeVulkan => "Vulkan",
        Dlss5DeploymentMode.OpenGlFeeder => "OpenGL",
        _ => new Dlss5Assessment(mode, null, Array.Empty<string>(), Array.Empty<string>(), true, true).ModeLabel,
    };

    private static string Between(string text, string start, string end)
    {
        var s = text.IndexOf(start, StringComparison.Ordinal);
        if (s < 0) return "";
        s += start.Length;
        var e = text.IndexOf(end, s, StringComparison.Ordinal);
        var inner = (e < 0 ? text[s..] : text[s..e]).Trim().TrimEnd('.');
        return inner.Length == 0 ? "" : $" ({inner.Trim('(', ')')})";
    }
}
