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
/// </summary>
public partial class GameSetupView : UserControl
{
    /// <summary>Set by the host window so buttons can invoke shared install commands.</summary>
    public MainViewModel? Main { get; set; }

    public GameSetupView()
    {
        InitializeComponent();

        InstallButton.Click += OnInstall;
        DiagnoseButton.Click += OnDiagnose;
        OptiScalerButton.Click += OnOptiScaler;
        DisplayCommanderButton.Click += OnDisplayCommander;
        DxvkButton.Click += OnDxvk;
        ReShadeButton.Click += OnReShade;
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
        ResetFolderButton.Click += OnResetFolder;

        DataContextChanged += (_, _) => _ = RefreshAssessmentAsync();
    }

    /// <summary>True while combos are being seeded from the card, so SelectionChanged handlers
    /// don't write the value straight back and trigger spurious re-assessments.</summary>
    private bool _populatingOverrides;

    private string Store => Card?.Source ?? "";

    private GameCardViewModel? Card => DataContext as GameCardViewModel;

    /// <summary>Re-runs the route assessment for the current card. Used by the shell's
    /// RequestCardRebuild / RequestOverridesPanelRebuild seams after engine-side state changes.</summary>
    public Task RefreshAsync() => RefreshAssessmentAsync();

    /// <summary>
    /// Probes/assesses the selected game off the UI thread and populates the route list — every route
    /// shown, recommended preselected, incompatible flagged with a reason. Avalonia rebuild of the WinUI
    /// detail-panel profile selector.
    /// </summary>
    private async Task RefreshAssessmentAsync()
    {
        var card = Card;
        if (card is null) return;

        RouteSummary.Text = "Analysing…";
        RoutesList.ItemsSource = null;
        InstallResult.IsVisible = false;

        string summary = "";
        IReadOnlyList<RouteOption> routes = Array.Empty<RouteOption>();
        RouteOption? recommended = null;

        await Task.Run(() =>
        {
            try
            {
                var compat = AppServices.Services.GetService<Dlss5CompatibilityService>();
                if (compat is null) { summary = "Compatibility service unavailable."; return; }

                var assessment = Dlss5CompatibilityService.Assess(compat.Probe(card), singlePlayerConfirmed: true);

                // Seed from an existing install of the same mode, else auto-pick from renderer/arch.
                var installed = assessment.DeploymentPath is { } dp ? Dlss5ComponentService.LoadRecord(dp) : null;
                var seed = installed?.Mode == assessment.Mode ? installed.Profile : Dlss5InstallProfile.MaximumQuality;
                var pick = Dlss5RouteCatalog.Recommend(assessment, seed);

                routes = Dlss5RouteCatalog.Build(assessment, pick);
                recommended = routes.FirstOrDefault(r => r.Profile == pick && r.Supported)
                              ?? routes.FirstOrDefault(r => r.Recommended);

                summary = assessment.CanInstall
                    ? $"Detected: {assessment.ModeLabel} ({(assessment.Is64Bit ? "64-bit" : "32-bit")}). Recommended route is preselected."
                    : "Not available for this game: "
                      + string.Join("; ", assessment.BlockingReasons.Concat(assessment.MissingRequirements)
                          .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
            }
            catch (Exception ex) { summary = $"Assessment failed: {ex.Message}"; }
        });

        void Apply()
        {
            if (!ReferenceEquals(Card, card)) return; // selection changed while probing
            RouteSummary.Text = summary;
            RoutesList.ItemsSource = routes;
            RoutesList.SelectedItem = recommended;
            PopulateOverrides(card);
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else await Dispatcher.UIThread.InvokeAsync(Apply);
    }

    private async void OnInstall(object? sender, RoutedEventArgs e)
    {
        var card = Card;
        if (Main is null || card is null) return;
        if (RoutesList.SelectedItem is not RouteOption route)
        {
            InstallResult.Text = "Select a route first.";
            InstallResult.IsVisible = true;
            return;
        }

        var owner = this.FindAncestorOfType<Window>();
        if (owner is null) return;

        InstallButton.IsEnabled = false;
        InstallProgress.IsVisible = true;
        InstallProgress.Value = 0;
        InstallResult.IsVisible = false;

        var progress = new Progress<(string message, double percent)>(u =>
        {
            InstallProgress.Value = u.percent;
            RouteSummary.Text = u.message;
        });

        try
        {
            var outcome = await Dlss5Installer.InstallAsync(Main, owner, card, route.Profile, progress);
            InstallResult.Text = outcome.Message;
            InstallResult.IsVisible = true;
            if (outcome.Ran)
            {
                try { await Main.RefreshAsync(); } catch { /* refresh best-effort */ }
                await RefreshAssessmentAsync();
            }
        }
        finally
        {
            InstallProgress.IsVisible = false;
            InstallButton.IsEnabled = true;
        }
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

    /// <summary>
    /// Per-game OptiScaler (universal upscaler) install — resolves the engine service and mirrors the
    /// WinUI InstallEventHandler flow (GPU type / DLSS inputs / hotkey from settings, per-game variant).
    /// The PD-Upscaler REFramework swap for RE Engine titles is not yet ported.
    /// </summary>
    private async void OnOptiScaler(object? sender, RoutedEventArgs e)
    {
        if (Main is null || Card is not { } card) return;
        if (string.IsNullOrWhiteSpace(card.InstallPath) || !Directory.Exists(card.InstallPath))
        {
            ExtrasResult.Text = "No install path resolved for this game yet.";
            ExtrasResult.IsVisible = true;
            return;
        }

        var svc = AppServices.Services.GetService<IOptiScalerService>();
        if (svc is null) { ExtrasResult.Text = "OptiScaler service unavailable."; ExtrasResult.IsVisible = true; return; }

        OptiScalerButton.IsEnabled = false;
        ExtrasProgress.IsVisible = true;
        ExtrasProgress.Value = 0;
        ExtrasResult.IsVisible = false;
        var progress = new Progress<(string message, double percent)>(u =>
        {
            ExtrasProgress.Value = u.percent;
            ExtrasResult.Text = u.message;
            ExtrasResult.IsVisible = true;
        });

        try
        {
            var variant = Main.GetOsVariant(card.GameName, card.Source ?? "");
            var record = await svc.InstallAsync(card, progress,
                Main.Settings.OsGpuType, Main.Settings.OsDlssInputs, Main.Settings.OsHotkey, variant);
            ExtrasResult.Text = record is null
                ? "OptiScaler install did not complete — see log."
                : $"OptiScaler installed{(string.IsNullOrWhiteSpace(record.OsVariant) ? "" : $" ({record.OsVariant})")}.";
            ExtrasResult.IsVisible = true;

            // PD-Upscaler REFramework swap for compatible RE Engine titles (ports the WinUI flow):
            // manifest lists the game and a dinput8.dll is already present.
            if (record is not null
                && Main.Manifest?.PdUpscalerGames is { } pd
                && pd.TryGetValue(card.GameName, out var pdArtifact)
                && File.Exists(Path.Combine(card.InstallPath!, "dinput8.dll")))
            {
                try
                {
                    var refSvc = AppServices.Services.GetService<IREFrameworkService>();
                    if (refSvc is not null)
                    {
                        await refSvc.InstallPdUpscalerAsync(card.GameName, card.InstallPath!, pdArtifact, progress);
                        card.RefInstalledVersion = "PD-Upscaler";
                        card.NotifyAll();
                    }
                }
                catch (Exception pdEx)
                {
                    // Non-fatal — OptiScaler is already installed.
                    ExtrasResult.Text += $"  (PD-Upscaler swap skipped: {pdEx.Message})";
                }
            }

            try { await Main.RefreshAsync(); } catch { /* refresh best-effort */ }
            await RefreshAssessmentAsync();
        }
        catch (Exception ex)
        {
            ExtrasResult.Text = $"OptiScaler failed: {ex.Message}";
            ExtrasResult.IsVisible = true;
        }
        finally
        {
            ExtrasProgress.IsVisible = false;
            OptiScalerButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Display Commander (the RenoDX HDR / display-control addon) toggle — installs if not present,
    /// uninstalls if it is. Uses the engine's InstallDcAsync / UninstallDc commands.
    /// </summary>
    private async void OnDisplayCommander(object? sender, RoutedEventArgs e)
    {
        if (Main is null || Card is not { } card) return;
        if (string.IsNullOrWhiteSpace(card.InstallPath) || !Directory.Exists(card.InstallPath))
        {
            ExtrasResult.Text = "No install path resolved for this game yet.";
            ExtrasResult.IsVisible = true;
            return;
        }

        DisplayCommanderButton.IsEnabled = false;
        try
        {
            if (card.DcStatus == GameStatus.Installed)
            {
                Main.UninstallDc(card);
                ExtrasResult.Text = "Display Commander removed.";
            }
            else
            {
                await Main.InstallDcAsync(card);
                ExtrasResult.Text = card.DcStatus == GameStatus.Installed
                    ? "Display Commander installed."
                    : card.DcActionMessage ?? "Display Commander install did not complete.";
            }
            ExtrasResult.IsVisible = true;
            card.NotifyAll();
        }
        catch (Exception ex)
        {
            ExtrasResult.Text = $"Display Commander failed: {ex.Message}";
            ExtrasResult.IsVisible = true;
        }
        finally { DisplayCommanderButton.IsEnabled = true; }
    }

    private async void OnDxvk(object? sender, RoutedEventArgs e)
    {
        if (Main is null || Card is null) return;
        try { await Main.InstallDxvkAsync(Card); }
        catch (Exception ex) { Card.ActionMessage = $"DXVK failed: {ex.Message}"; }
    }

    private async void OnReShade(object? sender, RoutedEventArgs e)
    {
        if (Main is null || Card is null) return;
        try { await Main.InstallReShadeAsync(Card); }
        catch (Exception ex) { Card.ActionMessage = $"ReShade failed: {ex.Message}"; }
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
