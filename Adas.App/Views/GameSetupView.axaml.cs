using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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

    /// <summary>
    /// Runs the engine compatibility probe/assessment for the selected game off the UI thread and
    /// surfaces the recommended route + any blocking reasons. This is the Avalonia rebuild of the
    /// per-game route summary the WinUI detail panel showed.
    /// </summary>
    private async Task RefreshAssessmentAsync()
    {
        var card = Card;
        if (card is null) return;

        RouteText.Text = "Analysing…";
        RouteReasons.IsVisible = false;

        string route = "", reasons = "";
        await Task.Run(() =>
        {
            try
            {
                var compat = AppServices.Services.GetService<Dlss5CompatibilityService>();
                if (compat is null) { route = "Compatibility service unavailable."; return; }

                var probe = compat.Probe(card);
                var assessment = Dlss5CompatibilityService.Assess(probe);

                route = assessment.CanInstall
                    ? $"✓ {assessment.ModeLabel}"
                    : $"✗ {assessment.ModeLabel} — not available for this game";

                var lines = assessment.BlockingReasons
                    .Concat(assessment.MissingRequirements)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .ToArray();
                if (lines.Length > 0)
                    reasons = "• " + string.Join(Environment.NewLine + "• ", lines);
            }
            catch (Exception ex) { route = $"Assessment failed: {ex.Message}"; }
        });

        void Apply()
        {
            if (!ReferenceEquals(Card, card)) return; // selection changed while probing
            RouteText.Text = route;
            RouteReasons.Text = reasons;
            RouteReasons.IsVisible = !string.IsNullOrEmpty(reasons);
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else await Dispatcher.UIThread.InvokeAsync(Apply);
    }

    private void OnInstall(object? sender, RoutedEventArgs e)
    {
        if (Main is null || Card is null) return;
        if (Main.InstallModCommand.CanExecute(Card))
            Main.InstallModCommand.Execute(Card);
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
