using System;
using System.Collections.Generic;
using System.Linq;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public enum Dlss5ReadinessState
{
    /// <summary>Everything checks out — the user only has to press Install.</summary>
    Ready,
    /// <summary>The only thing in the way is which folder holds the game; the user can fix it with a folder picker.</summary>
    NeedsGameFolder,
    /// <summary>Something the user can't fix from the route list (unsupported GPU, anti-cheat, …).</summary>
    Blocked,
}

/// <summary>
/// A beginner-friendly reading of a <see cref="Dlss5Assessment"/>: one headline, the real problems in plain
/// language, and — kept separate so they never read as errors — the pieces Adas downloads and sets up itself.
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
        "Click \"Choose game folder…\" and select the folder that contains the game's .exe file.";

    public static Dlss5Readiness Describe(Dlss5Assessment assessment)
    {
        var autoSetup = assessment.MissingRequirements
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(FriendlyRequirement)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (assessment.CanInstall)
        {
            return new Dlss5Readiness(Dlss5ReadinessState.Ready,
                $"Ready to install. Adas detected a {FriendlyRenderer(assessment.Mode)} game ({(assessment.Is64Bit ? "64-bit" : "32-bit")}) "
                + "and picked the best option for you — just click Install.",
                Array.Empty<string>(), autoSetup);
        }

        if (Dlss5CompatibilityService.CanConfirmDeploymentPath(assessment))
        {
            var ambiguous = assessment.BlockingReasons.Contains(Dlss5CompatibilityService.AmbiguousDeploymentPathReason);
            return new Dlss5Readiness(Dlss5ReadinessState.NeedsGameFolder,
                "One quick step first: show Adas where the game is installed.",
                new[]
                {
                    (ambiguous
                        ? "Adas found more than one folder that could be the game."
                        : "Adas couldn't find the game's program files.") + " " + ChooseFolderHint,
                },
                autoSetup);
        }

        var problems = new List<string>();
        var rendererUnknown = assessment.Mode == Dlss5DeploymentMode.None;
        foreach (var reason in assessment.BlockingReasons.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var friendly = FriendlyBlocker(reason);
            if (friendly is null && rendererUnknown) continue; // raw detector evidence; summarised below
            problems.Add(friendly ?? reason);
        }
        if (rendererUnknown)
            problems.Add("Adas couldn't tell which graphics technology (DirectX, Vulkan or OpenGL) this game uses. "
                         + "Launch the game once and come back, or pick it under Advanced options → Graphics API.");
        if (problems.Count == 0)
            problems.Add("This game isn't supported yet.");

        return new Dlss5Readiness(Dlss5ReadinessState.Blocked,
            "DLSS 5 can't be installed on this game yet.",
            problems.Distinct().ToArray(), autoSetup);
    }

    internal static string? FriendlyBlocker(string reason)
    {
        if (reason.Equals(Dlss5CompatibilityService.AmbiguousDeploymentPathReason, StringComparison.Ordinal))
            return "Adas found more than one folder that could be the game. " + ChooseFolderHint;
        if (reason.Equals(Dlss5CompatibilityService.MissingDeploymentPathReason, StringComparison.Ordinal))
            return "Adas couldn't find the game's program files. " + ChooseFolderHint;
        if (reason.StartsWith("This package requires an NVIDIA", StringComparison.Ordinal))
            return "Your graphics card isn't supported. DLSS 5 needs an NVIDIA GeForce RTX 20, 30, 40 or 50-series card.";
        if (reason.StartsWith("Detected anti-cheat software:", StringComparison.Ordinal))
            return "This game uses anti-cheat" + Between(reason, ":", ".") + ". To keep your account safe, Adas won't change it.";
        if (reason.StartsWith("Detected multiplayer/online-only evidence:", StringComparison.Ordinal))
            return "This looks like an online game" + Between(reason, ":", ". Adas") + ". Adas only changes single-player games, so you can't get banned.";
        if (reason.StartsWith("Online status is not verified", StringComparison.Ordinal))
            return "Please confirm you play this game offline / single-player.";
        if (reason.StartsWith("Microsoft Visual C++", StringComparison.Ordinal))
            return "A free Microsoft component is missing: Visual C++ 2015–2022 Redistributable"
                   + Between(reason, "runtime", " is missing") + ". Install it from microsoft.com, then come back. Nothing in the game was changed.";
        if (reason.StartsWith("Install the Vulkan ReShade layer", StringComparison.Ordinal))
            return "This Vulkan game needs ReShade set up first. Click \"ReShade\" under Extras below, then come back here.";
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
