using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Adas.App.Shell;
using Adas.App.Views.Pages;
using RenoDXCommander.Abstractions;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace Adas.App.Views;

public partial class MainWindow : Window
{
    private bool _initialized;
    private UpdateInfo? _pendingUpdate;
    private bool _hasDriverWarning;
    private bool _overlayDismissed;
    private string _currentPage = "Library";

    public MainWindow()
    {
        InitializeComponent();

        // Nav rail
        LibraryNav.IsChecked = true;
        LibraryNav.Click += (_, _) => NavigateTo("Library");
        ToolsNav.Click += (_, _) => NavigateTo("Tools");
        HistoryNav.Click += (_, _) => NavigateTo("Installed");
        SettingsNav.Click += (_, _) => NavigateTo("Settings");

        // Library toolbar
        RefreshButton.Click += OnRefresh;
        RescanButton.Click += OnRescan;
        EmptyScanButton.Click += OnRescan;
        UpdateButton.Click += OnUpdate;

        // Filter segments
        FilterAll.IsChecked = true;
        FilterAll.Click += (_, _) => ApplyLibraryFilter("Detected");
        FilterInstalled.Click += (_, _) => ApplyLibraryFilter("Installed");
        FilterHidden.Click += (_, _) => ApplyLibraryFilter("Hidden");

        // Health pill reveals the driver warning strip when there is one.
        HealthPill.Click += (_, _) =>
        {
            if (_hasDriverWarning) DriverWarnBadge.IsVisible = !DriverWarnBadge.IsVisible;
        };

        // Loading overlay is dismissible: hide it but let the scan keep running.
        DismissOverlayButton.Click += (_, _) =>
        {
            _overlayDismissed = true;
            LoadingOverlay.IsVisible = false;
        };

        // Show the running version on the rail (stamped from Adas Setup.iss at publish time).
        try
        {
            var v = UpdateSvc.CurrentVersion;
            VersionText.Text = $"v{v.Major}.{v.Minor}.{v.Build}";
        }
        catch { /* version display is best-effort */ }

        // Restore the saved library/detail split, and persist it whenever the user drags the divider.
        RestoreSplit();
        BodySplitter.DragCompleted += OnSplitterDragCompleted;

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                SetupView.Main = vm;
                WireSeams(vm);
                vm.PropertyChanged += OnVmPropertyChanged;
                vm.Filter.PropertyChanged += OnFilterPropertyChanged;
                UpdateLibraryChrome();
            }
        };

        Loaded += OnLoaded;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    // Body columns: [0] = library, [1] = splitter, [2] = detail. (x:Name on a ColumnDefinition
    // does not generate a field, so we address them by index on the named grid.)
    private ColumnDefinition LeftCol => BodyGrid.ColumnDefinitions[0];
    private ColumnDefinition RightCol => BodyGrid.ColumnDefinitions[2];

    /// <summary>Applies the persisted library/detail split ratio to the two star columns.</summary>
    private void RestoreSplit()
    {
        if (UiLayoutStore.LoadSplitRatio() is not { } ratio) return;
        LeftCol.Width = new GridLength(ratio, GridUnitType.Star);
        RightCol.Width = new GridLength(1 - ratio, GridUnitType.Star);
    }

    /// <summary>Persists the split ratio after the user finishes dragging the divider.</summary>
    private void OnSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        var left = LeftCol.Width.IsStar ? LeftCol.Width.Value : LeftCol.ActualWidth;
        var right = RightCol.Width.IsStar ? RightCol.Width.Value : RightCol.ActualWidth;
        var total = left + right;
        if (total > 0) UiLayoutStore.SaveSplitRatio(left / total);
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    /// <summary>Swaps the content host between the Library page and a secondary page.</summary>
    private void NavigateTo(string page)
    {
        // Persist Settings edits when leaving the Settings page.
        if (_currentPage == "Settings" && page != "Settings")
        {
            try { Vm?.SaveSettingsPublic(); } catch (Exception ex) { CrashReporter.Log($"[MainWindow.NavigateTo] SaveSettings — {ex.Message}"); }
        }

        _currentPage = page;

        // Keep the rail as a single-selection group.
        LibraryNav.IsChecked = page == "Library";
        ToolsNav.IsChecked = page == "Tools";
        HistoryNav.IsChecked = page == "Installed";
        SettingsNav.IsChecked = page == "Settings";

        PageTitle.Text = page;

        if (page == "Library")
        {
            PageHost.Content = null;
            PageHost.IsVisible = false;
            LibraryRoot.IsVisible = true;
            return;
        }

        LibraryRoot.IsVisible = false;
        PageHost.Content = page switch
        {
            "Tools" => new ToolsPage(Vm),
            "Installed" => new HistoryPage(Vm),
            "Settings" => new SettingsPage(Vm),
            _ => null
        };
        PageHost.IsVisible = true;
    }

    // ── Library filter segments ──────────────────────────────────────────────

    private void ApplyLibraryFilter(string filter)
    {
        Vm?.Filter.SetFilter(filter);
        SyncFilterSegments();
    }

    private void SyncFilterSegments()
    {
        var mode = Vm?.FilterMode ?? "Detected";
        FilterAll.IsChecked = mode.Equals("Detected", StringComparison.OrdinalIgnoreCase);
        FilterInstalled.IsChecked = mode.Equals("Installed", StringComparison.OrdinalIgnoreCase);
        FilterHidden.IsChecked = mode.Equals("Hidden", StringComparison.OrdinalIgnoreCase);
    }

    private void OnFilterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FilterViewModel.TotalGames) or nameof(FilterViewModel.FilterMode))
            Dispatcher.UIThread.Post(UpdateLibraryChrome);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsLoading))
            Dispatcher.UIThread.Post(UpdateOverlay);
        else if (e.PropertyName is nameof(MainViewModel.FilterMode) or nameof(MainViewModel.SearchQuery))
            Dispatcher.UIThread.Post(UpdateLibraryChrome);
    }

    /// <summary>Refreshes the count label, empty-state and filter segments from the current filter.</summary>
    private void UpdateLibraryChrome()
    {
        SyncFilterSegments();
        var count = Vm?.Filter.TotalGames ?? 0;
        LibraryCount.Text = count == 1 ? "1 game" : $"{count} games";

        var empty = count == 0 && Vm is { IsLoading: false };
        EmptyState.IsVisible = empty;
        GamesList.IsVisible = !empty;
        if (empty)
        {
            var searching = !string.IsNullOrWhiteSpace(Vm?.SearchQuery);
            EmptyStateText.Text = searching ? "No games match your search" : "No games found";
        }
    }

    private void UpdateOverlay()
    {
        if (Vm is not { } vm) return;
        if (vm.IsLoading)
        {
            if (!_overlayDismissed) LoadingOverlay.IsVisible = true;
        }
        else
        {
            _overlayDismissed = false;
            LoadingOverlay.IsVisible = false;
        }
        UpdateLibraryChrome();
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        _ = PopulateHealthBannerAsync();

        if (Vm is { } vm)
        {
            UpdateOverlay();
            try { await vm.InitializeAsync(); }
            catch (Exception ex) { vm.StatusText = $"Startup failed: {ex.Message}"; vm.IsLoading = false; }
        }

        // Silent app-update check after startup; surfaces the update banner if newer.
        _ = CheckForAppUpdateAsync();
    }

    private static IUpdateService UpdateSvc
        => ServiceProviderServiceExtensions.GetRequiredService<IUpdateService>(AppServices.Services);

    /// <summary>
    /// Queries GitHub for a newer Adas release. When one exists, reveals the update banner;
    /// clicking Update downloads the installer and relaunches. Failures are silent (offline, rate-limit).
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
                var version = info.DisplayVersion ?? info.RemoteVersion.ToString();
                UpdateBannerText.Text = $"Adas {version} is available.";
                UpdateButton.Content = "Update";
                UpdateBanner.IsVisible = true;
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
            var path = await UpdateSvc.DownloadInstallerAsync(info.DownloadUrl, progress, info.ExpectedSha256);
            if (string.IsNullOrEmpty(path))
            {
                var hasReleasePage = !string.IsNullOrWhiteSpace(info.ReleasePageUrl);
                var openPage = await DialogHost.ConfirmAsync(this, "Update couldn't be verified",
                    "Adas couldn't download and verify the installer's checksum, so nothing was installed. "
                    + (hasReleasePage
                        ? "You can open the Adas releases page and install it manually."
                        : "Please try again later, or install it from the Adas releases page."),
                    primaryText: hasReleasePage ? "Open releases page" : "OK", closeText: "Close");
                if (openPage && hasReleasePage)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.ReleasePageUrl!)
                        { UseShellExecute = true });
                    }
                    catch (Exception ex) { CrashReporter.Log($"[MainWindow.OnUpdate] Could not open release page — {ex.Message}"); }
                }
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

    /// <summary>Context-menu "Hide"/"Show" on a library card toggles the game's hidden state.</summary>
    private void OnHideGameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: GameCardViewModel card })
            Vm?.ToggleHideGame(card);
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
    /// Fills the GPU + driver health pill. Detection can spawn nvidia-smi, so it runs off the UI
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
            _hasDriverWarning = !string.IsNullOrWhiteSpace(warning);
            if (_hasDriverWarning)
            {
                DriverWarnText.Text = warning;
                HealthDot.Fill = this.FindResource("AdasWarnBrush") as IBrush ?? HealthDot.Fill;
            }
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else await Dispatcher.UIThread.InvokeAsync(Apply);
    }
}
