using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Adas.App.Shell;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Abstractions;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace Adas.App.Views;

/// <summary>
/// Per-game setup pane. Its <see cref="Control.DataContext"/> is the selected
/// <see cref="GameCardViewModel"/>; the owning <see cref="MainViewModel"/> is injected via
/// <see cref="Main"/> so install actions run through the same engine code paths the WinUI shell used.
/// <para>
/// Design rule: nothing is ever greyed out. The user ticks a DLSS 5 route and/or tools, every choice shows what
/// it works (and doesn't work) with, and one "Install selected" button runs it all — asking for the game folder
/// or graphics API only when it truly can't continue without them, and confirming warnings once.
/// </para>
/// </summary>
public partial class GameSetupView : UserControl
{
    /// <summary>Set by the host window so buttons can invoke shared install commands.</summary>
    public MainViewModel? Main { get; set; }

    private static readonly GraphicsApiType[] AllApis =
    {
        GraphicsApiType.DirectX12, GraphicsApiType.DirectX11, GraphicsApiType.Vulkan, GraphicsApiType.DirectX9,
        GraphicsApiType.OpenGL, GraphicsApiType.DirectX10, GraphicsApiType.DirectX8,
    };

    public GameSetupView()
    {
        InitializeComponent();

        InstallButton.Click += OnInstall;
        RepairButton.Click += OnRepair;
        RemoveButton.Click += OnRemove;
        RoutesList.SelectionChanged += OnRouteSelectionChanged;
        InstallDlss5Check.IsCheckedChanged += (_, _) => UpdateSelectionState();
        DiagnoseButton.Click += OnDiagnose;
        ShareWorkedButton.Click += (_, _) => _ = ShareResultAsync(true);
        ShareFailedButton.Click += (_, _) => _ = ShareResultAsync(false);
        CommunityGpuOnlyCheck.IsCheckedChanged += (_, _) => ApplyCommunityResults();
        RrUpdateButton.Click += OnRrUpdate;
        RrRestoreButton.Click += OnRrRestore;
        OpenFolderButton.Click += OnOpenFolder;

        BitnessCombo.ItemsSource = new[] { "Auto", "32-bit", "64-bit" };
        ApiCombo.ItemsSource = new[] { "Auto", "DirectX8", "DirectX9", "DirectX10", "DirectX11", "DirectX12", "Vulkan", "OpenGL" };
        ReShadeCombo.ItemsSource = new[] { "Default", "Stable", "Nightly", "Custom" };
        DxvkCombo.ItemsSource = new[] { "Default", "Development", "Stable", "LiliumHdr" };
        ShaderModeCombo.ItemsSource = new[] { "Global", "Custom", "Select", "Off" };

        BitnessCombo.SelectionChanged += OnBitnessChanged;
        ApiCombo.SelectionChanged += OnApiChanged;
        ReShadeCombo.SelectionChanged += OnReShadeChannelChanged;
        DxvkCombo.SelectionChanged += OnDxvkVariantChanged;
        ShaderModeCombo.SelectionChanged += OnShaderModeChanged;
        ChoosePacksButton.Click += OnChoosePacks;
        ChangeFolderButton.Click += OnChangeFolder;
        ChooseFolderInlineButton.Click += OnChangeFolder;
        ResetFolderButton.Click += OnResetFolder;
        ImportDfcButton.Click += OnImportDeepFriedChicken;
        PreviewButton.Click += OnPreview;
        FsrFgCheck.IsCheckedChanged += OnFsrFgChanged;
        FeederChannelCombo.ItemsSource = new[] { "Packaged (default)", "Newest pre-release", "Exact release…" };
        MotionProviderCombo.ItemsSource = new[] { "Automatic", "LumeniteFX Kernel", "VORT Motion" };
        FeederChannelCombo.SelectionChanged += (_, _) => OnFeederChannelChanged();
        FeederTagBox.LostFocus += (_, _) => OnFeederChannelChanged();
        MotionProviderCombo.SelectionChanged += (_, _) => OnMotionProviderChanged();
        InitTuning();

        _tools = Enum.GetValues<SetupTool>()
            .Select(tool => new SetupToolItem(tool, _ => UpdateSelectionState(), item => _ = RemoveToolAsync(item)))
            .ToArray();
        ToolsList.ItemsSource = _tools;

        DataContextChanged += (_, _) => _ = RefreshAssessmentAsync();
    }

    /// <summary>True while combos are being seeded from the card, so SelectionChanged handlers
    /// don't write the value straight back and trigger spurious re-assessments.</summary>
    private bool _populatingOverrides;

    /// <summary>True while routes are being re-bound, so the user's route choice isn't overwritten.</summary>
    private bool _populatingRoutes;

    /// <summary>True while an install/remove runs — the only time the buttons are disabled.</summary>
    private bool _busy;

    /// <summary>Focused assessment view-model (DI-constructed). Owns the latest-wins generation gate and the
    /// pure probe/assess/route computation, so the code-behind only applies results to the UI.</summary>
    private GameSetupViewModel? _setupVm;
    private GameSetupViewModel? SetupVm => _setupVm ??= AppServices.Services.GetService<GameSetupViewModel>();

    /// <summary>Cancels the probe for a superseded selection/refresh so it doesn't waste work applying.</summary>
    private System.Threading.CancellationTokenSource? _assessmentCts;

    private readonly SetupToolItem[] _tools;

    private Dlss5Probe? _probe;
    private Dlss5Assessment? _assessment;
    private bool _hasUpscalerRuntime;

    /// <summary>Plain-language reading of the latest assessment; drives the banner.</summary>
    private Dlss5Readiness? _readiness;

    /// <summary>Every route from the latest assessment — all shown, none hidden.</summary>
    private IReadOnlyList<RouteOption> _allRoutes = Array.Empty<RouteOption>();

    /// <summary>The card the page was last populated for; selections reset when it changes.</summary>
    private GameCardViewModel? _populatedCard;

    /// <summary>The route the user explicitly clicked (profile + DFC flag), kept across refreshes.</summary>
    private (Dlss5InstallProfile Profile, bool DeepFriedChicken, bool BridgeSubstitute)? _userRoute;

    private string Store => Card?.Source ?? "";

    private GameCardViewModel? Card => DataContext as GameCardViewModel;

    /// <summary>Re-runs the route assessment for the current card. Used by the shell's
    /// RequestCardRebuild / RequestOverridesPanelRebuild seams after engine-side state changes.</summary>
    public Task RefreshAsync() => RefreshAssessmentAsync();

    /// <summary>
    /// Probes/assesses the selected game off the UI thread (honouring the user's Graphics API choice) and
    /// populates the banner, the API evidence, every route, and the tool checklist.
    /// </summary>
    private async Task RefreshAssessmentAsync()
    {
        var card = Card;
        if (card is null) return;
        var main = Main;
        var vm = SetupVm;
        if (main is null || vm is null) { RouteSummary.Text = "Compatibility service unavailable."; return; }

        // Freeze this game's identity, paths and Graphics API choice for the whole assessment.
        var op = Dlss5GameOperation.Capture(card, main.GetSingleApiOverride(card.GameName, card.Source ?? ""));

        // Latest assessment wins. Bump the generation and cancel any probe still running for a previous
        // selection/refresh, so a slow background probe can never apply its result over a newer one.
        _assessmentCts?.Cancel();
        var cts = _assessmentCts = new System.Threading.CancellationTokenSource();
        var ct = cts.Token;
        var generation = vm.BeginAssessment();

        if (!ReferenceEquals(_populatedCard, card))
        {
            _populatedCard = card;
            _userRoute = null;
            foreach (var tool in _tools) tool.SetSelectedSilently(false);
            InstallDlss5Check.IsChecked = !card.IsDlss5Installed;
            InstallResult.IsVisible = false;
        }

        RouteSummary.Text = "Checking this game…";
        ReadinessIcon.Text = "…";
        ProblemsText.IsVisible = false;
        AutoSetupText.IsVisible = false;
        ChooseFolderInlineButton.IsVisible = false;

        string summary = "";
        Dlss5Readiness? readiness = null;
        Dlss5Probe? probe = null;
        Dlss5Assessment? assessment = null;
        IReadOnlyList<RouteOption> routes = Array.Empty<RouteOption>();
        RouteOption? preferred = null;
        GameStatus dlss5Status = GameStatus.NotInstalled;
        string? dlss5Label = null;
        bool hasUpscaler = false;
        string? installedRoot = null;
        Dlss5InstallRecord? installedRecord = null;

        try
        {
        await Task.Run(() =>
        {
            try
            {
                var result = vm.Assess(op);
                probe = result.Probe;
                assessment = result.Assessment;
                hasUpscaler = result.HasUpscalerRuntime;
                routes = result.Routes;
                preferred = result.Preferred;
                dlss5Status = result.Dlss5Status;
                dlss5Label = result.Dlss5Label;
                installedRoot = result.InstalledRoot;
                installedRecord = result.InstalledRecord;
                readiness = result.Readiness;
                summary = result.Summary;
            }
            catch (Exception ex) { summary = $"Couldn't check this game: {ex.Message}. You can still pick options and install."; }
        }, ct);

        void Apply()
        {
            // Apply only the latest probe for the still-selected game: a superseded refresh (newer
            // generation started) or a changed selection must not clobber current UI or progress.
            if (!generation.IsCurrent || !ReferenceEquals(Card, card)) return;
            _probe = probe;
            _assessment = assessment;
            _hasUpscalerRuntime = hasUpscaler;
            RouteSummary.Text = summary;
            ApplyReadiness(readiness);

            _allRoutes = routes;
            _populatingRoutes = true;
            try
            {
                RoutesList.ItemsSource = routes;
                RoutesList.SelectedItem = (_userRoute is { } chosen
                                              ? routes.FirstOrDefault(r => r.Profile == chosen.Profile && r.DeepFriedChicken == chosen.DeepFriedChicken && r.BridgeSubstitute == chosen.BridgeSubstitute)
                                              : null)
                                          ?? preferred;
            }
            finally { _populatingRoutes = false; }

            card.Dlss5Status = dlss5Status;
            card.Dlss5InstalledLabel = dlss5Label;

            if (probe is not null)
                ReconcileCardWithProbe(card, probe);
            ApplyApiEvidence(card, probe);
            PopulateOverrides(card);
            _installedRoot = installedRoot;
            _installedRecord = installedRecord;
            PopulateDlss5Preferences(card);
            RefreshTuning();
            RefreshTips(card);
            UpdateSelectionState();
            _ = RefreshRrCardAsync(card, probe?.Is64Bit ?? !card.Is32Bit);
            _ = RefreshCommunityResultsAsync(card);
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else await Dispatcher.UIThread.InvokeAsync(Apply);
        }
        catch (OperationCanceledException)
        {
            // A newer refresh superseded this one before its probe started; the newer one owns the UI.
        }
    }

    /// <summary>
    /// Keeps the library badges (API + bitness) in line with the probe the page just used, so the header never
    /// contradicts the route reasons.
    /// </summary>
    private static void ReconcileCardWithProbe(GameCardViewModel card, Dlss5Probe probe)
    {
        var changed = false;
        if (card.Is32Bit == probe.Is64Bit)
        {
            card.Is32Bit = !probe.Is64Bit;
            changed = true;
        }
        if (probe.GraphicsApi != GraphicsApiType.Unknown && card.GraphicsApi != probe.GraphicsApi)
        {
            card.GraphicsApi = probe.GraphicsApi;
            card.DetectedApis = new HashSet<GraphicsApiType>(probe.SupportedGraphicsApis.Append(probe.GraphicsApi)
                .Where(api => api != GraphicsApiType.Unknown));
            changed = true;
        }
        if (changed) card.NotifyAll();
    }

    private void ApplyApiEvidence(GameCardViewModel card, Dlss5Probe? probe)
    {
        if (probe is null) { ApiEvidenceText.Text = ""; return; }
        var manual = Main?.GetSingleApiOverride(card.GameName, Store) is not null;
        var supported = probe.SupportedGraphicsApis.Where(a => a != GraphicsApiType.Unknown).ToArray();
        var canUse = supported.Length > 1
            ? " The game's files support: " + string.Join(", ", supported.Select(GraphicsApiDetector.GetLabel)) + "."
            : "";
        if (probe.GraphicsApi == GraphicsApiType.Unknown)
        {
            ApiEvidenceText.Text = "⚠ Adas couldn't detect the graphics API. Pick it above — or press Install and Adas will ask." + canUse;
            ApiEvidenceText.Foreground = Brush("#E3B341");
            return;
        }

        var label = GraphicsApiDetector.GetLabel(probe.GraphicsApi);
        var how = manual ? "your choice" : probe.GraphicsApiIsBestGuess ? "best guess" : "detected";
        ApiEvidenceText.Text = $"Using {label} ({how}). {probe.GraphicsApiEvidence}".TrimEnd()
                               + (manual ? "" : canUse)
                               + (probe.GraphicsApiIsBestGuess && !manual ? " Wrong? Change Graphics API above." : "");
        ApiEvidenceText.Foreground = probe.GraphicsApiIsBestGuess && !manual ? Brush("#E3B341") : Brush("#9BA6B4");
    }

    private RouteOption? SelectedRoute => InstallDlss5Check.IsChecked == true ? RoutesList.SelectedItem as RouteOption : null;

    private ToolContext BuildToolContext()
    {
        var card = Card;
        return new ToolContext(
            _probe?.GraphicsApi ?? card?.GraphicsApi ?? GraphicsApiType.Unknown,
            _assessment?.Is64Bit ?? !(card?.Is32Bit ?? false),
            _assessment?.Mode ?? Dlss5DeploymentMode.None,
            SelectedRoute?.Profile,
            _tools.Where(t => t.IsSelected).Select(t => t.Tool).ToArray(),
            HasNativeUpscaler: _hasUpscalerRuntime,
            HasAntiCheat: _probe?.AntiCheatEvidence.Count > 0,
            ReShadeInstalled: card?.IsRsInstalled == true,
            ReLimiterInstalled: card?.IsUlInstalled == true);
    }

    /// <summary>Recomputes every compatibility note and the Install button text from the current selection.</summary>
    private void UpdateSelectionState()
    {
        var card = Card;
        if (card is null) return;

        RoutesPanel.Opacity = InstallDlss5Check.IsChecked == true ? 1 : 0.55;
        var context = BuildToolContext();
        var route = SelectedRoute;

        // Route notes: why it isn't recommended (still installable) + advice from the upstream READMEs.
        var routeNotes = new List<string>();
        if (route is { Supported: false, Installed: false })
            routeNotes.Add(route.StatusText);
        routeNotes.AddRange(Dlss5ToolCompatibility.RouteNotes(context).Select(n => "⚠ " + n.Text));
        RouteNotesText.Text = string.Join("\n", routeNotes);
        RouteNotesText.IsVisible = route is not null && routeNotes.Count > 0;
        UpdateDriverWarning(route);

        foreach (var item in _tools)
        {
            item.IsInstalled = item.Tool switch
            {
                SetupTool.OptiScaler => card.IsOsInstalled,
                SetupTool.Dxvk => card.IsDxvkInstalled,
                SetupTool.ReShade => card.IsRsInstalled,
                SetupTool.DisplayCommander => card.IsDcInstalled,
                _ => false,
            };
            var notes = Dlss5ToolCompatibility.Notes(item.Tool, context);
            item.NotesText = string.Join("\n", notes.Select(n => n.Level switch
            {
                ToolNoteLevel.Good => "✓ ",
                ToolNoteLevel.NoEffect => "– ",
                ToolNoteLevel.Conflict => "✕ ",
                _ => "⚠ ",
            } + n.Text));
            item.NotesBrush = Dlss5ToolCompatibility.Worst(notes) switch
            {
                ToolNoteLevel.Good => Brush("#3FB950"),
                ToolNoteLevel.Caution => Brush("#E3B341"),
                ToolNoteLevel.Conflict => Brush("#F0883E"),
                _ => Brush("#9BA6B4"),
            };
        }

        var parts = new List<string>();
        if (route is not null) parts.Add("DLSS 5");
        parts.AddRange(_tools.Where(t => t.IsSelected).Select(t => t.Name));
        InstallButton.Content = parts.Count == 0 ? "Install selected" : "Install " + string.Join(" + ", parts);
        InstallButton.IsEnabled = !_busy;

        string? hint = parts.Count == 0
            ? "Tick “Install DLSS 5” and/or any tools above, then press Install."
            : _readiness?.State switch
            {
                Dlss5ReadinessState.NeedsGameFolder => "Install will first ask you where the game is installed.",
                Dlss5ReadinessState.NeedsGraphicsApi when route is not null => "Install will first ask which graphics API the game uses.",
                _ => null,
            };
        InstallHint.Text = hint ?? "";
        InstallHint.IsVisible = hint is not null;
    }

    private void OnRouteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_populatingRoutes && RoutesList.SelectedItem is RouteOption picked)
        {
            _userRoute = (picked.Profile, picked.DeepFriedChicken, picked.BridgeSubstitute);
            if (InstallDlss5Check.IsChecked != true) InstallDlss5Check.IsChecked = true;
        }
        UpdateSelectionState();
    }

    /// <summary>Paints the readiness banner: icon + colour by state, the things to know, the inline
    /// "Choose game folder…" fix, and the list of things Adas sets up automatically (never shown as errors).</summary>
    private void ApplyReadiness(Dlss5Readiness? readiness)
    {
        _readiness = readiness;
        (ReadinessIcon.Text, ReadinessBanner.Background, ReadinessBanner.BorderBrush) = readiness?.State switch
        {
            Dlss5ReadinessState.Ready => ("✓", Brush("#12261A"), Brush("#2E6B3F")),
            Dlss5ReadinessState.NeedsGameFolder => ("📁", Brush("#2B2410"), Brush("#8A6D2F")),
            Dlss5ReadinessState.NeedsGraphicsApi => ("?", Brush("#2B2410"), Brush("#8A6D2F")),
            Dlss5ReadinessState.Warnings => ("⚠", Brush("#2B2410"), Brush("#8A6D2F")),
            _ => ("⚠", Brush("#1A2230"), Brush("#2F3B4E")),
        };

        var problems = readiness?.Problems ?? Array.Empty<string>();
        ProblemsText.Text = problems.Count == 1 ? problems[0] : string.Join("\n", problems.Select(p => "• " + p));
        ProblemsText.IsVisible = problems.Count > 0;
        ChooseFolderInlineButton.IsVisible = readiness?.NeedsGameFolder == true;

        AutoSetupText.Text = readiness?.AutoSetupText ?? "";
        AutoSetupText.IsVisible = !string.IsNullOrEmpty(readiness?.AutoSetupText);
    }

    private static Avalonia.Media.IBrush Brush(string hex) => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex));

    /// <summary>
    /// Shows the known-bad-driver pre-flight warning for the selected route, keyed to the assessed
    /// deployment mode and the route's profile so it only fires for routes the driver actually breaks.
    /// </summary>
    private void UpdateDriverWarning(RouteOption? route)
    {
        var warning = route is null
            ? null
            : Dlss5CompatibilityService.GetDriverPreflightWarning(_assessment?.Mode ?? Dlss5DeploymentMode.None, route.Profile);
        DriverWarningText.Text = warning ?? "";
        DriverWarningBanner.IsVisible = !string.IsNullOrWhiteSpace(warning);
    }

    // ── Install selected ────────────────────────────────────────────────────

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InstallButton.IsEnabled = !busy;
        RepairButton.IsEnabled = !busy;
        RemoveButton.IsEnabled = !busy;
        InstallProgress.IsVisible = busy;
        if (busy) InstallProgress.Value = 0;
        // Keep the ray-reconstruction actions inside the same single-flight guard. Only disable here;
        // their correct enabled state (an update is available / a backup exists) is restored by
        // RefreshRrCardAsync after the operation, so we never wrongly re-enable them.
        if (busy)
        {
            RrUpdateButton.IsEnabled = false;
            RrRestoreButton.IsEnabled = false;
        }
    }

    private async void OnInstall(object? sender, RoutedEventArgs e)
    {
        var card = Card;
        var main = Main;
        var owner = this.FindAncestorOfType<Window>();
        if (main is null || card is null || owner is null || _busy) return;

        var route = SelectedRoute;
        var tools = _tools.Where(t => t.IsSelected).ToList();
        if (route is null && tools.Count == 0)
        {
            ShowResult(InstallDlss5Check.IsChecked == true
                ? "Pick a DLSS 5 route in the list (★ is the recommended one), or tick a tool."
                : "Tick “Install DLSS 5” and/or at least one tool, then press Install.");
            return;
        }

        SetBusy(true);
        InstallResult.IsVisible = false;
        var progress = new Progress<(string message, double percent)>(u =>
        {
            InstallProgress.Value = u.percent;
            RouteSummary.Text = u.message;
        });
        var results = new List<string>();
        var anyRan = false;

        try
        {
            // 1. Where is the game? (only asked when Adas really doesn't know)
            var (folderOk, deploymentPath) = await EnsureGameFolderAsync(owner, card, route is not null);
            if (!folderOk) { ShowResult("Cancelled — Adas needs to know where the game is installed."); return; }

            // 2. Which graphics API? (only asked when detection came up empty)
            var needsApi = route is not null
                ? _assessment?.Mode == Dlss5DeploymentMode.None
                : tools.Any(t => t.Tool is SetupTool.ReShade or SetupTool.Dxvk or SetupTool.DisplayCommander)
                  && _probe?.GraphicsApi == GraphicsApiType.Unknown;
            if (needsApi && !await AskGraphicsApiAsync(owner, card))
            {
                ShowResult("Cancelled — pick the game's graphics API to continue.");
                return;
            }

            // Re-resolve the route against the fresh assessment (folder/API answers can change the verdict).
            if (route is not null)
                route = _allRoutes.FirstOrDefault(r => r.Profile == route.Profile && r.DeepFriedChicken == route.DeepFriedChicken && r.BridgeSubstitute == route.BridgeSubstitute) ?? route;

            // 3. Deep Fried Chicken can't be bundled — fetch the user's copy now instead of refusing.
            if (route is { DeepFriedChicken: true }
                && AppServices.Services.GetService<DeepFriedChickenService>()?.IsImported != true)
            {
                if (!await ImportDeepFriedChickenAsync())
                {
                    ShowResult("Deep Fried Chicken isn't imported, so that route can't be installed. Import it under Advanced options, or pick another route.");
                    return;
                }
                route = _allRoutes.FirstOrDefault(r => r.DeepFriedChicken) ?? route;
            }

            // 4. One confirmation listing everything worth knowing — then no more questions.
            var context = BuildToolContext() with { Route = route?.Profile };
            var warnings = new List<string>();
            if (route is not null)
            {
                if (_readiness?.State is Dlss5ReadinessState.Warnings or Dlss5ReadinessState.NeedsGraphicsApi)
                    warnings.AddRange(_readiness.Problems.Where(p => !p.Contains("couldn't tell which graphics technology")));
                if (!route.Supported && !route.Installed)
                    warnings.Add($"DLSS 5 “{route.Label}”: {route.StatusText.TrimStart('⚠', ' ')}");
            }
            foreach (var tool in tools)
                warnings.AddRange(Dlss5ToolCompatibility.Notes(tool.Tool, context)
                    .Where(n => n.Level is ToolNoteLevel.Conflict or ToolNoteLevel.NoEffect)
                    .Select(n => $"{tool.Name}: {n.Text}"));
            if (warnings.Count > 0
                && !await DialogHost.ConfirmAsync(owner, "Install anyway?",
                    "Heads up before Adas installs your selection:\n\n" + string.Join("\n\n", warnings.Distinct().Select(w => "⚠ " + w))
                    + "\n\nThese are warnings, not blockers — you decide.",
                    "Install anyway", "Go back"))
            {
                ShowResult("Cancelled — nothing was changed.");
                return;
            }

            // 5. DLSS 5 route
            var routeInstalled = false;
            if (route is not null)
            {
                var outcome = await Dlss5Installer.InstallAsync(main, owner, card, route.Profile, progress,
                    deepFriedChicken: route.DeepFriedChicken,
                    bridgeSubstitute: route.BridgeSubstitute,
                    forceProfile: !route.Supported && !route.Installed,
                    deploymentPath: deploymentPath,
                    risksConfirmed: true);
                if (outcome is { Ran: false, Message: "Cancelled." })
                {
                    ShowResult("Cancelled — nothing was changed.");
                    return;
                }
                routeInstalled = outcome.Ran;
                anyRan |= outcome.Ran;
                results.Add((outcome.Ran ? "✓ DLSS 5 — " : "✕ DLSS 5 — ") + outcome.Message);
                if (outcome.Ran)
                    results.Add("Hotkeys & tips:\n• " + string.Join("\n• ", Dlss5ComponentService.GetPostInstallTips(
                        _assessment?.Mode ?? Dlss5DeploymentMode.None, route.Profile,
                        tools.Any(t => t.Tool == SetupTool.OptiScaler) || card.IsOsInstalled)));
            }

            // 6. Tools, in an order where each one's dependencies are already in place.
            if (tools.Count > 0)
            {
                if (!await GuardGameClosedAsync(card, m => results.Add("✕ Tools skipped — " + m)))
                    return;
                foreach (var tool in tools.OrderBy(t => t.Tool))
                {
                    if (tool.Tool == SetupTool.ReShade && routeInstalled)
                    {
                        results.Add("– ReShade — already installed as part of DLSS 5.");
                        tool.SetSelectedSilently(false);
                        continue;
                    }
                    RouteSummary.Text = $"Installing {tool.Name}…";
                    var (ok, message) = await InstallToolAsync(main, card, tool.Tool, progress);
                    anyRan |= ok;
                    results.Add((ok ? "✓ " : "✕ ") + tool.Name + " — " + message);
                    if (ok) tool.SetSelectedSilently(false);
                }
            }
        }
        catch (Exception ex)
        {
            results.Add("✕ Install stopped: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            if (results.Count > 0) ShowResult(string.Join("\n\n", results));
            if (anyRan)
            {
                card.NotifyAll();
                try { await main.RefreshAsync(); } catch { /* refresh best-effort */ }
                if (results.Count > 0) ShowResult(string.Join("\n\n", results));
            }
            await RefreshAssessmentAsync();
        }
    }

    private void ShowResult(string text)
    {
        InstallResult.Text = text;
        InstallResult.IsVisible = true;
    }

    /// <summary>
    /// Makes sure Adas knows the game folder: when the remembered folder is gone, the picked folder becomes the
    /// game's folder; when detection found several candidates (route installs only), the picked folder is used
    /// for this install. Returns false only when the user cancels the picker.
    /// </summary>
    private async Task<(bool ok, string? deploymentPath)> EnsureGameFolderAsync(Window owner, GameCardViewModel card, bool forRoute)
    {
        if (string.IsNullOrWhiteSpace(card.InstallPath) || !Directory.Exists(card.InstallPath))
        {
            var picked = await PickFolderAsync(owner, $"{card.GameName} isn't where Adas last saw it — select the game's folder", null);
            if (picked is null) return (false, null);
            Main!.SetFolderOverride(card.GameName, picked, Store);
            FolderText.Text = picked;
            card.NotifyAll();
            await RefreshAssessmentAsync();
        }

        if (forRoute && _readiness?.State == Dlss5ReadinessState.NeedsGameFolder)
        {
            var picked = await PickFolderAsync(owner, $"Select the folder that contains {card.GameName}'s .exe", card.InstallPath);
            return picked is null ? (false, null) : (true, picked);
        }
        return (true, null);
    }

    private static async Task<string?> PickFolderAsync(Window owner, string title, string? startIn)
    {
        if (owner.StorageProvider is not { } sp) return null;
        Avalonia.Platform.Storage.IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(startIn) && Directory.Exists(startIn))
        {
            try { start = await sp.TryGetFolderFromPathAsync(new Uri(startIn)); } catch { /* optional */ }
        }
        var folders = await sp.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        var path = folders.Count > 0 ? folders[0].Path.LocalPath : null;
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    /// <summary>Asks which graphics API the game uses (APIs found in its files listed first) and saves the answer.</summary>
    private async Task<bool> AskGraphicsApiAsync(Window owner, GameCardViewModel card)
    {
        var supported = _probe?.SupportedGraphicsApis.Where(a => a != GraphicsApiType.Unknown).ToArray() ?? Array.Empty<GraphicsApiType>();
        var options = supported.Concat(AllApis).Distinct().ToArray();
        var labels = options.Select(api => GraphicsApiDetector.GetLabel(api)
                                           + (supported.Contains(api) ? "   — found in the game's files" : "")).ToArray();
        var choice = await DialogHost.ChooseAsync(owner, "Which graphics API does this game use?",
            "Adas couldn't tell for sure. If you don't know, keep the first option — you can change it any time under Graphics.",
            labels, 0, "Use this");
        if (choice is null) return false;

        Main!.SetApiOverride(card.GameName, new List<string> { options[choice.Value].ToString() }, Store);
        card.NotifyAll();
        await RefreshAssessmentAsync();
        return true;
    }

    /// <summary>Installs one checklist tool through the same engine paths as the library, and reads back the result.</summary>
    private async Task<(bool ok, string message)> InstallToolAsync(MainViewModel main, GameCardViewModel card, SetupTool tool,
        IProgress<(string message, double percent)> progress)
    {
        if (string.IsNullOrWhiteSpace(card.InstallPath) || !Directory.Exists(card.InstallPath))
            return (false, "the game folder isn't set.");
        try
        {
            switch (tool)
            {
                case SetupTool.OptiScaler:
                {
                    var svc = AppServices.Services.GetService<IOptiScalerService>();
                    if (svc is null) return (false, "OptiScaler service unavailable.");
                    var record = await svc.InstallAsync(card, progress,
                        main.Settings.OsGpuType, main.Settings.OsDlssInputs, main.Settings.OsHotkey,
                        main.GetOsVariant(card.GameName, card.Source ?? ""));
                    if (record is null) return (false, "install did not complete — see the log.");
                    await TryPdUpscalerSwapAsync(main, card, progress);
                    var fgNote = "";
                    if (FsrFgCheck.IsChecked == true)
                    {
                        var fgProblem = ApplyFsrFrameGeneration(record.InstallPath, true);
                        fgNote = fgProblem is null ? " FSR 3.1 frame generation is on (2x) — turn the game's own frame generation off." : $" FSR 3.1 frame generation not enabled: {fgProblem}";
                    }
                    return (true, $"installed{(string.IsNullOrWhiteSpace(record.OsVariant) ? "" : $" ({record.OsVariant})")}. Press Insert in-game for its menu.{fgNote}");
                }
                case SetupTool.Dxvk:
                    await main.InstallDxvkAsync(card);
                    card.NotifyAll();
                    return card.IsDxvkInstalled
                        ? (true, "installed.")
                        : (false, string.IsNullOrWhiteSpace(card.DxvkActionMessage) ? "install was cancelled or did not complete." : card.DxvkActionMessage);
                case SetupTool.ReShade:
                    await main.InstallReShadeAsync(card);
                    card.NotifyAll();
                    return card.IsRsInstalled
                        ? (true, "installed.")
                        : (false, string.IsNullOrWhiteSpace(card.RsActionMessage) ? "install did not complete." : card.RsActionMessage);
                case SetupTool.DisplayCommander:
                    if (card.IsUlInstalled)
                        return (false, "ReLimiter is installed in this game; remove it first (the two frame limiters can't run together).");
                    await main.InstallDcAsync(card);
                    card.NotifyAll();
                    return card.IsDcInstalled
                        ? (true, "installed.")
                        : (false, string.IsNullOrWhiteSpace(card.DcActionMessage) ? "install did not complete." : card.DcActionMessage);
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        return (false, "unknown tool.");
    }

    /// <summary>PD-Upscaler REFramework swap for compatible RE Engine titles after OptiScaler (ports the WinUI flow).</summary>
    private static async Task TryPdUpscalerSwapAsync(MainViewModel main, GameCardViewModel card,
        IProgress<(string message, double percent)> progress)
    {
        if (main.Manifest?.PdUpscalerGames is not { } pd
            || !pd.TryGetValue(card.GameName, out var pdArtifact)
            || !File.Exists(Path.Combine(card.InstallPath!, "dinput8.dll")))
            return;
        try
        {
            var refSvc = AppServices.Services.GetService<IREFrameworkService>();
            if (refSvc is null) return;
            await refSvc.InstallPdUpscalerAsync(card.GameName, card.InstallPath!, pdArtifact, progress);
            card.RefInstalledVersion = "PD-Upscaler";
            card.NotifyAll();
        }
        catch
        {
            // Non-fatal — OptiScaler is already installed.
        }
    }

    private async Task RemoveToolAsync(SetupToolItem item)
    {
        var card = Card;
        var main = Main;
        if (card is null || main is null || _busy) return;
        if (!await GuardGameClosedAsync(card, ShowResult)) return;

        SetBusy(true);
        try
        {
            switch (item.Tool)
            {
                case SetupTool.OptiScaler: AppServices.Services.GetService<IOptiScalerService>()?.Uninstall(card); break;
                case SetupTool.Dxvk: main.UninstallDxvk(card); break;
                case SetupTool.ReShade: main.UninstallReShade(card); break;
                case SetupTool.DisplayCommander: main.UninstallDc(card); break;
            }
            card.NotifyAll();
            ShowResult($"{item.Name} removed.");
        }
        catch (Exception ex)
        {
            ShowResult($"Removing {item.Name} failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            UpdateSelectionState();
        }
    }

    private void OnRepair(object? sender, RoutedEventArgs e) =>
        _ = RunMaintenanceAsync((main, owner, card, progress) => Dlss5Installer.RepairAsync(main, owner, card, progress));

    private void OnRemove(object? sender, RoutedEventArgs e) =>
        _ = RunMaintenanceAsync((main, owner, card, progress) => Dlss5Installer.RemoveAsync(main, owner, card, progress));

    /// <summary>
    /// Shared runner for the Remove/Repair maintenance actions: drives the progress bar, then refreshes the
    /// library card and this pane so the badge/label update.
    /// </summary>
    private async Task RunMaintenanceAsync(
        Func<MainViewModel, Window, GameCardViewModel, IProgress<(string message, double percent)>, Task<Dlss5Installer.Outcome>> action)
    {
        var card = Card;
        if (Main is null || card is null || _busy) return;
        var owner = this.FindAncestorOfType<Window>();
        if (owner is null) return;

        SetBusy(true);
        InstallResult.IsVisible = false;

        var progress = new Progress<(string message, double percent)>(u =>
        {
            InstallProgress.Value = u.percent;
            RouteSummary.Text = u.message;
        });

        try
        {
            var outcome = await action(Main, owner, card, progress);
            ShowResult(outcome.Message);
            if (outcome.Ran)
            {
                try { await Main.RefreshAsync(); } catch { /* refresh best-effort */ }
                await RefreshAssessmentAsync();
            }
        }
        finally
        {
            SetBusy(false);
            UpdateSelectionState();
        }
    }

    /// <summary>
    /// Closes a running game before a component writes DLLs Windows keeps locked. Returns true when
    /// it is safe to proceed; on cancel/failure it reports through <paramref name="report"/> and returns false.
    /// </summary>
    private async Task<bool> GuardGameClosedAsync(GameCardViewModel card, Action<string> report)
    {
        var owner = this.FindAncestorOfType<Window>();
        if (owner is null) return true;
        var result = await GameCloseGuard.EnsureClosedAsync(owner, card.GameName, card.InstallPath);
        if (result.CanProceed) return true;
        if (result.Error is { } err) report(err);
        return false;
    }

    // ── Advanced per-game overrides ─────────────────────────────────────────
    // Seeds each combo from the persisted per-game override; handlers below write changes back
    // through the same MainViewModel getters/setters the WinUI overrides panel used.
    private void PopulateOverrides(GameCardViewModel card)
    {
        if (Main is null) return;
        _populatingOverrides = true;
        try
        {
            var store = card.Source ?? "";

            BitnessCombo.SelectedItem = Main.GetBitnessOverride(card.GameName, store) switch
            {
                "32" => "32-bit",
                "64" => "64-bit",
                _ => "Auto",
            };

            var api = Main.GetApiOverride(card.GameName, store);
            ApiCombo.SelectedItem = api is { Count: > 0 }
                ? api[0] switch
                {
                    "DirectX12" => "DirectX12", "DirectX11" => "DirectX11", "DirectX10" => "DirectX10",
                    "DirectX9" => "DirectX9", "DirectX8" => "DirectX8", "Vulkan" => "Vulkan",
                    "OpenGL" => "OpenGL", _ => "Auto",
                }
                : "Auto";

            ReShadeCombo.SelectedItem = Main.GetReShadeChannelOverride(card.GameName, store) switch
            {
                null => "Default",
                "Stable" => "Stable",
                "Nightly" => "Nightly",
                "Custom" => "Custom",
                _ => "Default", // legacy version string — not represented as a discrete option here
            };

            DxvkCombo.SelectedItem = Main.GetDxvkVariantOverride(card.GameName, store) ?? "Default";
            ShaderModeCombo.SelectedItem = Main.GetPerGameShaderMode(card.GameName, store);

            var folder = Main.GetFolderOverride(card.GameName, store);
            FolderText.Text = string.IsNullOrWhiteSpace(folder) ? "Using detected folder." : folder;

            UpdateDfcStatus();
        }
        finally { _populatingOverrides = false; }
    }

    private void OnBitnessChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populatingOverrides || Main is null || Card is not { } card) return;
        var value = (BitnessCombo.SelectedItem as string) switch { "32-bit" => "32", "64-bit" => "64", _ => (string?)null };
        Main.SetBitnessOverride(card.GameName, value, Store);

        // Compute the new effective bitness (explicit override, or re-resolve auto-detection).
        bool newIs32Bit;
        if (value == "32") newIs32Bit = true;
        else if (value == "64") newIs32Bit = false;
        else
        {
            var detected = AppServices.Services.GetService<IPeHeaderService>()?.DetectGameArchitecture(card.InstallPath ?? "")
                           ?? MachineType.Native;
            newIs32Bit = Main.ResolveIs32Bit(card.GameName, detected, card.Source ?? "");
        }

        // If bitness actually flips, uninstall every installed component BEFORE updating card.Is32Bit —
        // the uninstall paths resolve deployed DLL filenames from card.Is32Bit. Ports the WinUI cascade.
        if (card.Is32Bit != newIs32Bit && !card.RequiresVulkanInstall)
        {
            if (card.IsRsInstalled) Main.UninstallReShade(card);
            if (card.IsDcInstalled) Main.UninstallDc(card);
            if (card.InstalledRecord != null) Main.UninstallMod(card);
            if (card.IsUlInstalled) Main.UninstallUl(card);
            if (card.IsOsInstalled) AppServices.Services.GetService<IOptiScalerService>()?.Uninstall(card);
            if (card.IsDxvkInstalled) Main.UninstallDxvk(card);
            if (card.IsRefInstalled) Main.UninstallREFramework(card);
            if (card.IsLumaInstalled) Main.UninstallLuma(card);
        }

        card.Is32Bit = newIs32Bit;
        card.NotifyAll();
        _ = RefreshAssessmentAsync();
    }

    private void OnApiChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populatingOverrides || Main is null || Card is not { } card) return;
        var sel = ApiCombo.SelectedItem as string;
        var apis = sel is null or "Auto" ? null : new List<string> { sel };
        Main.SetApiOverride(card.GameName, apis, Store);
        card.NotifyAll();
        _ = RefreshAssessmentAsync();
    }

    private void OnReShadeChannelChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populatingOverrides || Main is null || Card is not { } card) return;
        var sel = ReShadeCombo.SelectedItem as string;
        Main.SetReShadeChannelOverride(card.GameName, sel == "Default" ? null : sel, Store);
    }

    private void OnDxvkVariantChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populatingOverrides || Main is null || Card is not { } card) return;
        var sel = DxvkCombo.SelectedItem as string;
        Main.SetDxvkVariantOverride(card.GameName, sel == "Default" ? null : sel, Store);
    }

    private void OnShaderModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populatingOverrides || Main is null || Card is not { } card) return;
        var mode = ShaderModeCombo.SelectedItem as string ?? "Global";
        Main.SetPerGameShaderMode(card.GameName, mode, Store);
        if (mode == "Select") _ = ChoosePacksAsync(card);
    }

    private void OnChoosePacks(object? sender, RoutedEventArgs e)
    {
        if (Card is { } card) _ = ChoosePacksAsync(card);
    }

    private async Task ChoosePacksAsync(GameCardViewModel card)
    {
        if (Main is null) return;
        var picker = Main.ShowPerGameShaderSelectionPicker;
        if (picker is null) return;

        var svc = Main.GameNameServiceInstance;
        var key = GameKey.From(card.GameName, Store).ToKey();
        var current = svc.PerGameShaderSelection.TryGetValue(key, out var existing) ? existing : null;

        var result = await picker(card.GameName, current);
        if (result is null) return; // cancelled

        svc.PerGameShaderSelection[key] = result;
        if (ShaderModeCombo.SelectedItem as string != "Select")
        {
            _populatingOverrides = true;
            ShaderModeCombo.SelectedItem = "Select";
            _populatingOverrides = false;
            Main.SetPerGameShaderMode(card.GameName, "Select", Store);
        }
        Main.SaveSettingsPublic();
        Main.DeployShadersForCard(card.GameName);
    }

    private async void OnChangeFolder(object? sender, RoutedEventArgs e)
    {
        if (Main is null || Card is not { } card) return;
        var owner = this.FindAncestorOfType<Window>();
        if (owner?.StorageProvider is not { } sp) return;

        var folders = await sp.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Select the game's install folder",
            AllowMultiple = false,
        });
        var path = folders.Count > 0 ? folders[0].Path.LocalPath : null;
        if (string.IsNullOrWhiteSpace(path)) return;

        Main.SetFolderOverride(card.GameName, path, Store);
        FolderText.Text = path;
        card.NotifyAll();
        await RefreshAssessmentAsync();
    }

    private async void OnResetFolder(object? sender, RoutedEventArgs e)
    {
        if (Main is null || Card is not { } card) return;
        Main.ResetFolderOverride(card);
        FolderText.Text = "Using detected folder.";
        card.NotifyAll();
        await RefreshAssessmentAsync();
    }

    /// <summary>Reflects whether a valid Deep Fried Chicken release is cached, and its version.</summary>
    private void UpdateDfcStatus()
    {
        var dfc = AppServices.Services.GetService<DeepFriedChickenService>();
        if (dfc?.IsImported == true)
        {
            var version = dfc.ImportedVersion;
            DfcStatusText.Text = string.IsNullOrWhiteSpace(version)
                ? "Imported — available as a route above."
                : $"Imported {version} — available as a route above.";
        }
        else
        {
            DfcStatusText.Text = "Not imported.";
        }
    }

    private async void OnImportDeepFriedChicken(object? sender, RoutedEventArgs e)
    {
        if (await ImportDeepFriedChickenAsync())
            await RefreshAssessmentAsync();
    }

    /// <summary>
    /// Imports the user-supplied Deep Fried Chicken release. Its licence forbids redistribution, so Adas
    /// never bundles it. First tries the author's release sitting in Downloads (or Downloads\DLSS5); if
    /// none is found, prompts for the .zip or the folder the user extracted the release into.
    /// Returns true when a valid release is imported.
    /// </summary>
    private async Task<bool> ImportDeepFriedChickenAsync()
    {
        var dfc = AppServices.Services.GetService<DeepFriedChickenService>();
        if (dfc is null) { DfcStatusText.Text = "Deep Fried Chicken service unavailable."; return false; }

        ImportDfcButton.IsEnabled = false;
        try
        {
            DfcStatusText.Text = "Looking in Downloads…";
            if (await dfc.EnsureImportedFromDefaultLocationsAsync() && dfc.IsImported)
            {
                UpdateDfcStatus();
                return true;
            }

            var owner = this.FindAncestorOfType<Window>();
            if (owner?.StorageProvider is not { } sp)
            {
                DfcStatusText.Text = "Could not open a file picker.";
                return false;
            }

            // Prefer a file picker for the official .zip; if the user cancels it, offer a folder picker for
            // an already-extracted release (the 1.7.x password-protected .7z workflow).
            var files = await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select the Deep Fried Chicken release (.zip)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Deep Fried Chicken release")
                    { Patterns = new[] { "*.zip" } },
                },
            });
            string? source = files.Count > 0 ? files[0].Path.LocalPath : null;

            if (string.IsNullOrWhiteSpace(source))
            {
                var folders = await sp.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "…or select the extracted Deep Fried Chicken folder",
                    AllowMultiple = false,
                });
                source = folders.Count > 0 ? folders[0].Path.LocalPath : null;
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                UpdateDfcStatus();
                return false;
            }

            DfcStatusText.Text = "Verifying and importing…";
            var error = await dfc.ImportAsync(source);
            if (error != null)
            {
                DfcStatusText.Text = "Import failed: " + error;
                return false;
            }

            UpdateDfcStatus();
            return dfc.IsImported;
        }
        catch (Exception ex)
        {
            DfcStatusText.Text = "Import failed: " + ex.Message;
            return false;
        }
        finally
        {
            ImportDfcButton.IsEnabled = true;
        }
    }

    private void OnDiagnose(object? sender, RoutedEventArgs e)
    {
        var card = Card;
        if (card is null) return;
        AioFixPanel.Children.Clear();
        AioFixPanel.IsVisible = false;
        var root = _installedRoot ?? card.InstallPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            DiagnoseResult.Text = "No install path resolved for this game yet.";
            return;
        }

        try
        {
            var record = Dlss5ComponentService.LoadRecord(root);
            if (record is null)
            {
                DiagnoseResult.Text = "No DLSS 5 install found in this folder. Install first, then verify.";
                return;
            }

            var report = Dlss5DiagnosticService.Diagnose(root, record.Mode, !card.Is32Bit);
            DiagnoseResult.Text = report.ToDisplayText();
            if (record.Profile == Dlss5InstallProfile.StandaloneAio)
                ShowAioFixes(root, record.Mode);
        }
        catch (Exception ex)
        {
            DiagnoseResult.Text = $"Diagnosis failed: {ex.Message}";
        }
    }

    /// <summary>One-click AIO troubleshooting switches; the ones the AIO log points at are listed first.</summary>
    private void ShowAioFixes(string root, Dlss5DeploymentMode mode)
    {
        var suggested = Dlss5AioTroubleshooter.Suggest(Dlss5AioTroubleshooter.ReadLogTail(), mode, out var repairNeeded);
        var header = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = (suggested.Count > 0
                    ? "AIO troubleshooting — the AIO log points at the ★ switches below. Each is one click and can be turned off from Tuning."
                    : "AIO troubleshooting — if AIO isn't showing, try one switch at a time, then relaunch the game.")
                   + (repairNeeded ? "\n⚠ The log reports a missing runtime file. Tick DLSS 5 and press Install selected to repair AIO." : ""),
        };
        AioFixPanel.Children.Add(header);
        foreach (var fix in suggested.Concat(Dlss5AioTroubleshooter.All(mode).Where(f => !suggested.Contains(f))))
        {
            var applied = Dlss5AioTroubleshooter.IsApplied(root, fix);
            var star = suggested.Contains(fix) ? "★ " : "";
            var button = new Button
            {
                Classes = { "subtle" },
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Content = applied ? $"✓ {fix.Title} (on)" : star + fix.Title,
                IsEnabled = !applied,
            };
            ToolTip.SetTip(button, fix.Explanation);
            button.Click += (_, _) =>
            {
                try
                {
                    Dlss5ComponentService.SaveAioUserSettings(root, new Dictionary<string, string> { [fix.Key] = fix.Value });
                    button.Content = $"✓ {fix.Title} (on)";
                    button.IsEnabled = false;
                    DiagnoseResult.Text += $"\n\n✓ Turned on “{fix.Title}”. Relaunch the game, then check again.";
                    RefreshTuning();
                }
                catch (Exception ex) { DiagnoseResult.Text += $"\n\n✕ Couldn't apply “{fix.Title}”: {ex.Message}"; }
            };
            AioFixPanel.Children.Add(button);
        }
        AioFixPanel.IsVisible = true;
    }

    // ── "Did it work?" results from other players (#2) ──────────────────────────────────────────

    private async Task RefreshCommunityResultsAsync(GameCardViewModel card)
    {
        var svc = AppServices.Services.GetService<CommunityResultsService>();
        if (svc is null) { CommunityHeader.IsVisible = false; return; }
        var ok = await svc.RefreshAsync();
        if (!ReferenceEquals(Card, card)) return;
        if (!ok)
        {
            CommunityHeader.Text = "Results from other players couldn't be loaded.";
            CommunityGpuOnlyCheck.IsVisible = false;
            return;
        }
        ApplyCommunityResults();
    }

    private void ApplyCommunityResults()
    {
        var card = Card;
        var svc = AppServices.Services.GetService<CommunityResultsService>();
        if (card is null || svc is null || _allRoutes.Count == 0) return;
        var gpu = CommunityResultsService.GpuFamily(Dlss5CompatibilityService.DetectedGpuName);
        var gpuOnly = CommunityGpuOnlyCheck.IsChecked == true && gpu.Length > 0;
        var summaries = svc.Summarize(card.GameName, gpuOnly ? gpu : null);
        var anyForGame = gpuOnly ? svc.Summarize(card.GameName).Count > 0 : summaries.Count > 0;
        CommunityGpuOnlyCheck.IsVisible = anyForGame && gpu.Length > 0;
        CommunityGpuOnlyCheck.Content = $"Only {gpu}";

        var best = summaries.FirstOrDefault(s => s.Worked > s.Failed);
        var updated = _allRoutes.Select(route =>
        {
            var s = summaries.FirstOrDefault(x => x.Route == CommunityResultsService.RouteKey(route));
            var text = s is null ? null
                : $"👥 Other players{(gpuOnly ? $" on {gpu}" : "")}: worked {s.Worked} of {s.Total}"
                  + (ReferenceEquals(s, best) ? " — most reported working" : "");
            return route with { CommunityText = text };
        }).ToList();

        var selected = RoutesList.SelectedItem as RouteOption;
        _allRoutes = updated;
        _populatingRoutes = true;
        try
        {
            RoutesList.ItemsSource = updated;
            RoutesList.SelectedItem = selected is null ? null
                : updated.FirstOrDefault(r => r.Profile == selected.Profile && r.DeepFriedChicken == selected.DeepFriedChicken
                                              && r.BridgeSubstitute == selected.BridgeSubstitute);
        }
        finally { _populatingRoutes = false; }

        var total = summaries.Sum(s => s.Total);
        CommunityHeader.Text = total == 0
            ? gpuOnly ? $"No results from other {gpu} players for this game yet." : "No results from other players for this game yet."
            : $"👥 {total} report{(total == 1 ? "" : "s")} from other players{(gpuOnly ? $" on {gpu}" : "")}"
              + (best is null ? "." : $" — most reported working: {updated.FirstOrDefault(r => CommunityResultsService.RouteKey(r) == best.Route)?.Label ?? best.Route}.");
        UpdateSelectionState();
    }

    private async Task ShareResultAsync(bool worked)
    {
        var card = Card;
        var owner = this.GetVisualRoot() as Window;
        if (card is null || owner is null) return;
        var route = _allRoutes.FirstOrDefault(r => r.Installed);
        if (route is null || _installedRecord is null)
        {
            DiagnoseResult.Text = "Install a DLSS 5 route and play the game first, then share how it went.";
            return;
        }
        var version = AppServices.Services.GetService<IUpdateService>()?.CurrentVersion;
        var report = new CommunityReport(
            card.GameName,
            CommunityResultsService.RouteKey(route),
            _probe?.GraphicsApi is { } api && api != GraphicsApiType.Unknown ? GraphicsApiDetector.GetLabel(api) : _installedRecord.Mode.ToString(),
            _probe?.Is64Bit ?? !card.Is32Bit,
            Dlss5CompatibilityService.DetectedGpuName,
            Dlss5CompatibilityService.DetectedDriverVersion,
            version is null ? "" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}",
            worked);
        if (!await DialogHost.ConfirmAsync(owner, "Share this result?",
                "Adas will open a GitHub issue in your browser with only this:\n\n"
                + CommunityResultsService.DescribeReport(report)
                + "\n\nYou need a GitHub account. Check it and press Create to share it; close the page to cancel. The issue is public.",
                "Open GitHub", "Cancel"))
            return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = CommunityResultsService.BuildReportUrl(report), UseShellExecute = true });
            DiagnoseResult.Text = "Opened GitHub. Press Create there to share your result. Counts in Adas update within a few hours.";
        }
        catch (Exception ex) { DiagnoseResult.Text = $"Couldn't open the browser: {ex.Message}"; }
    }

    // ── Ray reconstruction DLL update (#8) ───────────────────────────────────────────────────────

    private string? _rrPath;
    private string? _rrNewest;

    private async Task RefreshRrCardAsync(GameCardViewModel card, bool is64Bit)
    {
        RrCard.IsVisible = false;
        _rrPath = null;
        var svc = AppServices.Services.GetService<IDlssStreamlineService>();
        if (svc is null || !is64Bit || string.IsNullOrWhiteSpace(card.InstallPath) || !Directory.Exists(card.InstallPath)) return;
        try
        {
            var detection = await Task.Run(() => svc.Detect(card.InstallPath));
            if (!ReferenceEquals(Card, card) || detection.DlssdPath is not { } path || !File.Exists(path)) return;
            if (svc.DlssdVersions.Count == 0)
                try { await svc.FetchManifestAsync(); } catch { }
            if (!ReferenceEquals(Card, card)) return;
            var current = svc.GetFileVersion(path);
            var newest = svc.DlssdVersions.FirstOrDefault();
            var hasBackup = svc.HasBackup(path);
            var canUpdate = IsNewerVersion(newest, current);
            _rrPath = path;
            _rrNewest = newest;
            RrText.Text = $"This game ships ray reconstruction (nvngx_dlssd.dll {current ?? "unknown version"})."
                          + (canUpdate ? $" {newest} is available." : newest is null ? " Adas couldn't load the version list." : " It's already the newest.")
                          + (hasBackup ? " The original is backed up." : "");
            RrUpdateButton.IsVisible = canUpdate;
            RrUpdateButton.Content = $"Update to {newest}";
            RrRestoreButton.IsVisible = hasBackup;
            RrCard.IsVisible = true;
        }
        catch (Exception ex) { CrashReporter.Log($"[GameSetupView] RR detection failed: {ex.Message}"); }
    }

    internal static bool IsNewerVersion(string? candidate, string? current)
    {
        static Version? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var digits = new string(text.TrimStart('v', 'V').TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
            if (!digits.Contains('.')) digits += ".0";
            return Version.TryParse(digits, out var v) ? v : null;
        }
        var a = Parse(candidate);
        if (a is null) return false;
        var b = Parse(current);
        return b is null || Normalize(a) > Normalize(b);

        static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }

    private async void OnRrUpdate(object? sender, RoutedEventArgs e)
    {
        var card = Card;
        var owner = this.GetVisualRoot() as Window;
        var svc = AppServices.Services.GetService<IDlssStreamlineService>();
        if (card is null || owner is null || svc is null || _rrPath is not { } path || _rrNewest is not { } newest) return;
        if (_busy) return; // one mutating operation at a time — same guard as install/repair/remove
        if (!await DialogHost.ConfirmAsync(owner, "Update ray reconstruction?",
                "Adas backs up the game's nvngx_dlssd.dll and replaces it with " + newest + ".\n\n"
                + "⚠ Launchers that verify files (Steam, EA app, Battle.net) may put the original back after an update or verify.\n\n"
                + "⚠ Don't do this in online games with anti-cheat; a changed DLL can be flagged.",
                "Update", "Cancel"))
            return;
        var guard = await GameCloseGuard.EnsureClosedAsync(owner, card.GameName, card.InstallPath);
        if (!guard.CanProceed) { RrText.Text = guard.Error ?? "Cancelled."; return; }
        SetBusy(true);
        try
        {
            var before = svc.GetFileVersion(path);
            await svc.SwapDlssdAsync(path, newest);
            var after = svc.GetFileVersion(path);
            await RefreshRrCardAsync(card, true);
            RrText.Text = (after != before && svc.HasBackup(path)
                ? $"✓ Ray reconstruction updated to {after}. "
                : "✕ The update didn't complete; the game's file is unchanged. See the log. ") + RrText.Text;
        }
        finally { SetBusy(false); }
    }

    private async void OnRrRestore(object? sender, RoutedEventArgs e)
    {
        var card = Card;
        var owner = this.GetVisualRoot() as Window;
        var svc = AppServices.Services.GetService<IDlssStreamlineService>();
        if (card is null || owner is null || svc is null || _rrPath is not { } path) return;
        if (_busy) return; // one mutating operation at a time — same guard as install/repair/remove
        var guard = await GameCloseGuard.EnsureClosedAsync(owner, card.GameName, card.InstallPath);
        if (!guard.CanProceed) { RrText.Text = guard.Error ?? "Cancelled."; return; }
        SetBusy(true);
        try
        {
            svc.Restore(path);
            await RefreshRrCardAsync(card, true);
            RrText.Text = "✓ Original ray reconstruction restored. " + RrText.Text;
        }
        catch (Exception ex) { RrText.Text = $"✕ Restore failed: {ex.Message}"; }
        finally { SetBusy(false); }
    }

    // ── Per-game DLSS 5 choices: Feeder build (#4), motion vectors (#9), FSR FG (#5) ──────────────

    private string? _installedRoot;
    private Dlss5InstallRecord? _installedRecord;
    private bool _populatingPreferences;

    private void PopulateDlss5Preferences(GameCardViewModel card)
    {
        _populatingPreferences = true;
        try
        {
            var pref = Dlss5GamePreferences.Get(card.GameName, card.Source);
            FeederChannelCombo.SelectedIndex = (int)pref.FeederChannel;
            FeederTagBox.Text = pref.FeederReleaseTag ?? "";
            FeederTagBox.IsVisible = pref.FeederChannel == Dlss5FeederChannel.ExactRelease;
            MotionProviderCombo.SelectedIndex = pref.MotionProvider switch
            {
                Dlss5MotionProvider.LumeniteKernel => 1,
                Dlss5MotionProvider.VortMotion => 2,
                _ => 0,
            };
            FsrFgCheck.IsChecked = pref.OptiScalerFsrFrameGeneration;
            UpdateFeederChannelNote();
            UpdateFsrFgNote();
        }
        finally { _populatingPreferences = false; }
    }

    private void OnFeederChannelChanged()
    {
        var card = Card;
        if (card is null || _populatingPreferences) return;
        var channel = (Dlss5FeederChannel)Math.Max(0, FeederChannelCombo.SelectedIndex);
        var tag = FeederTagBox.Text?.Trim();
        FeederTagBox.IsVisible = channel == Dlss5FeederChannel.ExactRelease;
        Dlss5GamePreferences.Update(card.GameName, card.Source, p =>
        {
            p.FeederChannel = channel;
            p.FeederReleaseTag = string.IsNullOrWhiteSpace(tag) ? null : tag;
        });
        UpdateFeederChannelNote();
    }

    private void UpdateFeederChannelNote()
    {
        var channel = (Dlss5FeederChannel)Math.Max(0, FeederChannelCombo.SelectedIndex);
        var tag = FeederTagBox.Text?.Trim();
        FeederChannelNote.Text = channel switch
        {
            Dlss5FeederChannel.NewestPrerelease => Dlss5ComponentService.DescribeFeederPairing(Dlss5ComponentService.NewestPrereleaseTag),
            Dlss5FeederChannel.ExactRelease when Dlss5GamePreferences.IsValidReleaseTag(tag) => Dlss5ComponentService.DescribeFeederPairing(tag),
            Dlss5FeederChannel.ExactRelease => "Type the exact tag from github.com/jlrouzies-fr/DLSS5-Feeder/releases. Until then the packaged build is used.",
            _ => Dlss5ComponentService.DescribeFeederPairing(null),
        } + " If a download fails, Adas installs the packaged build and says so. Takes effect on the next Install.";
    }

    private void OnMotionProviderChanged()
    {
        var card = Card;
        if (card is null || _populatingPreferences) return;
        Dlss5MotionProvider? provider = MotionProviderCombo.SelectedIndex switch
        {
            1 => Dlss5MotionProvider.LumeniteKernel,
            2 => Dlss5MotionProvider.VortMotion,
            _ => null,
        };
        Dlss5GamePreferences.Update(card.GameName, card.Source, p => p.MotionProvider = provider);
    }

    private void OnFsrFgChanged(object? sender, RoutedEventArgs e)
    {
        var card = Card;
        if (card is null || _populatingPreferences) return;
        var enable = FsrFgCheck.IsChecked == true;
        Dlss5GamePreferences.Update(card.GameName, card.Source, p => p.OptiScalerFsrFrameGeneration = enable);
        UpdateFsrFgNote();
        if (!card.IsOsInstalled || string.IsNullOrWhiteSpace(card.InstallPath)) return;

        // OptiScaler is already in the game: apply straight away (it reads the ini at launch).
        var folder = new[] { card.InstallPath, ModInstallService.GetAddonDeployPath(card.InstallPath) }
            .FirstOrDefault(dir => File.Exists(Path.Combine(dir, "OptiScaler.ini")));
        var problem = folder is null
            ? "OptiScaler.ini wasn't found in the game folder."
            : ApplyFsrFrameGeneration(folder, enable);
        ShowResult(problem is null
            ? enable
                ? "FSR 3.1 frame generation is on (2x). Turn the game's own frame generation off; it applies the next time the game starts."
                : "FSR 3.1 frame generation is off."
            : "FSR 3.1 frame generation: " + problem);
    }

    private void UpdateFsrFgNote()
    {
        var api = _probe?.GraphicsApi ?? Card?.GraphicsApi ?? GraphicsApiType.Unknown;
        var note = FsrFgCheck.IsChecked == true && api is not (GraphicsApiType.DirectX12 or GraphicsApiType.Unknown)
            ? $"⚠ This game looks like {GraphicsApiDetector.GetLabel(api)}. OptiScaler's FSR 3.1 frame generation only works on DirectX 12, so it will likely have no effect."
            : null;
        FsrFgNote.Text = note ?? "";
        FsrFgNote.IsVisible = note is not null;
    }

    private static string? ApplyFsrFrameGeneration(string folder, bool enable)
    {
        try { return OptiScalerService.ApplyFsr31FrameGeneration(folder, enable); }
        catch (Exception ex) { return ex.Message; }
    }

    // ── #13 What will happen? ──────────────────────────────────────────────

    private async void OnPreview(object? sender, RoutedEventArgs e)
    {
        var card = Card;
        var owner = this.FindAncestorOfType<Window>();
        if (card is null || owner is null) return;
        var route = SelectedRoute;
        var assessment = _assessment;
        var tools = _tools.Where(t => t.IsSelected).Select(t => t.Name).ToList();
        if (route is null || assessment is null || assessment.Mode == Dlss5DeploymentMode.None
            || string.IsNullOrWhiteSpace(assessment.DeploymentPath))
        {
            await DialogHost.ConfirmAsync(owner, "What will happen?",
                route is null
                    ? "Tick “Install DLSS 5” and pick a route to preview its files."
                    : "Adas needs the game folder and graphics API before it can list the files. Install asks for them.",
                "OK", "Close");
            return;
        }

        string text;
        try
        {
            var (_, provider) = Dlss5GamePreferences.ResolveInstallChoices(
                Dlss5GamePreferences.Get(card.GameName, card.Source), assessment.Mode);
            var root = assessment.DeploymentPath!;
            text = await Task.Run(() => Dlss5ComponentService.FormatPreview(root,
                Dlss5ComponentService.PreviewInstall(root, assessment.Mode, assessment.Is64Bit, route.Profile, provider, route.DeepFriedChicken)));
            if (tools.Count > 0)
                text = $"Also installs: {string.Join(", ", tools)} (each keeps its own backup and has a Remove button).\n\n" + text;
            text = $"DLSS 5 “{route.Label}” in {root}\n\n" + text;
        }
        catch (Exception ex)
        {
            text = $"Couldn't build the preview: {ex.Message}";
        }
        await DialogHost.ConfirmAsync(owner, "What will happen?", text, "OK", "Close");
    }

    // ── #15 Hotkeys and tips ───────────────────────────────────────────────

    private void RefreshTips(GameCardViewModel card)
    {
        if (_installedRecord is null && !card.IsOsInstalled)
        {
            TipsCard.IsVisible = false;
            return;
        }
        var tips = _installedRecord is { } record
            ? Dlss5ComponentService.GetPostInstallTips(record.Mode, record.Profile, card.IsOsInstalled)
            : new[] { "Insert — open the OptiScaler menu." };
        TipsText.Text = "• " + string.Join("\n• ", tips);
        TipsCard.IsVisible = true;
    }

    // ── #3 Tuning ──────────────────────────────────────────────────────────

    private Dlss5TuningState? _tuning;
    private bool _populatingTuning;

    private void InitTuning()
    {
        WorkAreaSlider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) UpdateTuningLabels(); };
        SharpnessSlider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) UpdateTuningLabels(); };
        foreach (var slider in new[] { AioIntensitySlider, AioToneSlider, AioStructureSlider })
            slider.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) UpdateTuningLabels(); };
        PresetQualityButton.Click += (_, _) => ApplyPreset(Dlss5TuningPreset.Quality);
        PresetBalancedButton.Click += (_, _) => ApplyPreset(Dlss5TuningPreset.Balanced);
        PresetPerformanceButton.Click += (_, _) => ApplyPreset(Dlss5TuningPreset.Performance);
        PresetCustomButton.Click += (_, _) => ApplyCustomProfile();
        SaveCustomButton.Click += (_, _) => SaveCustomProfile();
        AimFpsButton.Click += (_, _) => SuggestForTarget();
        ApplyTuningButton.Click += (_, _) => ApplyTuning(null);
    }

    private void RefreshTuning()
    {
        var root = _installedRoot;
        Dlss5TuningState? state = null;
        if (root is not null)
        {
            try { state = Dlss5ComponentService.ReadTuning(root); }
            catch { state = null; }
        }
        _tuning = state;
        TuningCard.IsVisible = state is { HasFeeder: true } or { HasAio: true };
        if (state is null || !TuningCard.IsVisible) return;

        _populatingTuning = true;
        try
        {
            FeederTuningPanel.IsVisible = state.HasFeeder;
            AioTuningPanel.IsVisible = state.HasAio;
            PresetButtons.IsVisible = state.HasFeeder;
            WorkAreaSlider.Value = state.WorkResolution;
            FsrExpandCheck.IsChecked = state.WorkUpscale == 1;
            SharpnessSlider.Value = state.WorkSharpness;
            if (state.HasAio)
            {
                double D(string key) => double.TryParse(state.Aio.GetValueOrDefault(key), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 1;
                AioNeuralCheck.IsChecked = state.Aio.GetValueOrDefault("NeuralRendering") != "0";
                AioFrameGenCheck.IsChecked = state.Aio.GetValueOrDefault("FrameGeneration") == "1";
                AioFrameGenCheck.IsEnabled = state.AioFrameGenerationAvailable || AioFrameGenCheck.IsChecked == true;
                AioIntensitySlider.Value = D("Intensity");
                AioToneSlider.Value = D("LocalTone");
                AioStructureSlider.Value = D("LocalStructure");
            }
            var card = Card;
            var preset = card is null ? null : Dlss5GamePreferences.Get(card.GameName, card.Source).TuningPreset;
            TuningIntro.Text = (state.HasFeeder
                    ? state.WorkResolutionApplies
                        ? "Work area is the Feeder's cost knob: DLSS 5 runs on a smaller copy of the frame that is then expanded back. It is not DLSS upscaling — lower looks softer."
                        : "The work-area slider only has an effect on the Feeder's DirectX 11 transport; this game's route keeps 100%. Sharpness still applies."
                    : "Standalone AIO settings, written to ReShade.ini.")
                + (preset is null ? "" : $" Last applied: {preset}.")
                + " Changes apply the next time the game starts.";
            PresetCustomButton.IsEnabled = card is not null
                && Dlss5GamePreferences.Get(card.GameName, card.Source).CustomTuning is { Count: > 0 };
            TuningStatus.Text = "";
        }
        finally { _populatingTuning = false; }
        UpdateTuningLabels();
    }

    private void UpdateTuningLabels()
    {
        var percent = (int)Math.Round(WorkAreaSlider.Value);
        var cost = Dlss5ComponentService.RelativeWorkCost(percent);
        WorkAreaLabel.Text = $"Work area: {percent}%";
        WorkAreaNote.Text = percent >= 100
            ? "Full quality — the neural pass runs on every pixel."
            : $"About {cost:P0} of the full neural cost (≈{1 / cost:0.#}× cheaper); softer image.";
        SharpnessValue.Text = SharpnessSlider.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        AioIntensityValue.Text = AioIntensitySlider.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        AioToneValue.Text = AioToneSlider.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        AioStructureValue.Text = AioStructureSlider.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    private Dictionary<string, string> CollectFeederTuning() => new()
    {
        ["work_resolution"] = ((int)Math.Round(WorkAreaSlider.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["work_upscale"] = FsrExpandCheck.IsChecked == true ? "1" : "0",
        ["work_sharpness"] = SharpnessSlider.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
    };

    private Dictionary<string, string> CollectAioTuning()
    {
        string F(double v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var values = new Dictionary<string, string>
        {
            ["NeuralRendering"] = AioNeuralCheck.IsChecked == true ? "1" : "0",
            ["Intensity"] = F(AioIntensitySlider.Value),
            ["LocalTone"] = F(AioToneSlider.Value),
            ["LocalStructure"] = F(AioStructureSlider.Value),
        };
        if (_tuning?.AioFrameGenerationAvailable == true || AioFrameGenCheck.IsChecked != true)
            values["FrameGeneration"] = AioFrameGenCheck.IsChecked == true ? "1" : "0";
        return values;
    }

    private void ApplyPreset(Dlss5TuningPreset preset)
    {
        WorkAreaSlider.Value = Dlss5ComponentService.WorkResolutionFor(preset);
        FsrExpandCheck.IsChecked = preset != Dlss5TuningPreset.Quality;
        ApplyTuning(preset.ToString());
    }

    private void ApplyCustomProfile()
    {
        var card = Card;
        if (card is null) return;
        var custom = Dlss5GamePreferences.Get(card.GameName, card.Source).CustomTuning;
        if (custom is null || custom.Count == 0) { TuningStatus.Text = "No saved profile yet — set the sliders and press Save as my profile."; return; }
        double Get(string key, double fallback) => custom.TryGetValue(key, out var text)
            && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        WorkAreaSlider.Value = Get("work_resolution", WorkAreaSlider.Value);
        FsrExpandCheck.IsChecked = Get("work_upscale", FsrExpandCheck.IsChecked == true ? 1 : 0) >= 1;
        SharpnessSlider.Value = Get("work_sharpness", SharpnessSlider.Value);
        AioIntensitySlider.Value = Get("Intensity", AioIntensitySlider.Value);
        AioToneSlider.Value = Get("LocalTone", AioToneSlider.Value);
        AioStructureSlider.Value = Get("LocalStructure", AioStructureSlider.Value);
        if (custom.TryGetValue("NeuralRendering", out var nr)) AioNeuralCheck.IsChecked = nr != "0";
        if (custom.TryGetValue("FrameGeneration", out var fg)) AioFrameGenCheck.IsChecked = fg == "1";
        ApplyTuning("My profile");
    }

    private void SaveCustomProfile()
    {
        var card = Card;
        if (card is null || _tuning is null) return;
        var values = new Dictionary<string, string>();
        if (_tuning.HasFeeder) foreach (var pair in CollectFeederTuning()) values[pair.Key] = pair.Value;
        if (_tuning.HasAio) foreach (var pair in CollectAioTuning()) values[pair.Key] = pair.Value;
        Dlss5GamePreferences.Update(card.GameName, card.Source, p => p.CustomTuning = values);
        PresetCustomButton.IsEnabled = true;
        TuningStatus.Text = "Saved as this game's profile.";
    }

    private void SuggestForTarget()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        if (!double.TryParse(TargetFpsBox.Text, System.Globalization.NumberStyles.Float, culture, out var target)
            || !double.TryParse(CurrentFpsBox.Text, System.Globalization.NumberStyles.Float, culture, out var current)
            || target <= 0 || current <= 0)
        {
            TuningStatus.Text = "Enter the fps you want and the fps you get now (with DLSS 5 on).";
            return;
        }
        var suggested = Dlss5ComponentService.EstimateWorkResolutionForTarget(current, target, (int)Math.Round(WorkAreaSlider.Value));
        WorkAreaSlider.Value = suggested;
        if (suggested < 100) FsrExpandCheck.IsChecked = true;
        TuningStatus.Text = suggested == Dlss5ComponentService.MinWorkResolution && current * 4 < target
            ? "Even 50% (about a quarter of the cost) probably won't reach that — press Apply to use 50%, and consider a lighter route."
            : $"Suggested work area: {suggested}%. It's an estimate — press Apply, then check in game.";
    }

    private void ApplyTuning(string? presetName)
    {
        var card = Card;
        var root = _installedRoot;
        if (card is null || root is null || _tuning is null || _populatingTuning) return;
        if (_busy) // an install/remove/repair/RR op is writing to this same install root — don't race it
        {
            TuningStatus.Text = "Finish the current operation before changing tuning.";
            return;
        }
        try
        {
            if (_tuning.HasFeeder) Dlss5ComponentService.SaveFeederTuning(root, CollectFeederTuning());
            if (_tuning.HasAio) Dlss5ComponentService.SaveAioUserSettings(root, CollectAioTuning());
            Dlss5GamePreferences.Update(card.GameName, card.Source, p => p.TuningPreset = presetName ?? "Custom");
            TuningStatus.Text = (presetName is null ? "Applied." : $"{presetName} applied.") + " Restart the game if it's running.";
        }
        catch (Exception ex)
        {
            TuningStatus.Text = "Couldn't apply: " + ex.Message;
        }
    }

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        var path = Card?.InstallPath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch { /* best effort */ }
    }
}
