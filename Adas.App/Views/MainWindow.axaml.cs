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
    private int _updatableCount;
    private bool _updatingAll;

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
        ScanFolderMenuItem.Click += OnAddFolder;
        EmptyScanButton.Click += OnRescan;
        UpdateButton.Click += OnUpdate;
        UpdateAllButton.Click += OnUpdateAll;

        // First-run onboarding overlay.
        FirstRunDismissButton.Click += (_, _) => DismissFirstRun();
        FirstRunScanButton.Click += (_, _) => { DismissFirstRun(); OnRescan(this, new RoutedEventArgs()); };

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

        RefreshUpdateAllButton();
    }

    /// <summary>Shows "Update all (N)" when N games are pinned to a component version older than Adas ships.</summary>
    private void RefreshUpdateAllButton()
    {
        var stale = 0;
        if (Vm?.AllCards is { } cards)
            foreach (var c in cards)
                if (c.HasComponentUpdate) stale++;

        _updatableCount = stale;
        UpdateAllButton.IsVisible = stale > 0;
        UpdateAllText.Text = stale == 1 ? "Update 1" : $"Update all ({stale})";
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

        try { if (!UiLayoutStore.LoadFirstRunDone()) FirstRunOverlay.IsVisible = true; }
        catch { /* onboarding is best-effort */ }

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

    /// <summary>Hides the first-run overlay and records that it has been seen.</summary>
    private void DismissFirstRun()
    {
        FirstRunOverlay.IsVisible = false;
        try { UiLayoutStore.SaveFirstRunDone(true); } catch { /* best-effort */ }
    }

    /// <summary>
    /// Re-runs the DLSS 5 install (repair path) for every game whose recorded component version is older
    /// than the one Adas now ships, so a user who updated Adas can bring all their installs current.
    /// </summary>
    private async void OnUpdateAll(object? sender, RoutedEventArgs e)
    {
        if (_updatingAll || Vm is not { } vm) return;

        var stale = new System.Collections.Generic.List<GameCardViewModel>();
        foreach (var c in vm.AllCards)
            if (c.HasComponentUpdate) stale.Add(c);
        if (stale.Count == 0) return;

        var confirm = await DialogHost.ConfirmAsync(this, "Update all installs",
            $"Adas will re-run the install for {stale.Count} game(s) pinned to an older component version. "
            + "Each game's current route is kept; this re-deploys the components at the versions Adas now ships.\n\n"
            + "Close any of these games before continuing.",
            primaryText: "Update all", closeText: "Cancel");
        if (!confirm) return;

        _updatingAll = true;
        UpdateAllButton.IsEnabled = false;
        var progress = new Progress<(string message, double percent)>(u => vm.StatusText = u.message);

        try
        {
            var updated = 0;
            foreach (var card in stale)
            {
                vm.SubStatusText = $"Updating {card.GameName}…";
                try
                {
                    // Re-run the game's *recorded* route so the components are re-deployed and the record's
                    // ComponentVersion is rewritten to the current one — that is what clears the stale flag.
                    // (Repair only fixes the ReShade config and never bumps the version, so it can't update.)
                    var route = await Task.Run(() => ResolveInstalledRoute(card));
                    if (route is null) continue;
                    var outcome = await Dlss5Installer.InstallAsync(vm, this, card, route.Value.Profile, progress,
                        deepFriedChicken: route.Value.DeepFriedChicken,
                        bridgeSubstitute: route.Value.BridgeSubstitute,
                        forceProfile: true, risksConfirmed: true);
                    if (outcome.Ran)
                    {
                        updated++;
                        // Refresh only this game's DLSS 5 state (re-reads its record) instead of rescanning
                        // the whole library — the badge/count clears from the freshly-rewritten record.
                        vm.RefreshCardDlss5State(card, card.InstallPath);
                        card.NotifyAll();
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[MainWindow.OnUpdateAll] {card.GameName} — {ex.Message}"); }
            }

            vm.StatusText = updated == stale.Count
                ? $"Updated {updated} game(s)."
                : $"Updated {updated} of {stale.Count} game(s); see the log for the rest.";
        }
        finally
        {
            _updatingAll = false;
            UpdateAllButton.IsEnabled = true;
            RefreshUpdateAllButton();
        }
    }

    /// <summary>
    /// Reads the DLSS 5 install record for a game and returns the route it was installed with, so an
    /// update can re-run exactly that route. Returns null when no record can be located.
    /// </summary>
    private static (Dlss5InstallProfile Profile, bool DeepFriedChicken, bool BridgeSubstitute)? ResolveInstalledRoute(GameCardViewModel card)
    {
        if (string.IsNullOrWhiteSpace(card.InstallPath)) return null;
        var record = Dlss5ComponentService.LoadRecord(card.InstallPath)
            ?? Dlss5ComponentService.LoadRecord(ModInstallService.GetAddonDeployPath(card.InstallPath));
        if (record is null && Dlss5ComponentService.FindInstalledDeploymentPath(card.InstallPath) is { } root)
            record = Dlss5ComponentService.LoadRecord(root);
        return record is null ? null : (record.Profile, record.DeepFriedChicken, record.BridgeSubstitute);
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
    /// "Add a specific folder…" — lets the user pick one or more folders, scans each for game candidates
    /// (a folder that directly holds an .exe is one game; otherwise each immediate subfolder that contains
    /// an .exe is a candidate), then presents a checklist of the games found. Each confirmed folder becomes
    /// a manually-added game built in place via <see cref="MainViewModel.AddManualGame"/> — only the new
    /// cards are added, the library is not rescanned.
    /// </summary>
    private async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || StorageProvider is not { } sp) return;

        try
        {
            var folders = await sp.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "Select a game folder or a folder that contains your games",
                AllowMultiple = true,
            });
            if (folders.Count == 0) return;

            var detection = AppServices.Services.GetService<IGameDetectionService>();
            if (detection is null) { vm.StatusText = "Game detection service unavailable."; return; }

            vm.StatusText = "Scanning folder(s) for games…";

            // Scan each picked root for candidate game folders (off the UI thread), de-duplicated by path.
            var roots = folders.Select(f => f.Path.LocalPath).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            var candidates = await Task.Run(() =>
            {
                var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var found = new System.Collections.Generic.List<GameCandidate>();
                foreach (var root in roots)
                    foreach (var c in detection.FindGameCandidates(root))
                        if (seen.Add(c.Path)) found.Add(c);
                return found;
            });

            if (candidates.Count == 0)
            {
                vm.StatusText = "No games found in the selected folder(s).";
                return;
            }

            var chosen = await GameCandidateSelectionDialog.ShowAsync(this, candidates);
            if (chosen is null || chosen.Count == 0) { vm.StatusText = "No games added."; return; }

            foreach (var c in chosen)
                vm.AddManualGame(new DetectedGame
                {
                    Name = c.Name,
                    InstallPath = c.Path,
                    Source = "Manual",
                    IsManuallyAdded = true,
                });

            vm.StatusText = chosen.Count == 1
                ? "Added 1 game to your library."
                : $"Added {chosen.Count} games to your library.";
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Add folder failed: {ex.Message}";
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
