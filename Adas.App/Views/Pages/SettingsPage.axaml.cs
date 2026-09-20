using System;
using Adas.App.Shell;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Abstractions;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace Adas.App.Views.Pages;

/// <summary>
/// Global settings editor page. Binds two-way to the app's single <see cref="SettingsViewModel"/>
/// instance so edits mutate live state; the owning <see cref="MainViewModel"/> persists them via
/// SaveSettingsPublic when the user navigates away from this page (MainWindow.NavigateTo). Hosted in
/// the MainWindow page host.
/// </summary>
public partial class SettingsPage : UserControl
{
    private readonly MainViewModel? _main;

    public SettingsPage() : this(null) { }

    public SettingsPage(MainViewModel? main)
    {
        InitializeComponent();
        _main = main;

        ReShadeChannelCombo.ItemsSource = new[] { "Stable", "Nightly" };
        DxvkVariantCombo.ItemsSource = new[] { "Development", "Stable", "LiliumHdr" };
        OsGpuTypeCombo.ItemsSource = new[] { "NVIDIA", "AMD", "Intel" };

        if (main is not null) DataContext = main.Settings;

        AddonsButton.Click += OnChooseAddons;
        ScreenshotFolderButton.Click += OnChooseScreenshotFolder;

        UpdateAddonsHint();

        if (main is not null)
        {
            var s = main.Settings;
            NexusStatus.Text = string.IsNullOrWhiteSpace(s.NexusUsername)
                ? "Not signed in."
                : $"Signed in as {s.NexusUsername}{(s.NexusIsPremium ? " (Premium)" : "")}.";
        }
    }

    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    private async void OnChooseScreenshotFolder(object? sender, RoutedEventArgs e)
    {
        if (_main is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var folders = await sp.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Select the screenshot folder",
            AllowMultiple = false,
        });
        if (folders.Count > 0)
            _main.Settings.ScreenshotPath = folders[0].Path.LocalPath;
    }

    private async void OnChooseAddons(object? sender, RoutedEventArgs e)
    {
        if (_main is null || OwnerWindow is not { } owner) return;
        var svc = AppServices.Services.GetService<IAddonPackService>();
        if (svc is null) { AddonsHint.Text = "Add-on service unavailable."; return; }

        var result = await AddonSelectionDialog.ShowAsync(owner, svc, _main.Settings.EnabledGlobalAddons);
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
