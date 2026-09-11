using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace Adas.App.Views;

public partial class MainWindow : Window
{
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();

        RefreshButton.Click += OnRefresh;
        RescanButton.Click += OnRescan;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                SetupView.Main = vm;
        };

        Loaded += OnLoaded;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private MainViewModel? Vm => DataContext as MainViewModel;

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        _ = PopulateHealthBannerAsync();

        if (Vm is { } vm)
        {
            try { await vm.InitializeAsync(); }
            catch (Exception ex) { vm.StatusText = $"Startup failed: {ex.Message}"; vm.IsLoading = false; }
        }
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
        {
            try { await vm.RefreshAsync(); } catch (Exception ex) { vm.StatusText = $"Refresh failed: {ex.Message}"; }
        }
    }

    private async void OnRescan(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
        {
            try { await vm.FullRefreshAsync(null); } catch (Exception ex) { vm.StatusText = $"Rescan failed: {ex.Message}"; }
        }
    }

    /// <summary>
    /// Fills the GPU + driver health banner. Detection can spawn nvidia-smi, so it runs off the UI
    /// thread and marshals the result back. Mirrors the Feeder/oneclick driver pre-flight banner.
    /// </summary>
    private async Task PopulateHealthBannerAsync()
    {
        string gpu = "", driver = "", warning = "";
        await Task.Run(() =>
        {
            try
            {
                gpu = Dlss5CompatibilityService.DetectedGpuName;
                driver = Dlss5CompatibilityService.DetectedDriverVersion;
                warning = Dlss5CompatibilityService.GetDriverPreflightWarning(Dlss5DeploymentMode.NativeDirectX12) ?? "";
            }
            catch { /* detection best-effort */ }
        });

        void Apply()
        {
            GpuText.Text = string.IsNullOrWhiteSpace(gpu) ? "Not detected" : gpu;
            DriverText.Text = string.IsNullOrWhiteSpace(driver) ? "Not detected" : driver;
            if (!string.IsNullOrWhiteSpace(warning))
            {
                DriverWarnText.Text = warning;
                DriverWarnBadge.IsVisible = true;
            }
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else await Dispatcher.UIThread.InvokeAsync(Apply);
    }
}
