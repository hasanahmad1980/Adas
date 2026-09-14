using System;
using System.Collections.Generic;
using System.Linq;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

/// <summary>
/// Focused, testable view-model for the per-game setup pane's assessment. It owns the pure
/// probe → assess → route-build → readiness computation that used to live inline in the Avalonia
/// code-behind (reached through a global service locator), plus the "latest assessment wins"
/// <see cref="GenerationGate"/>. Dependencies arrive by constructor injection, so the whole
/// assessment can be unit-tested with real engine services and no UI. The Avalonia view keeps only
/// the UI application of a <see cref="GameSetupAssessment"/>; this deliberately does NOT touch
/// <c>MainViewModel</c> — it extracts one responsibility, not a broad rewrite.
/// </summary>
internal sealed class GameSetupViewModel
{
    private readonly Dlss5CompatibilityService _compat;
    private readonly DeepFriedChickenService? _deepFriedChicken;
    private readonly GenerationGate _gate = new();

    public GameSetupViewModel(Dlss5CompatibilityService compat, DeepFriedChickenService? deepFriedChicken = null)
    {
        _compat = compat ?? throw new ArgumentNullException(nameof(compat));
        _deepFriedChicken = deepFriedChicken;
    }

    /// <summary>Starts a new assessment generation, superseding any in-flight one (latest wins).</summary>
    public GenerationToken BeginAssessment() => _gate.Begin();

    /// <summary>
    /// Runs the full compatibility assessment for a captured operation snapshot and builds every route
    /// (all shown, recommended marked). Pure and side-effect free — safe to run on a background thread.
    /// </summary>
    public GameSetupAssessment Assess(Dlss5GameOperation op)
    {
        var probe = _compat.Probe(op, op.ApiOverride);
        var assessment = Dlss5CompatibilityService.Assess(probe, singlePlayerConfirmed: true);
        var hasUpscaler = Dlss5ToolCompatibility.HasUpscalerRuntime(
            assessment.DeploymentPath ?? op.InstallPath, probe.HasNativeDlss);

        // Seed from an existing install of the same mode, else auto-pick from renderer/arch.
        var installed = assessment.DeploymentPath is { } dp ? Dlss5ComponentService.LoadRecord(dp) : null;
        var seed = installed?.Mode == assessment.Mode ? installed.Profile : Dlss5InstallProfile.MaximumQuality;
        var pick = Dlss5RouteCatalog.Recommend(assessment, seed);

        var routes = Dlss5RouteCatalog.Build(assessment, pick, installed?.Profile,
            deepFriedChickenAvailable: _deepFriedChicken?.IsImported == true,
            installedDeepFriedChicken: installed?.DeepFriedChicken == true,
            installedBridgeSubstitute: installed?.BridgeSubstitute == true);
        var preferred = routes.FirstOrDefault(r => r.Installed)
                        ?? routes.FirstOrDefault(r => r.Profile == pick && r.Supported && !r.DeepFriedChicken && !r.BridgeSubstitute)
                        ?? routes.FirstOrDefault(r => r.Recommended)
                        ?? routes.FirstOrDefault();

        GameStatus dlss5Status;
        string? dlss5Label = null;
        string? installedRoot = null;
        Dlss5InstallRecord? installedRecord = null;

        // DLSS 5 has its own on-disk record and is NOT part of the RenoDX-only Status; drive the badge
        // from the record so a successful install flips it off "Available".
        if (installed is not null)
        {
            installedRoot = assessment.DeploymentPath;
            installedRecord = installed;
            dlss5Status = GameStatus.Installed;
            var active = routes.FirstOrDefault(r => r.Installed);
            dlss5Label = "Active route: "
                + (active?.Label ?? installed.Profile.ToString())
                + (string.IsNullOrWhiteSpace(installed.ComponentVersion) ? "" : $" ({installed.ComponentVersion})");
        }
        else
        {
            dlss5Status = assessment.Mode != Dlss5DeploymentMode.None ? GameStatus.Available : GameStatus.NotInstalled;
        }

        var readiness = Dlss5ReadinessText.Describe(assessment, op.InstallPath);
        var summary = installed is not null && readiness.State == Dlss5ReadinessState.Ready
            ? "DLSS 5 is installed on this game. Launch the game to use it — or tick a different route or tools below and press Install selected."
            : readiness.Headline;

        return new GameSetupAssessment(
            probe, assessment, hasUpscaler, routes, preferred,
            dlss5Status, dlss5Label, installedRoot, installedRecord, readiness, summary);
    }
}

/// <summary>The result of one <see cref="GameSetupViewModel.Assess"/> — everything the setup pane binds.</summary>
internal sealed record GameSetupAssessment(
    Dlss5Probe Probe,
    Dlss5Assessment Assessment,
    bool HasUpscalerRuntime,
    IReadOnlyList<RouteOption> Routes,
    RouteOption? Preferred,
    GameStatus Dlss5Status,
    string? Dlss5Label,
    string? InstalledRoot,
    Dlss5InstallRecord? InstalledRecord,
    Dlss5Readiness Readiness,
    string Summary);
