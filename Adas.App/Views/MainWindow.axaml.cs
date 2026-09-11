using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Adas.App.Shell;
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
        ToolsButton.Click += OnTools;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                SetupView.Main = vm;
                WireSeams(vm);
            }
        };

        Loaded += OnLoaded;
    }

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

    private void OnTools(object? sender, RoutedEventArgs e)
    {
        var tools = new ToolsWindow(Vm);
        tools.Show(this);
    }

    /// <summary>
    /// Connects the remaining <see cref="MainViewModel"/> UI-callback seams that need named controls
    /// (list scrolling, per-card rebuilds, shader-pack pickers). Dialog/dispatcher seams are wired in
    /// <see cref="Adas.App.Shell.DialogWiring"/>. Avalonia rebuild of the WinUI MainWindow wiring.
    /// </summary>
    private void WireSeams(MainViewModel vm)
    {
        vm.ScrollToSelectedGame = () =>
        {
            void Scroll()
            {
                if (vm.SelectedGame is { } g && vm.DisplayedGames.Contains(g))
                    GamesList.ScrollIntoView(g);
            }
            if (Dispatcher.UIThread.CheckAccess()) Scroll();
            else Dispatcher.UIThread.Post(Scroll);
        };

        // Re-evaluate Luma injection for the card, then refresh its bound view. In the binding-driven
        // Avalonia UI a "panel rebuild" is a property re-notify plus a route re-assessment.
        vm.RequestCardRebuild = card => Dispatcher.UIThread.Post(() =>
        {
            vm.ReevaluateLumaForCard(card);
            card.NotifyAll();
            if (ReferenceEquals(card, vm.SelectedGame)) _ = SetupView.RefreshAsync();
        });

        vm.RequestOverridesPanelRebuild = card => Dispatcher.UIThread.Post(() =>
        {
            card.NotifyAll();
            if (ReferenceEquals(card, vm.SelectedGame)) _ = SetupView.RefreshAsync();
        });

        vm.ShowShaderSelectionPicker = current =>
            ShaderSelectionDialog.ShowAsync(this, ShaderPacks, current, global: true);

        vm.ShowPerGameShaderSelectionPicker = (_, current) =>
            ShaderSelectionDialog.ShowAsync(this, ShaderPacks, current, global: false);
    }

    private static IShaderPackService ShaderPacks
        => Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<IShaderPackService>(RenoDXCommander.Abstractions.AppServices.Services);

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
