using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Abstractions;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace Adas.App.Views;

/// <summary>
/// Standalone Tools window: NeuralScreen launch, Universal MFG unlock into the selected game,
/// and a savable diagnostics report. Reuses the engine services directly — no VM coupling — so
/// these paths match what the retired WinUI shell exposed.
/// </summary>
public partial class ToolsWindow : Window
{
    private readonly MainViewModel? _main;
    private string? _lastReport;

    public ToolsWindow() : this(null) { }

    public ToolsWindow(MainViewModel? main)
    {
        InitializeComponent();
        _main = main;

        NeuralScreenButton.Click += OnNeuralScreen;
        MfgButton.Click += OnMfg;
        MfgRemoveButton.Click += OnMfgRemove;
        DiagButton.Click += OnDiagnostics;
        SaveDiagButton.Click += OnSaveDiagnostics;

        var target = _main?.SelectedGame;
        if (target is not null && !string.IsNullOrWhiteSpace(target.InstallPath))
        {
            MfgTarget.Text = $"Target: {target.GameName} — {target.InstallPath}";
            _ = RefreshMfgPlanAsync();
        }
        else
        {
            MfgTarget.Text = "No game with a resolved install path is selected.";
            MfgButton.IsEnabled = false;
        }
    }

    private async void OnNeuralScreen(object? sender, RoutedEventArgs e)
    {
        NeuralScreenButton.IsEnabled = false;
        Output.Text = "Launching NeuralScreen…";
        try
        {
            var svc = AppServices.Services.GetService<Dlss5ComponentService>();
            if (svc is null) { Output.Text = "DLSS 5 service unavailable."; return; }
            await svc.LaunchNeuralScreenAsync();
            Output.Text = "NeuralScreen launched.";
        }
        catch (Exception ex) { Output.Text = $"NeuralScreen failed: {ex.Message}"; }
        finally { NeuralScreenButton.IsEnabled = true; }
    }

    private RtxMfgUnlockService.MfgPlacement? _mfgPlan;

    private async Task RefreshMfgPlanAsync()
    {
        var card = _main?.SelectedGame;
        if (card is null || string.IsNullOrWhiteSpace(card.InstallPath)) return;
        MfgPlan.Text = "Checking the game…";
        var plan = await Task.Run(() =>
        {
            var exe = AppServices.Services.GetService<IPeHeaderService>()?.FindGameExe(card.InstallPath);
            return RtxMfgUnlockService.PlanPlacement(card.InstallPath, exe, Dlss5CompatibilityService.DetectedGpuName);
        });
        _mfgPlan = plan;
        var installed = plan.TargetFolder is { } dir ? RtxMfgUnlockService.InstalledProxyFilename(dir) : null;
        var lines = new System.Collections.Generic.List<string>();
        if (installed is not null) lines.Add($"✔ Installed as {installed} in {plan.TargetFolder}.");
        if (plan.ProxyFilename is not null)
            lines.Add($"Autopilot: {plan.ProxyFilename}{(plan.ProxyImportedByExe ? " (imported by the exe)" : "")} → {plan.TargetFolder}");
        if (plan.FrameGenerationFile is not null)
            lines.Add($"Game frame generation found: {Path.GetFileName(plan.FrameGenerationFile)}");
        foreach (var w in plan.Warnings) lines.Add("⚠ " + w);
        MfgPlan.Text = string.Join("\n", lines);
        MfgRemoveButton.IsVisible = installed is not null;
        MfgButton.IsEnabled = plan.ProxyFilename is not null;
    }

    private async void OnMfgRemove(object? sender, RoutedEventArgs e)
    {
        var card = _main?.SelectedGame;
        if (card is null || _mfgPlan?.TargetFolder is not { } dir) return;
        var guard = await Shell.GameCloseGuard.EnsureClosedAsync(this, card.GameName, dir);
        if (!guard.CanProceed) { Output.Text = guard.Error ?? "Cancelled."; return; }
        var svc = AppServices.Services.GetService<RtxMfgUnlockService>();
        Output.Text = svc?.Uninstall(dir) == true ? "Universal MFG unlock removed; any original file restored." : "Removal failed — see log.";
        await RefreshMfgPlanAsync();
    }

    private async void OnMfg(object? sender, RoutedEventArgs e)
    {
        var card = _main?.SelectedGame;
        var folder = card?.InstallPath;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            Output.Text = "Select a game with a resolved install folder first.";
            return;
        }

        if (_mfgPlan is null) await RefreshMfgPlanAsync();
        if (_mfgPlan is not { TargetFolder: { } target, ProxyFilename: { } proxy } plan)
        {
            Output.Text = "No free proxy filename was found for this game; nothing was installed.";
            return;
        }
        if (plan.Warnings.Count > 0
            && !await Shell.DialogHost.ConfirmAsync(this, "Install Universal MFG unlock?",
                string.Join("\n\n", plan.Warnings), "Install anyway", "Cancel"))
        {
            Output.Text = "Cancelled.";
            return;
        }
        folder = target;

        var guard = await Shell.GameCloseGuard.EnsureClosedAsync(this, card!.GameName, folder);
        if (!guard.CanProceed)
        {
            Output.Text = guard.Error ?? "Cancelled.";
            return;
        }

        MfgButton.IsEnabled = false;
        try
        {
            var svc = AppServices.Services.GetService<RtxMfgUnlockService>();
            if (svc is null) { Output.Text = "MFG service unavailable."; return; }

            var progress = new Progress<(string message, double percent)>(u => Output.Text = u.message);
            var ok = await svc.InstallAsync(folder, proxy, progress);
            Output.Text = ok
                ? $"Universal MFG unlock installed into {card!.GameName} as {proxy}. Enable DLSS Frame Generation in game, then press Backspace for the MFG menu."
                : "MFG install did not complete — see log.";
            await RefreshMfgPlanAsync();
        }
        catch (Exception ex) { Output.Text = $"MFG install failed: {ex.Message}"; }
        finally { MfgButton.IsEnabled = true; }
    }

    private async void OnDiagnostics(object? sender, RoutedEventArgs e)
    {
        DiagButton.IsEnabled = false;
        SaveDiagButton.IsEnabled = false;
        Output.Text = "Generating diagnostics report…";
        try
        {
            var svc = AppServices.Services.GetService<DiagnosticsBundleService>();
            if (svc is null) { Output.Text = "Diagnostics service unavailable."; return; }

            var folder = _main?.SelectedGame?.InstallPath;
            _lastReport = await svc.BuildReportAsync(folder);
            Output.Text = _lastReport;
            SaveDiagButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastReport);
        }
        catch (Exception ex) { Output.Text = $"Diagnostics failed: {ex.Message}"; }
        finally { DiagButton.IsEnabled = true; }
    }

    private async void OnSaveDiagnostics(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastReport)) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save diagnostics report",
                SuggestedFileName = $"adas-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text file") { Patterns = new[] { "*.txt" } },
                },
            });
            if (file is null) return;

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(_lastReport);
            Output.Text = $"Saved to {file.Path.LocalPath}";
        }
        catch (Exception ex) { Output.Text = $"Save failed: {ex.Message}"; }
    }
}
