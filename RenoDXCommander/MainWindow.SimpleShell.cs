using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private sealed record RendererChoice(string Label, GraphicsApiType Api)
    {
        public override string ToString() => Label;
    }

    private int _simpleSelectionRevision;
    private bool _simpleOperationRunning;
    private bool _mfgUnlockOperationRunning;

    private void InitializeSimpleShell()
    {
        SimpleGameList.ItemsSource = ViewModel.DisplayedGames;
        SimpleRendererOverride.ItemsSource = new[]
        {
            new RendererChoice("DirectX 8", GraphicsApiType.DirectX8),
            new RendererChoice("DirectX 9", GraphicsApiType.DirectX9),
            new RendererChoice("DirectX 10", GraphicsApiType.DirectX10),
            new RendererChoice("DirectX 11", GraphicsApiType.DirectX11),
            new RendererChoice("DirectX 12", GraphicsApiType.DirectX12),
            new RendererChoice("Vulkan", GraphicsApiType.Vulkan),
            new RendererChoice("OpenGL", GraphicsApiType.OpenGL),
        };
        ViewModel.DisplayedGames.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            UpdateSimpleGameCount();
            if (SimpleGameList.SelectedItem == null && ViewModel.DisplayedGames.Count > 0)
                SimpleGameList.SelectedItem = ViewModel.DisplayedGames[0];
        });
        UpdateSimpleGameCount();
    }

    private void UpdateSimpleGameCount()
        => SimpleGameCountText.Text = $"{ViewModel.DisplayedGames.Count} games";

    private void SimpleSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!string.Equals(SearchBox.Text, SimpleSearchBox.Text, StringComparison.Ordinal))
            SearchBox.Text = SimpleSearchBox.Text;
    }

    private async void SimpleGameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var card = SimpleGameList.SelectedItem as GameCardViewModel;
        if (card == null)
        {
            SimpleEmptyState.Visibility = Visibility.Visible;
            SimpleDetailScroll.Visibility = Visibility.Collapsed;
            SimpleMfgUnlockCard.Visibility = Visibility.Collapsed;
            return;
        }

        if (GameList.SelectedItem != card)
            GameList.SelectedItem = card;
        ViewModel.SelectedGame = card;
        await UpdateSimpleSelectedGameAsync(card);
    }

    private async Task UpdateSimpleSelectedGameAsync(GameCardViewModel card)
    {
        var revision = Interlocked.Increment(ref _simpleSelectionRevision);
        SimpleEmptyState.Visibility = Visibility.Collapsed;
        SimpleDetailScroll.Visibility = Visibility.Visible;
        SimpleGameName.Text = card.GameName;
        SimpleInstallPath.Text = card.InstallPath;
        UpdateSimpleMfgUnlockCard(card);
        SimpleRendererText.Text = "Detecting";
        SimpleSetupTitle.Text = "Checking this game";
        SimpleSetupSummary.Text = "Detecting the renderer and checking the current setup…";
        SimpleActionStatus.Text = "";
        SimpleInstallButton.IsEnabled = false;
        SimpleRemoveButton.IsEnabled = false;

        try
        {
            var compatibility = App.Services.GetRequiredService<Dlss5CompatibilityService>();
            var components = App.Services.GetRequiredService<Dlss5ComponentService>();
            var snapshot = await Task.Run(() =>
            {
                var probe = ProbeDlss5(compatibility, card);
                var assessment = Dlss5CompatibilityService.Assess(probe, singlePlayerConfirmed: true);
                var installedPath = Dlss5ComponentService.FindInstalledDeploymentPath(card.InstallPath);
                var installedMode = installedPath == null
                    ? Dlss5DeploymentMode.None
                    : components.GetInstalledMode(installedPath);
                var report = installedPath == null || installedMode == Dlss5DeploymentMode.None
                    ? null
                    : Dlss5DiagnosticService.Diagnose(installedPath, installedMode, assessment.Is64Bit);
                return (probe, assessment, installedPath, installedMode, report);
            });
            if (revision != _simpleSelectionRevision || SimpleGameList.SelectedItem != card) return;

            var (probe, assessment, installedPath, installedMode, report) = snapshot;
            var renderer = probe.GraphicsApi == GraphicsApiType.Unknown
                ? "Renderer unknown"
                : GraphicsApiDetector.GetLabel(probe.GraphicsApi) + (probe.Is64Bit ? " · 64-bit" : " · 32-bit");
            if (ViewModel.GetSingleApiOverride(card.GameName, card.Source ?? "") != null)
                renderer += " · manual";
            SimpleRendererText.Text = renderer;
            // Re-evaluate the MFG Unlock card with the probe's accurate 32/64-bit result.
            UpdateSimpleMfgUnlockCard(card, is32BitOverride: !probe.Is64Bit);

            var selectedOverride = ViewModel.GetSingleApiOverride(card.GameName, card.Source ?? "");
            SimpleRendererOverride.SelectedItem = SimpleRendererOverride.Items
                .OfType<RendererChoice>().FirstOrDefault(item => item.Api == selectedOverride);

            var installed = installedPath != null && installedMode != Dlss5DeploymentMode.None;
            SimpleRemoveButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            SimpleRemoveButton.IsEnabled = installed && !_simpleOperationRunning;
            SimpleInstallButton.Content = installed ? "Repair automatically" : "Install best setup";

            if (probe.GraphicsApi == GraphicsApiType.Unknown)
            {
                SimpleSetupTitle.Text = "Renderer needs confirmation";
                SimpleSetupSummary.Text = "Adas could not prove which renderer reaches gameplay. Choose it once under Advanced; the override applies only to this game.";
                SimpleInstallButton.IsEnabled = !_simpleOperationRunning;
                return;
            }

            if (!assessment.CanInstall)
            {
                SimpleSetupTitle.Text = "Setup needs confirmation";
                SimpleSetupSummary.Text = string.Join(" ", assessment.BlockingReasons)
                    + "  Click Install to confirm the game folder and set it up anyway.";
                SimpleInstallButton.Content = installed ? "Repair anyway…" : "Install anyway…";
                // Allow the user to override the block by reconfirming the folder,
                // as long as a renderer/route was determined.
                SimpleInstallButton.IsEnabled = Dlss5CompatibilityService.CanConfirmDeploymentPath(assessment)
                                                && !_simpleOperationRunning;
                return;
            }

            if (installed)
            {
                var status = DescribeSimpleInstalledStatus(report);
                SimpleSetupTitle.Text = status.Title;
                SimpleSetupSummary.Text = status.Summary;
            }
            else
            {
                var profile = SelectAutomaticProfile(assessment);
                SimpleSetupTitle.Text = "Best setup is ready";
                SimpleSetupSummary.Text = DescribeAutomaticRoute(assessment, profile);
            }
            SimpleInstallButton.IsEnabled = !_simpleOperationRunning;
        }
        catch (Exception ex)
        {
            if (revision != _simpleSelectionRevision) return;
            SimpleSetupTitle.Text = "Could not check this game";
            SimpleSetupSummary.Text = ex.Message;
            SimpleInstallButton.IsEnabled = !_simpleOperationRunning;
        }
    }

    internal static Dlss5InstallProfile SelectAutomaticProfile(Dlss5Assessment assessment)
    {
        if (assessment.Mode is Dlss5DeploymentMode.NativeDirectX12
            or Dlss5DeploymentMode.NativeDirectX11
            or Dlss5DeploymentMode.NativeVulkan)
            return Dlss5InstallProfile.MaximumQuality;

        if (assessment.Is64Bit
            && assessment.Mode is (Dlss5DeploymentMode.Dx9Feeder
                or Dlss5DeploymentMode.Dx11Feeder
                or Dlss5DeploymentMode.Dx12Feeder))
            return Dlss5InstallProfile.ExperimentalUnified;

        return Dlss5InstallProfile.LatestFeederBeta;
    }

    private static string DescribeAutomaticRoute(Dlss5Assessment assessment, Dlss5InstallProfile profile)
        => profile switch
        {
            Dlss5InstallProfile.ExperimentalUnified =>
                "Adas will use the direct 64-bit ShortFuse pipeline. It replaces the old Feeder-plus-consumer chain, so only one neural add-on is installed.",
            Dlss5InstallProfile.LatestFeederBeta when !assessment.Is64Bit =>
                "Adas will install one matched 32-bit game add-on and 64-bit helper, then verify both halves before launch.",
            Dlss5InstallProfile.LatestFeederBeta =>
                "Adas will use the current Feeder route for this renderer and verify every required file.",
            _ when assessment.Mode is Dlss5DeploymentMode.NativeDirectX11 or Dlss5DeploymentMode.NativeVulkan =>
                "Adas will preserve the game's native DLSS data and add the current bridge with one neural add-on.",
            _ => "Adas will use the game's native DLSS data with one neural add-on.",
        };

    private async void SimpleInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleGameList.SelectedItem is not GameCardViewModel card || _simpleOperationRunning) return;
        await RunSimpleAutomaticInstallAsync(card);
    }

    private async Task RunSimpleAutomaticInstallAsync(GameCardViewModel card)
    {
        var compatibility = App.Services.GetRequiredService<Dlss5CompatibilityService>();
        var components = App.Services.GetRequiredService<Dlss5ComponentService>();
        var probe = await Task.Run(() => ProbeDlss5(compatibility, card));
        if (probe.GraphicsApi == GraphicsApiType.Unknown)
        {
            if (await ChooseDlss5RendererOverrideAsync(card) == null) return;
            probe = await Task.Run(() => ProbeDlss5(compatibility, card));
        }

        var assessment = Dlss5CompatibilityService.Assess(probe, singlePlayerConfirmed: true);
        var forced = false;
        if (assessment.Mode == Dlss5DeploymentMode.None)
        {
            await ShowDlss5MessageAsync("This game cannot be changed",
                assessment.BlockingReasons.Count > 0
                    ? string.Join("\n", assessment.BlockingReasons)
                    : "Adas could not determine a supported renderer for this game.");
            await UpdateSimpleSelectedGameAsync(card);
            return;
        }
        if (!assessment.CanInstall || string.IsNullOrWhiteSpace(assessment.DeploymentPath))
        {
            if (!Dlss5CompatibilityService.CanConfirmDeploymentPath(assessment))
            {
                await ShowDlss5MessageAsync("This game cannot be changed", string.Join("\n", assessment.BlockingReasons));
                await UpdateSimpleSelectedGameAsync(card);
                return;
            }
            // Blocked (usually an ambiguous or unconfirmed game folder). Let the user reconfirm the
            // exact executable folder and install anyway, instead of hard-blocking.
            var suggested = !string.IsNullOrWhiteSpace(assessment.DeploymentPath)
                ? assessment.DeploymentPath
                : Dlss5CompatibilityService.ResolveDeploymentPath(card.InstallPath).Candidates.FirstOrDefault()
                  ?? card.InstallPath;
            var chosen = await ConfirmGameFolderAndPickAsync(card, assessment, suggested);
            if (string.IsNullOrWhiteSpace(chosen))
            {
                await UpdateSimpleSelectedGameAsync(card);
                return;
            }
            assessment = Dlss5CompatibilityService.ConfirmDeploymentPath(assessment, chosen);
            forced = true;
        }

        var deploymentPath = assessment.DeploymentPath
            ?? throw new InvalidOperationException("The confirmed game executable folder is missing.");
        var profile = SelectAutomaticProfile(assessment);
        Dlss5CleanupPlan cleanup;
        try
        {
            cleanup = await Task.Run(() => Dlss5ComponentService.GetCleanupPlan(
                deploymentPath, assessment.Mode, profile));
        }
        catch (Exception ex)
        {
            await ShowDlss5MessageAsync("Could not prepare a safe change", ex.Message);
            return;
        }

        var running = await Task.Run(() => GameProcessService.FindRunningProcesses(card.InstallPath));
        var currentPath = Dlss5ComponentService.FindInstalledDeploymentPath(card.InstallPath);
        var isRepair = currentPath != null && components.GetInstalledMode(currentPath) != Dlss5DeploymentMode.None;
        var details = DescribeAutomaticRoute(assessment, profile)
            + "\n\nAdas will back up replaced files, remove conflicting managed pipelines, and restore the previous setup if the switch fails."
            + (running.Count > 0 ? "\n\nThe game is running and will be closed automatically before files are changed." : "")
            + (cleanup.Files.Count > 0 ? $"\n\n{cleanup.Files.Count} conflicting file(s) will be preserved under .adas\\preserved." : "")
            + "\n\nContinue only for single-player or offline use.";
        if (!forced)
        {
            var confirm = await DialogService.ShowSafeAsync(new ContentDialog
            {
                Title = isRepair ? $"Repair {card.GameName}?" : $"Install for {card.GameName}?",
                Content = MakeDlss5Text(details),
                PrimaryButtonText = isRepair ? "Repair automatically" : "Install automatically",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot,
            });
            if (confirm != ContentDialogResult.Primary) return;
        }

        var canWrite = await Task.Run(() =>
            FileSystemAccessService.CanWriteToDirectory(deploymentPath, out var error)
                ? (Allowed: true, Error: (string?)null)
                : (Allowed: false, Error: error));
        if (!canWrite.Allowed)
        {
            await _dialogService.ShowGameFolderAdminRequiredDialogAsync(
                card.GameName, deploymentPath, canWrite.Error);
            return;
        }

        if (running.Count > 0)
        {
            var stopErrors = await GameProcessService.StopProcessesAsync(running);
            if (stopErrors.Count > 0)
            {
                await ShowDlss5MessageAsync("Could not close the game", string.Join("\n", stopErrors));
                return;
            }
            await Task.Delay(250);
        }

        SetSimpleOperationState(true, "Preparing a safe installation…", 2);
        var progress = new Progress<(string message, double percent)>(update =>
            SetSimpleOperationState(true, update.message, update.percent));
        try
        {
            var relocationErrors = await Task.Run(() =>
                components.RemoveOtherManagedDeployments(card.InstallPath, deploymentPath));
            if (relocationErrors.Count > 0)
                throw new IOException(string.Join("\n", relocationErrors));

            // Re-check after confirmation and process shutdown. Never install to a stale target —
            // unless the user manually reconfirmed the folder (forced), in which case trust their choice.
            Dlss5Assessment installAssessment = assessment;
            if (!forced)
            {
                var freshProbe = await Task.Run(() => ProbeDlss5(compatibility, card));
                var fresh = Dlss5CompatibilityService.Assess(freshProbe, singlePlayerConfirmed: true);
                if (!fresh.CanInstall || fresh.Mode != assessment.Mode
                    || !string.Equals(fresh.DeploymentPath, assessment.DeploymentPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The renderer or game folder changed. No new setup was installed; try again.");
                installAssessment = fresh;
            }

            var result = await Task.Run(() => components.InstallAsync(
                card.GameName,
                installAssessment,
                progress,
                reShadeChannel: ViewModel.ResolveReShadeChannel(card.GameName, card.Source ?? ""),
                store: card.Source,
                profile: profile,
                cleanupApproval: cleanup));
            SimpleActionStatus.Text = result.Warnings.Count == 0
                ? "Required files installed and checked. Launch the game to confirm neural rendering."
                : "Required files installed and checked. " + string.Join(" ", result.Warnings.Distinct(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[SimpleShell.Install] {ex}");
            SimpleActionStatus.Text = "Installation stopped safely: " + ex.Message;
            await ShowDlss5MessageAsync("Installation stopped", ex.Message);
        }
        finally
        {
            var finalStatus = SimpleActionStatus.Text;
            SetSimpleOperationState(false, finalStatus, 0);
            await UpdateSimpleSelectedGameAsync(card);
            SimpleActionStatus.Text = finalStatus;
        }
    }

    internal static (string Title, string Summary) DescribeSimpleInstalledStatus(Dlss5DiagnosticReport? report)
    {
        if (report == null)
            return ("DLSS 5 files are installed", "Adas found an installation record, but has not checked the live neural-rendering session yet.");

        var findings = report.Findings.Count == 0 ? "" : " " + string.Join(" ", report.Findings);
        if (report.HasProblems)
            return ("Repair recommended", report.Summary + " Adas can replace wrong or missing managed files automatically." + findings);
        if (report.IsWorking)
            return ("DLSS 5 is working", report.Summary + findings);

        return (
            "DLSS 5 files are installed",
            "The required files match, but live neural rendering is not yet confirmed." + findings);
    }

    private void SetSimpleOperationState(bool running, string message, double percent)
    {
        _simpleOperationRunning = running;
        SimpleInstallProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        SimpleInstallProgress.Value = percent;
        SimpleActionStatus.Text = message;
        SimpleInstallButton.IsEnabled = !running;
        SimpleLaunchButton.IsEnabled = !running;
        SimpleRemoveButton.IsEnabled = !running && SimpleRemoveButton.Visibility == Visibility.Visible;
        SimpleFullCleanupButton.IsEnabled = !running;
        SimpleRemoveGameFromListButton.IsEnabled = !running;
        SimpleRefreshButton.IsEnabled = !running && !ViewModel.IsLoading;
    }

    private void SimpleLaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleGameList.SelectedItem is GameCardViewModel card && !_simpleOperationRunning)
            LaunchGame(card);
    }

    private async void SimpleRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleGameList.SelectedItem is not GameCardViewModel card || _simpleOperationRunning) return;
        await RemoveSimpleSetupAsync(card);
    }

    private async Task RemoveSimpleSetupAsync(GameCardViewModel card)
    {
        var components = App.Services.GetRequiredService<Dlss5ComponentService>();
        var path = Dlss5ComponentService.FindInstalledDeploymentPath(card.InstallPath);
        if (path == null)
        {
            await UpdateSimpleSelectedGameAsync(card);
            return;
        }
        var running = await Task.Run(() => GameProcessService.FindRunningProcesses(card.InstallPath));
        var confirm = await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = $"Remove DLSS 5 from {card.GameName}?",
            Content = MakeDlss5Text("Adas will remove only its managed files and restore tracked originals. Modified user files are preserved."
                + (running.Count > 0 ? " The game will be closed automatically first." : "")),
            PrimaryButtonText = "Remove & restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        });
        if (confirm != ContentDialogResult.Primary) return;

        if (!FileSystemAccessService.CanWriteToDirectory(path, out var accessError))
        {
            await _dialogService.ShowGameFolderAdminRequiredDialogAsync(card.GameName, path, accessError);
            return;
        }
        if (running.Count > 0)
        {
            var stopErrors = await GameProcessService.StopProcessesAsync(running);
            if (stopErrors.Count > 0)
            {
                await ShowDlss5MessageAsync("Could not close the game", string.Join("\n", stopErrors));
                return;
            }
            await Task.Delay(250);
        }

        SetSimpleOperationState(true, "Removing managed files and restoring originals…", 40);
        try
        {
            var errors = await Task.Run(() => components.Uninstall(path));
            SimpleActionStatus.Text = errors.Count == 0
                ? "Removed. Tracked originals were restored."
                : "Removal needs attention: " + string.Join(" ", errors);
            if (errors.Count > 0)
                await ShowDlss5MessageAsync("Removal needs attention", string.Join("\n", errors));
        }
        finally
        {
            var finalStatus = SimpleActionStatus.Text;
            SetSimpleOperationState(false, finalStatus, 0);
            await UpdateSimpleSelectedGameAsync(card);
            SimpleActionStatus.Text = finalStatus;
        }
    }

    private async void SimpleApplyRendererOverride_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleGameList.SelectedItem is not GameCardViewModel card
            || SimpleRendererOverride.SelectedItem is not RendererChoice choice) return;
        ViewModel.SetApiOverride(card.GameName, new List<string> { choice.Api.ToString() }, card.Source ?? "");
        card.GraphicsApi = choice.Api;
        card.DetectedApis = new HashSet<GraphicsApiType> { choice.Api };
        card.IsDualApiGame = false;
        card.NotifyAll();
        await UpdateSimpleSelectedGameAsync(card);
    }

    private async void SimpleClearRendererOverride_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleGameList.SelectedItem is not GameCardViewModel card) return;
        ViewModel.SetApiOverride(card.GameName, null, card.Source ?? "");
        SimpleRendererOverride.SelectedItem = null;
        var compatibility = App.Services.GetRequiredService<Dlss5CompatibilityService>();
        var probe = await Task.Run(() => ProbeDlss5(compatibility, card));
        card.GraphicsApi = probe.GraphicsApi;
        card.DetectedApis = new HashSet<GraphicsApiType>(probe.SupportedGraphicsApis);
        card.IsDualApiGame = card.DetectedApis.Count > 1;
        card.NotifyAll();
        await UpdateSimpleSelectedGameAsync(card);
    }

    private void SimpleOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleGameList.SelectedItem is not GameCardViewModel card || !Directory.Exists(card.InstallPath)) return;
        Process.Start(new ProcessStartInfo(card.InstallPath) { UseShellExecute = true });
    }

    private void SimpleExperimentalMethods_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleGameList.SelectedItem is not GameCardViewModel card || sender is not FrameworkElement source) return;
        source.Tag = card;
        Dlss5ManageButton_Click(source, e);
    }

    private async void SimpleFullCleanupButton_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleGameList.SelectedItem is not GameCardViewModel card || _simpleOperationRunning) return;
        var cleanupService = App.Services.GetRequiredService<GameCleanupService>();
        GameCleanupPlan plan;
        try
        {
            SetSimpleOperationState(true, "Scanning the whole game folder…", 10);
            plan = await Task.Run(() => cleanupService.CreatePlan(card.InstallPath));
        }
        catch (Exception ex)
        {
            SetSimpleOperationState(false, "Cleanup could not start: " + ex.Message, 0);
            await ShowDlss5MessageAsync("Full cleanup could not start", ex.Message);
            return;
        }
        finally
        {
            if (_simpleOperationRunning)
                SetSimpleOperationState(false, SimpleActionStatus.Text, 0);
        }

        if (plan.ItemCount == 0)
        {
            SimpleActionStatus.Text = "No recognized DLSS 5 or ReShade files were found.";
            return;
        }

        var running = await Task.Run(() => GameProcessService.FindRunningProcesses(card.InstallPath));
        var confirm = await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = $"Fully clean {card.GameName}?",
            Content = MakeDlss5Text(
                $"Adas found {plan.ItemCount} managed installation or recognizable leftover item(s) in this game and its subfolders.\n\n" +
                "Tracked original files will be restored. Recognized leftovers and your ReShade settings will be moved to a recovery folder outside the game. Unknown game files are never removed. " +
                "If an original backup is missing, Adas will leave the recovery record in place and tell you to verify the game through its store." +
                (running.Count > 0 ? "\n\nThe game is running and will be closed first." : "")),
            PrimaryButtonText = "Clean game folder",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        });
        if (confirm != ContentDialogResult.Primary) return;

        if (!FileSystemAccessService.CanWriteToDirectory(card.InstallPath, out var accessError))
        {
            await _dialogService.ShowGameFolderAdminRequiredDialogAsync(card.GameName, card.InstallPath, accessError);
            return;
        }
        if (running.Count > 0)
        {
            var stopErrors = await GameProcessService.StopProcessesAsync(running);
            if (stopErrors.Count > 0)
            {
                await ShowDlss5MessageAsync("Could not close the game", string.Join("\n", stopErrors));
                return;
            }
            await Task.Delay(250);
        }

        SetSimpleOperationState(true, "Restoring originals and cleaning all subfolders…", 50);
        try
        {
            var result = await Task.Run(() => cleanupService.Execute(plan, card.GameName));
            card.RsRecord = null;
            card.RsInstalledFile = null;
            card.RsInstalledVersion = null;
            card.RsStatus = GameStatus.NotInstalled;
            card.NotifyAll();

            var recovery = result.RecoveryPath == null ? "" : $" Recovery copy: {result.RecoveryPath}";
            SimpleActionStatus.Text = result.Errors.Count == 0
                ? $"Cleanup complete. Restored/removed {result.ManagedInstallationsRemoved} managed setup(s) and archived {result.LeftoversArchived} leftover item(s).{recovery}"
                : "Cleanup needs attention. Verify the game files through its store. " + string.Join(" ", result.Errors);
            if (result.Errors.Count > 0)
                await ShowDlss5MessageAsync("Cleanup needs attention",
                    string.Join("\n", result.Errors) + "\n\nUse the game's store launcher to verify or repair missing original files.");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[SimpleShell.FullCleanup] {ex}");
            SimpleActionStatus.Text = "Cleanup stopped: " + ex.Message;
            await ShowDlss5MessageAsync("Cleanup stopped", ex.Message);
        }
        finally
        {
            var finalStatus = SimpleActionStatus.Text;
            SetSimpleOperationState(false, finalStatus, 0);
            await UpdateSimpleSelectedGameAsync(card);
            SimpleActionStatus.Text = finalStatus;
        }
    }

    private async void SimpleRemoveGameFromListButton_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleGameList.SelectedItem is not GameCardViewModel card || _simpleOperationRunning) return;
        var confirm = await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = $"Remove {card.GameName} from Adas?",
            Content = MakeDlss5Text("This removes only the game entry from Adas. It does not delete the game or change files in its folder. Use Full cleanup first if you also want DLSS 5 and ReShade removed."),
            PrimaryButtonText = "Remove from list",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        });
        if (confirm != ContentDialogResult.Primary) return;

        var oldIndex = SimpleGameList.SelectedIndex;
        if (card.IsManuallyAdded)
            ViewModel.RemoveManualGameCommand.Execute(card);
        else
            ViewModel.ToggleHideGameCommand.Execute(card);

        SimpleGameList.SelectedItem = null;
        if (ViewModel.DisplayedGames.Count > 0)
            SimpleGameList.SelectedIndex = Math.Clamp(oldIndex, 0, ViewModel.DisplayedGames.Count - 1);
        UpdateSimpleGameCount();
    }

    /// <summary>
    /// Shown when the automatic check flagged a blocker (usually an ambiguous game folder).
    /// Explains the flag, lets the user pick the exact executable folder, and returns it so the
    /// install can proceed anyway. Returns null if the user cancels.
    /// </summary>
    private async Task<string?> ConfirmGameFolderAndPickAsync(GameCardViewModel card, Dlss5Assessment assessment, string? suggested)
    {
        var reasons = assessment.BlockingReasons.Count > 0
            ? "Adas flagged this game:\n\n• " + string.Join("\n• ", assessment.BlockingReasons) + "\n\n"
            : "";
        var confirm = await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = $"Install {card.GameName} anyway?",
            Content = MakeDlss5Text(reasons
                + "Choose the exact folder that holds the game's executable (the .exe). "
                + "Adas will install there and skip the automatic check.\n\n"
                + $"Detected route: {assessment.ModeLabel}. Single-player / offline use only."),
            PrimaryButtonText = "Choose folder…",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        });
        if (confirm != ContentDialogResult.Primary) return null;
        return await PickGameFolderAsync(suggested);
    }

    private async Task<string?> PickGameFolderAsync(string? suggested)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
            };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path ?? suggested;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[SimpleShell.PickGameFolder] {ex.Message}");
            return null;
        }
    }

    // ── MFG Ada Unlock (RTX 40-series only) ──────────────────────────────────

    private void UpdateSimpleMfgUnlockCard(GameCardViewModel card, bool? is32BitOverride = null)
    {
        // Shown only on GeForce RTX 40-series (Ada) GPUs. RTX 30 lacks the machine code;
        // RTX 50 already ships Multi Frame Generation natively.
        if (!Dlss5CompatibilityService.IsAdaGpuDetected)
        {
            SimpleMfgUnlockCard.Visibility = Visibility.Collapsed;
            return;
        }

        var folder = ResolveMfgUnlockFolder(card);
        var installed = !string.IsNullOrEmpty(folder) && _mfgUnlockService.IsInstalledIn(folder);
        // The add-on is 64-bit only, and DLSS Frame Generation only exists in 64-bit games.
        // is32BitOverride comes from the game probe (accurate); card.Is32Bit is the build-time guess.
        var is32Bit = is32BitOverride ?? card.Is32Bit;
        var supported = !is32Bit;

        // Not applicable to 32-bit games. Hide entirely unless a build was mistakenly installed here,
        // in which case keep the card so the user can remove it.
        if (!supported && !installed)
        {
            SimpleMfgUnlockCard.Visibility = Visibility.Collapsed;
            return;
        }
        SimpleMfgUnlockCard.Visibility = Visibility.Visible;

        SimpleMfgUnlockInstallButton.Content = installed ? "Reinstall" : "Install MFG Unlock";
        SimpleMfgUnlockInstallButton.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        SimpleMfgUnlockInstallButton.IsEnabled =
            supported && !_simpleOperationRunning && !_mfgUnlockOperationRunning && !string.IsNullOrEmpty(folder);
        SimpleMfgUnlockSettingsButton.Visibility = (installed && supported) ? Visibility.Visible : Visibility.Collapsed;
        SimpleMfgUnlockSettingsButton.IsEnabled = !_mfgUnlockOperationRunning;
        SimpleMfgUnlockRemoveButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        SimpleMfgUnlockRemoveButton.IsEnabled = !_mfgUnlockOperationRunning;

        SimpleMfgUnlockSummary.Text = !supported
            ? "Not supported on this game. MFG Unlock is 64-bit and needs a 64-bit game with DLSS Frame Generation; a build was installed here by mistake. Use Remove to clean it up."
            : installed
                ? "Installed. Launch the game and pick your frame-generation multiplier, or use Settings for fine control."
                : "Unlock DLSS Multi Frame Generation 3×/4×/6× on your RTX 40-series GPU. In-memory only — no files are changed on disk.";

        if (!_mfgUnlockOperationRunning)
            SimpleMfgUnlockStatus.Text = installed && _mfgUnlockService.StagedVersion is { } version
                ? $"Version {version}"
                : "";
    }

    private async void SimpleMfgUnlockInstall_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleGameList.SelectedItem is not GameCardViewModel card || _mfgUnlockOperationRunning) return;
        if (card.Is32Bit) return; // 64-bit add-on; never install into a 32-bit game.
        var folder = ResolveMfgUnlockFolder(card);
        if (string.IsNullOrEmpty(folder)) return;

        _mfgUnlockOperationRunning = true;
        SimpleMfgUnlockInstallButton.IsEnabled = false;
        SimpleMfgUnlockRemoveButton.IsEnabled = false;
        SimpleMfgUnlockStatus.Text = "Installing MFG Ada Unlock…";
        try
        {
            var progress = new Progress<(string message, double percent)>(u => SimpleMfgUnlockStatus.Text = u.message);
            var ok = await _mfgUnlockService.InstallAsync(folder, progress);
            SimpleMfgUnlockStatus.Text = ok
                ? "✅ Installed. Only takes effect in games that run DLSS Frame Generation."
                : "❌ Install failed — make sure ReShade with add-on support is installed for this game.";
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[SimpleShell.MfgUnlock.Install] {ex}");
            SimpleMfgUnlockStatus.Text = "❌ " + ex.Message;
        }
        finally
        {
            _mfgUnlockOperationRunning = false;
            UpdateSimpleMfgUnlockCard(card);
        }
    }

    private void SimpleMfgUnlockRemove_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleGameList.SelectedItem is not GameCardViewModel card || _mfgUnlockOperationRunning) return;
        var folder = ResolveMfgUnlockFolder(card);
        if (string.IsNullOrEmpty(folder)) return;

        _mfgUnlockOperationRunning = true;
        try
        {
            var ok = _mfgUnlockService.Uninstall(folder);
            SimpleMfgUnlockStatus.Text = ok ? "Removed." : "❌ Removal failed.";
        }
        finally
        {
            _mfgUnlockOperationRunning = false;
            UpdateSimpleMfgUnlockCard(card);
        }
    }

    private async void SimpleMfgUnlockSettings_Click(object sender, RoutedEventArgs e)
    {
        if (SimpleGameList.SelectedItem is not GameCardViewModel card || _mfgUnlockOperationRunning) return;
        var folder = ResolveMfgUnlockFolder(card);
        if (string.IsNullOrEmpty(folder)) return;
        await MfgUnlockDialog.ShowAsync(_mfgUnlockService, card.GameName, folder, Content.XamlRoot);
    }

    /// <summary>
    /// The folder MFG Unlock deploys into — the same folder ReShade loads add-ons from for this
    /// game (next to reshade.ini and the DLSS 5 add-ons), not necessarily the game's root install path.
    /// </summary>
    private static string ResolveMfgUnlockFolder(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return "";
        var installed = Dlss5ComponentService.FindInstalledDeploymentPath(card.InstallPath);
        if (!string.IsNullOrEmpty(installed)) return installed;
        var resolved = Dlss5CompatibilityService.ResolveDeploymentPath(card.InstallPath).Path;
        return string.IsNullOrEmpty(resolved) ? card.InstallPath : resolved;
    }
}
