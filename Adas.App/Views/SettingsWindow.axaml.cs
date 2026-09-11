using Avalonia.Controls;
using Avalonia.Interactivity;
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

        CloseButton.Click += OnClose;
        Closed += (_, _) => _main?.SaveSettingsPublic();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
