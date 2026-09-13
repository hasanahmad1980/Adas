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
    private (Dlss5InstallProfile Profile, bool DeepFriedChicken)? _userRoute;

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
        var main = Main;

        await Task.Run(() =>
        {
            try
            {
                var compat = AppServices.Services.GetService<Dlss5CompatibilityService>();
                if (compat is null || main is null) { summary = "Compatibility service unavailable."; return; }

                probe = Dlss5Installer.ProbeFor(main, compat, card);
                assessment = Dlss5CompatibilityService.Assess(probe, singlePlayerConfirmed: true);
                hasUpscaler = Dlss5ToolCompatibility.HasUpscalerRuntime(assessment.DeploymentPath ?? card.InstallPath, probe.HasNativeDlss);

                // Seed from an existing install of the same mode, else auto-pick from renderer/arch.
                var installed = assessment.DeploymentPath is { } dp ? Dlss5ComponentService.LoadRecord(dp) : null;
                var seed = installed?.Mode == assessment.Mode ? installed.Profile : Dlss5InstallProfile.MaximumQuality;
                var pick = Dlss5RouteCatalog.Recommend(assessment, seed);

                var dfc = AppServices.Services.GetService<DeepFriedChickenService>();
                routes = Dlss5RouteCatalog.Build(assessment, pick, installed?.Profile,
                    deepFriedChickenAvailable: dfc?.IsImported == true,
                    installedDeepFriedChicken: installed?.DeepFriedChicken == true);
                preferred = routes.FirstOrDefault(r => r.Installed)
                            ?? routes.FirstOrDefault(r => r.Profile == pick && r.Supported && !r.DeepFriedChicken)
                            ?? routes.FirstOrDefault(r => r.Recommended)
                            ?? routes.FirstOrDefault();

                // DLSS 5 has its own on-disk record and is NOT part of the RenoDX-only `Status`; drive the
                // badge from the record so a successful install flips it off "Available".
                if (installed is not null)
                {
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

                readiness = Dlss5ReadinessText.Describe(assessment, card.InstallPath);
                summary = installed is not null && readiness.State == Dlss5ReadinessState.Ready
                    ? "DLSS 5 is installed on this game. Launch the game to use it — or tick a different route or tools below and press Install selected."
                    : readiness.Headline;
            }
            catch (Exception ex) { summary = $"Couldn't check this game: {ex.Message}. You can still pick options and install."; }
        });

        void Apply()
        {
            if (!ReferenceEquals(Card, card)) return; // selection changed while probing
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
                                              ? routes.FirstOrDefault(r => r.Profile == chosen.Profile && r.DeepFriedChicken == chosen.DeepFriedChicken)
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
            UpdateSelectionState();
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else await Dispatcher.UIThread.InvokeAsync(Apply);
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
            _userRoute = (picked.Profile, picked.DeepFriedChicken);
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
                route = _allRoutes.FirstOrDefault(r => r.Profile == route.Profile && r.DeepFriedChicken == route.DeepFriedChicken) ?? route;

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
                    return (true, $"installed{(string.IsNullOrWhiteSpace(record.OsVariant) ? "" : $" ({record.OsVariant})")}. Press Insert in-game for its menu.");
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
        if (string.IsNullOrWhiteSpace(card.InstallPath) || !Directory.Exists(card.InstallPath))
        {
            DiagnoseResult.Text = "No install path resolved for this game yet.";
            return;
        }

        try
        {
            var record = Dlss5ComponentService.LoadRecord(card.InstallPath);
            if (record is null)
            {
                DiagnoseResult.Text = "No DLSS 5 install found in this folder. Install first, then verify.";
                return;
            }

            var report = Dlss5DiagnosticService.Diagnose(card.InstallPath, record.Mode, !card.Is32Bit);
            DiagnoseResult.Text = report.ToDisplayText();
        }
        catch (Exception ex)
        {
            DiagnoseResult.Text = $"Diagnosis failed: {ex.Message}";
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
