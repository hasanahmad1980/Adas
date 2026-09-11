using System;
using Adas.App.Shell;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Abstractions;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace Adas.App.Views;

/// <summary>
/// Global settings editor. Binds two-way to the app's single <see cref="SettingsViewModel"/>
/// instance so edits mutate live state; the owning <see cref="MainViewModel"/> persists them via
/// SaveSettingsPublic when the window closes. Avalonia rebuild of the WinUI Settings page (subset —
/// the most-used global toggles; per-component update-skip flags and Nexus/HDR settings are TODO).
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel? _main;

    public SettingsWindow() : this(null) { }

    public SettingsWindow(MainViewModel? main)
    {
        InitializeComponent();
        _main = main;

        ReShadeChannelCombo.ItemsSource = new[] { "Stable", "Nightly" };
        DxvkVariantCombo.ItemsSource = new[] { "Development", "Stable", "LiliumHdr" };
        OsGpuTypeCombo.ItemsSource = new[] { "NVIDIA", "AMD", "Intel" };

        if (main is not null) DataContext = main.Settings;

        AddonsButton.Click += OnChooseAddons;
        CloseButton.Click += OnClose;
        Closed += (_, _) => _main?.SaveSettingsPublic();

        UpdateAddonsHint();

        if (main is not null)
        {
            var s = main.Settings;
            NexusStatus.Text = string.IsNullOrWhiteSpace(s.NexusUsername)
                ? "Not signed in."
                : $"Signed in as {s.NexusUsername}{(s.NexusIsPremium ? " (Premium)" : "")}.";
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private async void OnChooseAddons(object? sender, RoutedEventArgs e)
    {
        if (_main is null) return;
        var svc = AppServices.Services.GetService<IAddonPackService>();
        if (svc is null) { AddonsHint.Text = "Add-on service unavailable."; return; }

        var result = await AddonSelectionDialog.ShowAsync(this, svc, _main.Settings.EnabledGlobalAddons);
        if (result is null) return; // cancelled

        _main.Settings.EnabledGlobalAddons = result;
        _main.SaveSettingsPublic();
        try { _main.DeployAllAddons(); } catch (Exception ex) { AddonsHint.Text = $"Deploy failed: {ex.Message}"; return; }
        UpdateAddonsHint();
    }

    private void UpdateAddonsHint()
    {
        var count = _main?.Settings.EnabledGlobalAddons.Count ?? 0;
        AddonsHint.Text = count == 0 ? "No global add-ons enabled." : $"{count} global add-on(s) enabled.";
    }
}
