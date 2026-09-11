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
        DxvkButton.Click += OnDxvk;
        ReShadeButton.Click += OnReShade;
        OpenFolderButton.Click += OnOpenFolder;

        DataContextChanged += (_, _) => _ = RefreshAssessmentAsync();
    }

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
