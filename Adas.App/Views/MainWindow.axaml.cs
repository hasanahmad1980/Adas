using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Adas.App.Shell;
using RenoDXCommander.Abstractions;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace Adas.App.Views;

public partial class MainWindow : Window
{
    private bool _initialized;
    private UpdateInfo? _pendingUpdate;

    public MainWindow()
    {
        InitializeComponent();

        RefreshButton.Click += OnRefresh;
        RescanButton.Click += OnRescan;
        ToolsButton.Click += OnTools;
        SettingsButton.Click += OnSettings;
        HistoryButton.Click += OnHistory;
        UpdateButton.Click += OnUpdate;

        // Show the running version in the header (stamped from Adas Setup.iss at publish time).
        try
        {
            var v = UpdateSvc.CurrentVersion;
            VersionText.Text = $"v{v.Major}.{v.Minor}.{v.Build}";
        }
        catch { /* version display is best-effort */ }

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

        // Silent app-update check after startup; surfaces the header Update button if newer.
        _ = CheckForAppUpdateAsync();
    }

    private static IUpdateService UpdateSvc
        => ServiceProviderServiceExtensions.GetRequiredService<IUpdateService>(AppServices.Services);

    /// <summary>
    /// Queries GitHub for a newer Adas release. When one exists, reveals the header Update button;
    /// clicking it downloads the installer and relaunches. Failures are silent (offline, rate-limit).
    /// </summary>
    private async Task CheckForAppUpdateAsync()
    {
        try
        {
            var info = await UpdateSvc.CheckForUpdateAsync().ConfigureAwait(true);
            if (info == null) return;

            _pendingUpdate = info;
            void Reveal()
            {
                UpdateButton.Content = $"⬆ Update to {info.DisplayVersion ?? info.RemoteVersion.ToString()}";
                UpdateButton.IsVisible = true;
            }
            if (Dispatcher.UIThread.CheckAccess()) Reveal();
            else await Dispatcher.UIThread.InvokeAsync(Reveal);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.CheckForAppUpdate] {ex.Message}");
        }
    }

    private async void OnUpdate(object? sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is not { } info) return;

        var current = UpdateSvc.CurrentVersion;
        var confirm = await DialogHost.ConfirmAsync(this, "Update available",
            $"A newer version of Adas is available.\n\n" +
            $"Installed:  v{current.Major}.{current.Minor}.{current.Build}\n" +
            $"Available:  {info.DisplayVersion ?? info.RemoteVersion.ToString()}\n\n" +
            "Download the installer and relaunch now? Your settings and installs are preserved.",
            primaryText: "Download & install", closeText: "Later");
        if (!confirm) return;

        UpdateButton.IsEnabled = false;
        var progress = new Progress<(string msg, double pct)>(p =>
        {
            if (Vm is { } vm) vm.StatusText = p.msg;
        });

        try
        {
            var path = await UpdateSvc.DownloadInstallerAsync(info.DownloadUrl, progress);
            if (string.IsNullOrEmpty(path))
            {
                await DialogHost.ConfirmAsync(this, "Update failed",
                    "The installer could not be downloaded. Please try again later or download it "
                    + "from the Adas releases page.", primaryText: "OK", closeText: "Close");
                UpdateButton.IsEnabled = true;
                return;
            }

            UpdateSvc.LaunchInstallerAndExit(path, () =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
                else Close();
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.OnUpdate] {ex.Message}");
            UpdateButton.IsEnabled = true;
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

    private void OnSettings(object? sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(Vm);
        settings.Show(this);
    }

    private void OnHistory(object? sender, RoutedEventArgs e)
    {
        var history = new HistoryWindow(Vm);
        history.Show(this);
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
