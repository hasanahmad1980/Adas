using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    /// <summary>
    /// Prompts the user for the author's official Deep Fried Chicken zip and imports it (unmodified)
    /// into Adas's cache. Returns true on success. DFC is never bundled — its licence forbids it.
    /// </summary>
    private async Task<bool> ImportDeepFriedChickenAsync(DeepFriedChickenService dfc)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
            picker.FileTypeFilter.Add(".zip");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file == null) return false;
            var error = await dfc.ImportAsync(file.Path);
            if (error != null)
            {
                await ShowDlss5MessageAsync("Deep Fried Chicken import failed", error);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            await ShowDlss5MessageAsync("Deep Fried Chicken import failed", ex.Message);
            return false;
        }
    }

    /// <summary>True when the NVIDIA driver string (e.g. "616.64") is at least major.minor.</summary>
    private static bool DriverAtLeast(string? version, int major, int minor)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        var parts = version.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var maj) || !int.TryParse(parts[1], out var min))
            return false;
        return maj > major || (maj == major && min >= minor);
    }

    private Dlss5Probe ProbeDlss5(
        Dlss5CompatibilityService compatibility,
        GameCardViewModel card)
        => compatibility.Probe(
            card,
            ViewModel.GetSingleApiOverride(card.GameName, card.Source ?? ""));

    private async Task<GraphicsApiType?> ChooseDlss5RendererOverrideAsync(GameCardViewModel card)
    {
        var choices = new Dictionary<string, GraphicsApiType>
        {
            ["DirectX 8"] = GraphicsApiType.DirectX8,
            ["DirectX 9"] = GraphicsApiType.DirectX9,
            ["DirectX 10"] = GraphicsApiType.DirectX10,
            ["DirectX 11"] = GraphicsApiType.DirectX11,
            ["DirectX 12"] = GraphicsApiType.DirectX12,
            ["Vulkan"] = GraphicsApiType.Vulkan,
            ["OpenGL"] = GraphicsApiType.OpenGL,
        };
        var selector = new ComboBox
        {
            ItemsSource = choices.Keys.ToArray(),
            PlaceholderText = "Choose the renderer used in gameplay",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 320,
        };
        var panel = new StackPanel { Spacing = 12, MaxWidth = 520 };
        panel.Children.Add(MakeDlss5Text(
            "Automatic detection did not capture reliable gameplay evidence. Choose the renderer this game is actually using. Adas will remember it and use it for ReShade, DLSS 5, repairs, and updates."));
        panel.Children.Add(selector);
        panel.Children.Add(MakeDlss5Text(
            "You can change or clear this later from the game override settings.",
            ResourceKeys.TextSecondaryBrush));

        var dialog = new ContentDialog
        {
            Title = $"Choose renderer for {card.GameName}",
            Content = panel,
            PrimaryButtonText = "Use selected renderer",
            CloseButtonText = "Cancel",
            IsPrimaryButtonEnabled = false,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        selector.SelectionChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = selector.SelectedItem is string;
            dialog.DefaultButton = dialog.IsPrimaryButtonEnabled
                ? ContentDialogButton.Primary
                : ContentDialogButton.Close;
        };

        if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary
            || selector.SelectedItem is not string label
            || !choices.TryGetValue(label, out var selectedApi))
            return null;

        ViewModel.SetApiOverride(card.GameName, new List<string> { selectedApi.ToString() }, card.Source ?? "");
        card.GraphicsApi = selectedApi;
        card.DetectedApis = new HashSet<GraphicsApiType> { selectedApi };
        card.IsDualApiGame = false;
        card.NotifyAll();
        PopulateDetailPanel(card);
        return selectedApi;
    }

    private async void Dlss5InfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        var compatibility = App.Services.GetRequiredService<Dlss5CompatibilityService>();
        var assessment = Dlss5CompatibilityService.Assess(
            await Task.Run(() => ProbeDlss5(compatibility, card)), singlePlayerConfirmed: true);

        var body = new StackPanel { Spacing = 10, MaxWidth = 620 };
        body.Children.Add(MakeDlss5Text(
            "Adas detects the renderer and architecture, then selects one compatible DLSS 5 transport and one neural add-on. Advanced methods are isolated so incompatible pipelines are never mixed."));
        body.Children.Add(MakeDlss5Heading($"Recommendation: {assessment.ModeLabel}"));
        body.Children.Add(MakeDlss5Text(assessment.Mode switch
        {
            Dlss5DeploymentMode.NativeDirectX12 => "Uses RenoDX v4.70 directly with the game's native DirectX 12 DLSS calls.",
            Dlss5DeploymentMode.NativeDirectX11 => $"Uses RenoDX v4.70 with DLSS5 Bridge {Dlss5ComponentService.BridgeVersion} and the game's native motion vectors and depth.",
            Dlss5DeploymentMode.NativeVulkan => $"Uses RenoDX v4.70 with DLSS5 Bridge {Dlss5ComponentService.BridgeVersion} to mirror the game's native Vulkan DLSS contract, motion vectors, depth, and jitter onto D3D12.",
            Dlss5DeploymentMode.Dx11Feeder => "For DirectX 11 games without DLSS. The Feeder builds a DLAA contract from ReShade depth and LumeniteFX motion vectors.",
            Dlss5DeploymentMode.Dx12Feeder => "For DirectX 12 games without native DLSS. The Feeder evaluates directly on the game's D3D12 device.",
            Dlss5DeploymentMode.Dx10ViaDxvkFeeder => "For rare 64-bit DirectX 10 games. Adas installs stable DXVK, switches ReShade to Vulkan, and uses the Vulkan Feeder transport because the x64 Feeder has no native D3D10 backend.",
            Dlss5DeploymentMode.Dx10Feeder => $"For 32-bit DirectX 10 games. Feeder {Dlss5ComponentService.BundledFeederBetaVersion} uses its native private D3D11 relay, so Adas installs directly as dxgi.dll without DXVK or a machine-wide Vulkan layer.",
            Dlss5DeploymentMode.VulkanFeeder => "For Vulkan games. The Feeder shares textures and fences with a private DirectX 12 neural-rendering session.",
            Dlss5DeploymentMode.Dx9Feeder => "For older DirectX 9 games. Adas downloads and configures dgVoodoo2 automatically, installs ReShade as dxgi.dll, and selects the hosted x64 path for 32-bit games.",
            Dlss5DeploymentMode.Dx9ViaDxvkFeeder => $"Automatic recovery for a 32-bit DirectX 9 game whose managed dgVoodoo/ReShade route crashed with a stack overflow. Adas switches this game only to DXVK, Vulkan ReShade, and matched Feeder {Dlss5ComponentService.BundledFeederBetaVersion} files.",
            Dlss5DeploymentMode.Dx8Feeder => "For 32-bit DirectX 8 games. Adas installs the D3D8 dgVoodoo2 wrapper, ReShade for DX11, and Feeder with its matched 64-bit helper.",
            Dlss5DeploymentMode.OpenGlFeeder => "For OpenGL games. ReShade is installed locally as opengl32.dll and the Feeder uses NVIDIA OpenGL/D3D12 interop.",
            _ => "This game does not currently satisfy the DirectX 9 through DirectX 12, Vulkan, or OpenGL requirements.",
        }));
        body.Children.Add(MakeDlss5Heading("Safety policy"));
        body.Children.Add(MakeDlss5Text(
            "RTX 20/30/40/50-series GPUs are supported. Anti-cheat or multiplayer evidence is a hard block. " +
            "Ambiguous binary folders are never guessed. Native and Feeder paths are selected automatically. " +
            $"Every overwritten suite file is backed up for hash-aware uninstall. The ReShade suite and OptiScaler routes remain mutually exclusive. Smooth Motion must stay off on stable Feeder 0.7; Feeder {Dlss5ComponentService.BundledFeederBetaVersion} adds the newer synchronized Present fixes and native 32-bit D3D10 relay."));
        body.Children.Add(MakeDlss5Heading("Included upstream features"));
        body.Children.Add(MakeDlss5Text(
            $"RenoDX DLSS 4.70; DLSS5 Bridge {Dlss5ComponentService.BridgeVersion}; stable Feeder 0.7 plus optional {Dlss5ComponentService.BundledFeederBetaVersion}; ShortFuse (2026-09-02); automatic dgVoodoo2 and DXVK legacy translation; native 32-bit D3D10 relay; x86 hosted deployment; Vulkan and OpenGL transport; " +
            "bundled standard ReShade headers; LumeniteFX Kernel setup; hot-reloaded configuration; local Streamline/runtime import; and the exact " +
            "DLSSNR signature-repair preview/backup/atomic-replace/rollback workflow."));
        body.Children.Add(MakeDlss5Text(
            "Licensing: Adas is GPL-3.0. DLSS5-Feeder and dlssnr-signature-repair are MIT; their notices are preserved."));

        await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = "About the Adas DLSS 5 Suite",
            Content = new ScrollViewer { Content = body, MaxHeight = 620 },
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
        });
    }

    private async void Dlss5ManageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        var emulator = await Task.Run(() => Dlss5EmulatorService.FindInstallation(card.InstallPath));
        if (emulator != null && !await ChooseDlss5EmulatorRendererAsync(emulator, card.InstallPath)) return;
        var compatibility = App.Services.GetRequiredService<Dlss5CompatibilityService>();
        var components = App.Services.GetRequiredService<Dlss5ComponentService>();
        var probe = await Task.Run(() => ProbeDlss5(compatibility, card));
        if (emulator == null && probe.GraphicsApi == GraphicsApiType.Unknown)
        {
            if (await ChooseDlss5RendererOverrideAsync(card) == null) return;
            probe = await Task.Run(() => ProbeDlss5(compatibility, card));
        }
        var assessment = Dlss5CompatibilityService.Assess(probe, singlePlayerConfirmed: true);
        var installedMode = assessment.DeploymentPath == null
            ? Dlss5DeploymentMode.None
            : components.GetInstalledMode(assessment.DeploymentPath);
        if (installedMode == Dlss5DeploymentMode.None)
        {
            var previousDeploymentPath = Dlss5ComponentService.FindInstalledDeploymentPath(card.InstallPath);
            if (previousDeploymentPath != null)
                installedMode = components.GetInstalledMode(previousDeploymentPath);
        }

        var installedRecord = assessment.DeploymentPath == null
            ? null
            : Dlss5ComponentService.LoadRecord(assessment.DeploymentPath);
        var installedProfile = installedRecord?.Mode == assessment.Mode
            ? installedRecord.Profile
            : SelectAutomaticProfile(assessment);
        var selectedProfile = installedProfile switch
        {
            Dlss5InstallProfile.OpenGlBridge when Dlss5ComponentService.SupportsOpenGlBridge(assessment.Mode, assessment.Is64Bit)
                => Dlss5InstallProfile.OpenGlBridge,
            Dlss5InstallProfile.OptiScalerNeuralRendering => Dlss5InstallProfile.OptiScalerNeuralRendering,
            Dlss5InstallProfile.OptiScalerNrBeforeSr => Dlss5InstallProfile.OptiScalerNrBeforeSr,
            Dlss5InstallProfile.StandaloneAio when Dlss5ComponentService.SupportsAio(assessment.Mode, assessment.Is64Bit)
                => Dlss5InstallProfile.StandaloneAio,
            Dlss5InstallProfile.ExperimentalUnified when assessment.Is64Bit
                && assessment.Mode != Dlss5DeploymentMode.NativeVulkan => Dlss5InstallProfile.ExperimentalUnified,
            Dlss5InstallProfile.LatestFeederBeta when Dlss5CompatibilityService.IsFeederMode(assessment.Mode)
                => Dlss5InstallProfile.LatestFeederBeta,
            _ when assessment.Mode == Dlss5DeploymentMode.Dx10Feeder
                || (assessment.Mode is Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.Dx10ViaDxvkFeeder
                        or Dlss5DeploymentMode.Dx9ViaDxvkFeeder
                    && !assessment.Is64Bit)
                => Dlss5InstallProfile.LatestFeederBeta,
            _ => Dlss5InstallProfile.MaximumQuality,
        };

        var content = new StackPanel { Spacing = 12, MaxWidth = 650 };
        var isInstalled = installedMode != Dlss5DeploymentMode.None;
        content.Children.Add(MakeDlss5StatusCard(
            assessment.CanInstall
                ? isInstalled ? "Automatic repair is ready" : "Recommended setup is ready"
                : "Adas cannot install this game yet",
            assessment.CanInstall
                ? $"The renderer folder and architecture are resolved for {card.GameName}. Review the selected setup below; advanced alternatives are optional."
                : "Review the problem below. Adas will not change game files while installation is blocked.",
            assessment.CanInstall));

        content.Children.Add(MakeDlss5Heading("What Adas will do"));
        content.Children.Add(MakeDlss5Text(
            probe.GraphicsApi == GraphicsApiType.Unknown
                ? "Renderer: not confirmed — " + probe.GraphicsApiEvidence
                : $"Renderer: {GraphicsApiDetector.GetLabel(probe.GraphicsApi)} — {probe.GraphicsApiEvidence}",
            probe.GraphicsApi == GraphicsApiType.Unknown ? ResourceKeys.AccentAmberBrush : ResourceKeys.AccentGreenBrush));
        if (probe.OpenXrDetected)
            content.Children.Add(MakeDlss5Text("OpenXR loader detected. Ada treats VR/OpenXR separately; it does not replace the game's DirectX, OpenGL or Vulkan renderer."));
        content.Children.Add(MakeDlss5Text("✓ Verify the game renderer and choose the correct 32-bit or 64-bit files.", ResourceKeys.AccentGreenBrush));
        content.Children.Add(MakeDlss5Text("✓ Install or repair the selected rendering components and required runtime files.", ResourceKeys.AccentGreenBrush));
        content.Children.Add(MakeDlss5Text("✓ Apply the selected profile's settings and preserve your existing tuning during repair.", ResourceKeys.AccentGreenBrush));
        content.Children.Add(MakeDlss5Text("✓ Remove obsolete suite files, preserve backups, and verify the finished installation.", ResourceKeys.AccentGreenBrush));

        if (assessment.BlockingReasons.Count > 0)
        {
            content.Children.Add(MakeDlss5Heading("Needs attention"));
            foreach (var reason in assessment.BlockingReasons)
                content.Children.Add(MakeDlss5Text($"• {reason}", ResourceKeys.AccentRedBrush));
        }
        if (probe.InstallationIssues.Count > 0)
        {
            content.Children.Add(MakeDlss5Heading("Current installation problems"));
            foreach (var issue in probe.InstallationIssues)
                content.Children.Add(MakeDlss5Text("• " + issue, ResourceKeys.AccentRedBrush));
        }

        var missingRequirementsPanel = new StackPanel { Spacing = 6 };
        foreach (var architecture in probe.MissingRuntimeArchitectures)
        {
            var runtimeButton = new Button { Content = $"Get Microsoft Visual C++ runtime ({architecture})" };
            runtimeButton.Click += (_, _) => Process.Start(new ProcessStartInfo(Dlss5RuntimePrerequisites.DownloadUrl(architecture)) { UseShellExecute = true });
            content.Children.Add(runtimeButton);
        }
        content.Children.Add(missingRequirementsPanel);
        if (assessment.MissingRequirements.Count > 0)
        {
            missingRequirementsPanel.Children.Add(MakeDlss5Heading("Adas will also add"));
            foreach (var requirement in assessment.MissingRequirements.Distinct(StringComparer.OrdinalIgnoreCase))
                missingRequirementsPanel.Children.Add(MakeDlss5Text($"• {requirement}", ResourceKeys.AccentAmberBrush));
        }

        if (isInstalled)
        {
            var checkSetup = new Button
            {
                Content = "Check current setup",
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            checkSetup.Click += async (_, _) =>
            {
                var installedPath = Dlss5ComponentService.FindInstalledDeploymentPath(card.InstallPath)
                    ?? assessment.DeploymentPath
                    ?? card.InstallPath;
                var report = await Task.Run(() => Dlss5DiagnosticService.Diagnose(
                    installedPath,
                    installedMode,
                    assessment.Is64Bit));
                await ShowDlss5MessageAsync(report.HasProblems
                    ? "DLSS 5 needs repair"
                    : report.IsWorking ? "DLSS 5 processing reported" : "DLSS 5 is ready to test",
                    report.ToDisplayText());
            };
            content.Children.Add(checkSetup);
        }

        var confirmation = new CheckBox
        {
            Content = "This game will be used in single-player/offline mode only.",
            IsChecked = false,
        };
        content.Children.Add(confirmation);
        var confirmationHelp = MakeDlss5Text(
            assessment.CanInstall
                ? "Confirm offline use to enable automatic installation."
                : "Installation remains disabled until the compatibility problem is resolved.",
            assessment.CanInstall ? ResourceKeys.AccentAmberBrush : ResourceKeys.AccentRedBrush);
        content.Children.Add(confirmationHelp);

        var advancedProfiles = new StackPanel { Spacing = 8 };
        advancedProfiles.Children.Add(MakeDlss5Text(
            "Choose one rendering setup. Stable is the default where supported; beta and experimental options add newer features but may have game-specific issues."));
        var recommendedProfile = new RadioButton
        {
            GroupName = "Dlss5InstallProfile",
            Content = "Recommended (stable)",
            IsChecked = selectedProfile == Dlss5InstallProfile.MaximumQuality,
            IsEnabled = assessment.Mode != Dlss5DeploymentMode.Dx10Feeder
                        && !(assessment.Mode is Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.Dx10ViaDxvkFeeder
                                or Dlss5DeploymentMode.Dx9ViaDxvkFeeder
                             && !assessment.Is64Bit),
        };
        var experimentalProfile = new RadioButton
        {
            GroupName = "Dlss5InstallProfile",
            Content = "ShortFuse unified — September 2 build (experimental)",
            IsChecked = selectedProfile == Dlss5InstallProfile.ExperimentalUnified,
            IsEnabled = assessment.Is64Bit && assessment.Mode != Dlss5DeploymentMode.NativeVulkan,
        };
        var betaProfile = new RadioButton
        {
            GroupName = "Dlss5InstallProfile",
            Content = $"Feeder {Dlss5ComponentService.BundledFeederBetaVersion} (beta)",
            IsChecked = selectedProfile == Dlss5InstallProfile.LatestFeederBeta,
            IsEnabled = Dlss5CompatibilityService.IsFeederMode(assessment.Mode),
        };
        var aioProfile = new RadioButton
        {
            GroupName = "Dlss5InstallProfile",
            Content = $"Standalone AIO {Dlss5ComponentService.AioVersion} — NR + DLAA/upscaling + optional frame generation (experimental)",
            IsChecked = selectedProfile == Dlss5InstallProfile.StandaloneAio,
            IsEnabled = Dlss5ComponentService.SupportsAio(assessment.Mode, assessment.Is64Bit),
        };
        var openGlBridgeProfile = new RadioButton
        {
            GroupName = "Dlss5InstallProfile",
            Content = $"OpenGL Bridge {Dlss5ComponentService.OpenGlBridgeVersion} — native 64-bit OpenGL DLAA (experimental)",
            IsChecked = selectedProfile == Dlss5InstallProfile.OpenGlBridge,
            IsEnabled = Dlss5ComponentService.SupportsOpenGlBridge(assessment.Mode, assessment.Is64Bit),
        };
        advancedProfiles.Children.Add(recommendedProfile);
        advancedProfiles.Children.Add(experimentalProfile);
        advancedProfiles.Children.Add(betaProfile);
        advancedProfiles.Children.Add(aioProfile);
        advancedProfiles.Children.Add(openGlBridgeProfile);
        var optiNrProfile = new RadioButton
        {
            GroupName = "Dlss5InstallProfile", Content = $"OptiScaler DLSS-NR {Dlss5ComponentService.OptiScalerNrVersion} (experimental; native DLSS DX11/DX12/Vulkan)",
            IsChecked = selectedProfile == Dlss5InstallProfile.OptiScalerNeuralRendering,
            IsEnabled = Dlss5ComponentService.SupportsOptiScalerNr(assessment.Mode, assessment.Is64Bit, false),
        };
        var splitProfile = new RadioButton
        {
            GroupName = "Dlss5InstallProfile", Content = "NR before upscaling (experimental OptiScaler fork; native DLSS DX12)",
            IsChecked = selectedProfile == Dlss5InstallProfile.OptiScalerNrBeforeSr,
            IsEnabled = Dlss5ComponentService.SupportsOptiScalerNr(assessment.Mode, assessment.Is64Bit, true),
        };
        advancedProfiles.Children.Add(optiNrProfile);
        advancedProfiles.Children.Add(splitProfile);
        advancedProfiles.Children.Add(MakeDlss5Text("Disabled options do not match this game's renderer or architecture. Standard OptiScaler NR requires native DLSS and a 64-bit DX11, DX12 or Vulkan game; the split fork remains DX12 only."));
        var profileSummary = MakeDlss5Text("");
        content.Children.Add(profileSummary);

        // ── À-la-carte slot #1: Neural consumer (RenoDX) ──────────────────────
        // Manual mode exposes each combined choice. The profile seeds a Recommended default;
        // any value can be forced, and the label states compatibility (never blocked).
        content.Children.Add(MakeDlss5Heading("Neural consumer"));
        var dfc = App.Services.GetRequiredService<DeepFriedChickenService>();
        var consumerCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                "Recommended",
                "RenoDX v4.55 — feeder-pinned, classic engine",
                "RenoDX v4.70 — native / latest engine",
                "Deep Fried Chicken — import your own (alpha)",
            },
            SelectedIndex = 0,
        };
        var consumerLabel = MakeDlss5Text("");
        content.Children.Add(consumerCombo);
        content.Children.Add(consumerLabel);
        void UpdateConsumerLabel()
        {
            if (consumerCombo.SelectedIndex == 3)
            {
                if (dfc.IsImported)
                {
                    consumerLabel.Text = $"✓ Deep Fried Chicken {dfc.ImportedVersion} imported — replaces the RenoDX consumer wherever it deploys. Keep Frame Generation OFF (this alpha still black-screens with FG in some games). Licence: personal / non-commercial; Adas deploys your unmodified copy and never redistributes it.";
                    consumerLabel.Foreground = UIFactory.Brush(ResourceKeys.AccentGreenBrush);
                }
                else
                {
                    var requiredFiles = string.Join(", ", DeepFriedChickenService.RequiredFiles);
                    consumerLabel.Text = $"Deep Fried Chicken can't be bundled — its licence forbids anyone, Adas included, from hosting, mirroring or redistributing it, so you supply the author's own official release. Pick that zip (or its extracted folder) once and Adas caches your unmodified copy and deploys it as the neural consumer for any game. A valid release contains {requiredFiles}; get it only from the author's official distribution and verify its integrity (the release ships SHA256SUMS.txt) before importing.";
                    consumerLabel.Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush);
                }
                return;
            }
            var recommended = Dlss5ComponentService.GetCompatibilityPlan(assessment.Mode, assessment.Is64Bit, selectedProfile).RenoDxPackage;
            var chosen = consumerCombo.SelectedIndex switch
            {
                1 => Dlss5RenoDxPackage.Feeder455,
                2 => Dlss5RenoDxPackage.Native470,
                _ => recommended,
            };
            var badDriver = DriverAtLeast(_dlssPresetService.DriverVersionString, 616, 64);
            var isFeederRoute = Dlss5CompatibilityService.IsFeederMode(assessment.Mode);
            string label; string brushKey;
            if (chosen == Dlss5RenoDxPackage.Native470 && badDriver)
            {
                brushKey = ResourceKeys.AccentAmberBrush;
                label = $"⚠ Known issue: RenoDX v4.70 on driver {_dlssPresetService.DriverVersionString} faults in D3D12Core (black screen — NR runs but shows nothing). Not blocked, but v4.55 is the measured-good build here.";
            }
            else if (chosen == Dlss5RenoDxPackage.Native470 && isFeederRoute)
            {
                brushKey = ResourceKeys.AccentAmberBrush;
                label = "⚠ v4.70 is the native-route consumer; feeder routes are validated against the pinned v4.55. Usually fine on older drivers, but v4.55 is the safe pairing.";
            }
            else
            {
                brushKey = ResourceKeys.AccentGreenBrush;
                var chosenName = chosen == Dlss5RenoDxPackage.Feeder455 ? "v4.55" : chosen == Dlss5RenoDxPackage.Native470 ? "v4.70" : "the profile's built-in consumer";
                var recName = recommended == Dlss5RenoDxPackage.Feeder455 ? "v4.55" : recommended == Dlss5RenoDxPackage.Native470 ? "v4.70" : "profile consumer";
                label = consumerCombo.SelectedIndex == 0
                    ? $"✓ Compatible — Recommended for this route resolves to {recName}."
                    : $"✓ Compatible — forcing {chosenName}.";
            }
            consumerLabel.Text = label;
            consumerLabel.Foreground = UIFactory.Brush(brushKey);
        }
        consumerCombo.SelectionChanged += async (_, _) =>
        {
            if (consumerCombo.SelectedIndex == 3 && !dfc.IsImported)
            {
                if (!await ImportDeepFriedChickenAsync(dfc))
                    consumerCombo.SelectedIndex = 0;
            }
            UpdateConsumerLabel();
        };

        void UpdateProfileSummary()
        {
            selectedProfile = openGlBridgeProfile.IsChecked == true ? Dlss5InstallProfile.OpenGlBridge
                : splitProfile.IsChecked == true ? Dlss5InstallProfile.OptiScalerNrBeforeSr
                : optiNrProfile.IsChecked == true ? Dlss5InstallProfile.OptiScalerNeuralRendering
                : aioProfile.IsChecked == true ? Dlss5InstallProfile.StandaloneAio : betaProfile.IsChecked == true
                ? Dlss5InstallProfile.LatestFeederBeta
                : experimentalProfile.IsChecked == true
                    ? Dlss5InstallProfile.ExperimentalUnified
                    : Dlss5InstallProfile.MaximumQuality;
            var plan = Dlss5ComponentService.GetCompatibilityPlan(assessment.Mode, assessment.Is64Bit, selectedProfile);
            profileSummary.Text = selectedProfile switch
            {
                Dlss5InstallProfile.OptiScalerNeuralRendering or Dlss5InstallProfile.OptiScalerNrBeforeSr
                    => "Selected: experimental OptiScaler neural rendering. Keep the game's own DLSS ON. Version 0.2 adds hybrid color composition, live exposure, frame hold and optional model supersampling; DX11 is configured through its D3D11-on-12 DLSS path. Press Insert for controls. Driver 616.56+ is required. Apply switches the current pipeline automatically and saves its visual settings.",
                Dlss5InstallProfile.StandaloneAio => $"Selected: standalone AIO {Dlss5ComponentService.AioVersion}. Downloads three verified files once, then reuses the cache. NR starts on; frame generation starts off. Disable the game's own DLSS, frame generation and antialiasing. Native resolution uses DLAA; a smaller game backbuffer enables upscaling.\n\nApply switches pipelines and preserves each profile's visual settings. Ada asks before cleaning up conflicts or changing a shared Vulkan route, then does the removal itself. Vulkan needs an installed 64-bit ReShade layer. DX9/DX11 guidance and frame pacing remain experimental.",
                Dlss5InstallProfile.OpenGlBridge => $"Selected: {plan.ProfileName}. This is the dedicated 64-bit OpenGL path: ReShade loads as opengl32.dll and the bridge supplies a DLAA presentation path without Feeder.",
                Dlss5InstallProfile.MaximumQuality => $"Selected automatically: {plan.ProfileName}. Stable components are kept separate and only one transport route is installed.",
                Dlss5InstallProfile.LatestFeederBeta => $"Selected: {plan.ProfileName}. This test build adds native 32-bit DirectX 10, current Smooth Motion synchronization, matched protocol-v7 x86 hosting, an in-game host panel, FSR 1 expand-back, Vulkan/DXVK fixes, crash diagnostics, and the upstream verifier. It requires one matched beta set and is not the stable default.",
                _ => $"Selected: {plan.ProfileName}. This combined build has broader direct API support but may flicker, black-screen, or crash in games that work with the recommended profile.",
            };
            profileSummary.Foreground = UIFactory.Brush(selectedProfile == Dlss5InstallProfile.MaximumQuality
                ? ResourceKeys.AccentGreenBrush
                : ResourceKeys.AccentAmberBrush);
            missingRequirementsPanel.Visibility = selectedProfile is Dlss5InstallProfile.StandaloneAio or Dlss5InstallProfile.OpenGlBridge
                || Dlss5ComponentService.IsOptiScalerNrProfile(selectedProfile) ? Visibility.Collapsed : Visibility.Visible;
            UpdateConsumerLabel();
        }
        recommendedProfile.Checked += (_, _) => UpdateProfileSummary();
        experimentalProfile.Checked += (_, _) => UpdateProfileSummary();
        betaProfile.Checked += (_, _) => UpdateProfileSummary();
        aioProfile.Checked += (_, _) => UpdateProfileSummary();
        openGlBridgeProfile.Checked += (_, _) => UpdateProfileSummary();
        optiNrProfile.Checked += (_, _) => UpdateProfileSummary();
        splitProfile.Checked += (_, _) => UpdateProfileSummary();
        UpdateProfileSummary();
        advancedProfiles.Children.Add(MakeDlss5Text($"Renderer target: {assessment.DeploymentPath ?? "Not resolved"}", ResourceKeys.TextTertiaryBrush));

        var alternateTools = new StackPanel { Spacing = 8 };
        var oneClickButton = new Button
        {
            Content = $"Open OneClick {Dlss5ComponentService.OneClickVersion} for this game",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = assessment.Mode is Dlss5DeploymentMode.NativeDirectX11 or Dlss5DeploymentMode.NativeDirectX12
                or Dlss5DeploymentMode.Dx11Feeder or Dlss5DeploymentMode.Dx12Feeder,
        };
        oneClickButton.Click += async (_, _) =>
        {
            oneClickButton.IsEnabled = false;
            try
            {
                if (Dlss5ComponentService.LoadRecord(assessment.DeploymentPath!) != null)
                    throw new InvalidOperationException("Remove the Adas-managed DLSS 5 suite first. OneClick is an external installer and must not be mixed with a tracked Adas pipeline.");
                await components.LaunchOneClickAsync(assessment.DeploymentPath!);
                await ShowDlss5MessageAsync("OneClick opened", "OneClick is a separate upstream installer. It received this game's exact renderer folder and owns any files it changes.");
            }
            catch (Exception ex) { await ShowDlss5MessageAsync("OneClick could not start", ex.Message); }
            finally { oneClickButton.IsEnabled = true; }
        };
        alternateTools.Children.Add(oneClickButton);

        var mainlineOptiButton = new Button
        {
            Content = "Install mainline OptiScaler beta/nightly (separate from stable)",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = assessment.Is64Bit && assessment.Mode is Dlss5DeploymentMode.NativeDirectX11
                or Dlss5DeploymentMode.NativeDirectX12 or Dlss5DeploymentMode.NativeVulkan,
        };
        mainlineOptiButton.Click += async (_, _) =>
        {
            mainlineOptiButton.IsEnabled = false;
            try
            {
                if (Dlss5ComponentService.LoadRecord(assessment.DeploymentPath!) != null)
                    throw new InvalidOperationException("Remove the current Adas-managed DLSS 5 suite first, then install the separate mainline OptiScaler beta pipeline.");
                var target = new GameCardViewModel
                {
                    GameName = card.GameName,
                    InstallPath = assessment.DeploymentPath!,
                    Source = card.Source,
                    Is32Bit = false,
                    GraphicsApi = assessment.Mode == Dlss5DeploymentMode.NativeVulkan
                        ? GraphicsApiType.Vulkan
                        : assessment.Mode == Dlss5DeploymentMode.NativeDirectX11
                            ? GraphicsApiType.DirectX11
                            : GraphicsApiType.DirectX12,
                };
                var result = await _optiScalerService.InstallAsync(target, variant: "Nightly");
                await ShowDlss5MessageAsync(result == null ? "OptiScaler beta unavailable" : "OptiScaler beta installed",
                    result == null ? "The latest mainline beta/nightly package could not be staged." : "The mainline beta/nightly was installed through its own OptiScaler route and settings, separate from stable.");
                PopulateDetailPanel(card);
            }
            catch (Exception ex) { await ShowDlss5MessageAsync("OptiScaler beta installation failed", ex.Message); }
            finally { mainlineOptiButton.IsEnabled = true; }
        };
        alternateTools.Children.Add(mainlineOptiButton);
        alternateTools.Children.Add(MakeDlss5Text("These are separate upstream methods. OneClick manages its own files; mainline OptiScaler beta uses a separate nightly cache and configuration from stable.", ResourceKeys.TextTertiaryBrush));
        advancedProfiles.Children.Add(alternateTools);
        if (!assessment.Is64Bit)
            advancedProfiles.Children.Add(MakeDlss5Text(
                assessment.Mode is Dlss5DeploymentMode.VulkanFeeder or Dlss5DeploymentMode.Dx10ViaDxvkFeeder
                    or Dlss5DeploymentMode.Dx9ViaDxvkFeeder
                    ? $"32-bit Vulkan requires matched Feeder {Dlss5ComponentService.BundledFeederBetaVersion} addon32 and host64 files, so Adas selected that packaged route automatically."
                    : assessment.Mode == Dlss5DeploymentMode.Dx10Feeder
                        ? $"32-bit DirectX 10 requires Feeder {Dlss5ComponentService.BundledFeederBetaVersion}. Adas selected its native relay automatically; no DXVK or Vulkan layer is needed."
                    : "The unified add-on is 64-bit only. Adas will use Feeder's host64 route for this game.",
                ResourceKeys.TextTertiaryBrush));
        if (assessment.Mode is Dlss5DeploymentMode.Dx10ViaDxvkFeeder or Dlss5DeploymentMode.Dx9ViaDxvkFeeder
            && !VulkanLayerService.IsRunningAsAdmin())
            advancedProfiles.Children.Add(MakeDlss5Text(
                "This DXVK recovery route needs a one-time administrator run so Adas can register the matching Vulkan ReShade layer. Reopen Adas as administrator before installing it.",
                ResourceKeys.AccentAmberBrush));
        content.Children.Add(new Expander
        {
            Header = "Rendering profile — stable, beta and experimental",
            Content = advancedProfiles,
            IsExpanded = false,
        });

        var recoveryTools = new StackPanel { Spacing = 8 };
        var importStatus = MakeDlss5Text("");
        var importAio = new Button
        {
            Content = $"Import all three AIO {Dlss5ComponentService.AioVersion} release files from a folder",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        importAio.Click += async (_, _) =>
        {
            var selected = await PickFolderAsync();
            if (selected == null) return;
            importAio.IsEnabled = false;
            try
            {
                await Task.Run(() => components.ImportAioFolderAsync(selected));
                importStatus.Text = "All three AIO files verified and cached. Select the standalone AIO profile to install them.";
            }
            catch (Exception ex) { importStatus.Text = ex.Message; }
            finally { importAio.IsEnabled = true; }
        };
        recoveryTools.Children.Add(importAio);
        var importComponents = new Button
        {
            Content = "Import local DLSS 5 component folder",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        importComponents.Click += async (_, _) =>
        {
            var selected = await PickFolderAsync();
            if (selected == null) return;
            try
            {
                var files = components.ImportLocalComponentFolder(selected);
                importStatus.Text = $"Imported and verified {files.Count} local component files.";
            }
            catch (Exception ex) { importStatus.Text = ex.Message; }
        };
        recoveryTools.Children.Add(importComponents);
        var importRuntime = new Button
        {
            Content = "Import local Streamline runtime ZIP",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = assessment.DeploymentPath != null,
        };
        importRuntime.Click += async (_, _) =>
        {
            var selected = await PickZipAsync();
            if (selected == null || assessment.DeploymentPath == null) return;
            try
            {
                var files = components.ImportLocalRuntimeFolder(
                    selected,
                    assessment.DeploymentPath,
                    hosted64Only: !assessment.Is64Bit);
                importStatus.Text = $"Imported and synchronized {files.Count} runtime files.";
            }
            catch (Exception ex) { importStatus.Text = ex.Message; }
        };
        recoveryTools.Children.Add(importRuntime);
        var importReShade = new Button
        {
            Content = "Import local ReShade full add-on installer",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        importReShade.Click += async (_, _) =>
        {
            var selected = await PickExecutableAsync();
            if (selected == null) return;
            try
            {
                var imported = components.ImportReShadeAddonInstaller(selected);
                importStatus.Text = $"Imported and verified {imported}.";
            }
            catch (Exception ex) { importStatus.Text = ex.Message; }
        };
        recoveryTools.Children.Add(importReShade);
        var runDlssNrRepairRequested = false;
        ContentDialog? dialog = null;
        var repairDlssNr = new Button
        {
            Content = "Advanced: restore an official signed DLSS-NR runtime",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = assessment.CanInstall,
        };
        repairDlssNr.Click += (_, _) =>
        {
            runDlssNrRepairRequested = true;
            dialog?.Hide();
        };
        recoveryTools.Children.Add(repairDlssNr);
        recoveryTools.Children.Add(importStatus);
        content.Children.Add(new Expander
        {
            Header = "Manual recovery tools",
            Content = recoveryTools,
            IsExpanded = false,
        });

        dialog = new ContentDialog
        {
            Title = installedMode == Dlss5DeploymentMode.None ? "Install DLSS 5" : "Repair DLSS 5",
            Content = new ScrollViewer { Content = content, MaxHeight = 570 },
            PrimaryButtonText = installedMode == Dlss5DeploymentMode.None ? "Install selected setup" : "Apply selected setup",
            CloseButtonText = "Cancel",
            IsPrimaryButtonEnabled = false,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        void UpdateSafetyConfirmation()
        {
            // A renderer/route was determined — allow the user to continue even when the automatic
            // check flagged a blocker (e.g. an ambiguous folder). They reconfirm the folder on proceed.
            var canOverride = assessment.CanInstall || Dlss5CompatibilityService.CanConfirmDeploymentPath(assessment);
            var enabled = canOverride && confirmation.IsChecked == true;
            dialog.IsPrimaryButtonEnabled = enabled;
            dialog.DefaultButton = enabled ? ContentDialogButton.Primary : ContentDialogButton.Close;
            confirmationHelp.Text = enabled
                ? (assessment.CanInstall
                    ? "Ready. Adas will handle the technical setup and verify it afterward."
                    : "Adas will ask you to confirm the exact game folder, then install anyway.")
                : canOverride
                    ? "Confirm offline use to continue."
                    : "No supported renderer was found for this game.";
            confirmationHelp.Foreground = UIFactory.Brush(enabled
                ? (assessment.CanInstall ? ResourceKeys.AccentGreenBrush : ResourceKeys.AccentAmberBrush)
                : ResourceKeys.AccentRedBrush);
        }
        confirmation.Checked += (_, _) => UpdateSafetyConfirmation();
        confirmation.Unchecked += (_, _) => UpdateSafetyConfirmation();
        UpdateSafetyConfirmation();
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        var result = await DialogService.ShowSafeAsync(dialog);

        if (runDlssNrRepairRequested)
        {
            await RunDlssNrRepairAsync(card, singlePlayerConfirmed: true);
            return;
        }
        if (result != ContentDialogResult.Primary) return;

        // If the automatic check flagged a blocker, let the user reconfirm the exact game folder
        // and install anyway instead of stopping.
        var manualForced = false;
        if (!assessment.CanInstall || string.IsNullOrWhiteSpace(assessment.DeploymentPath))
        {
            if (!Dlss5CompatibilityService.CanConfirmDeploymentPath(assessment))
            {
                await ShowDlss5MessageAsync("DLSS 5 installation stopped", string.Join("\n", assessment.BlockingReasons));
                return;
            }
            var suggested = !string.IsNullOrWhiteSpace(assessment.DeploymentPath)
                ? assessment.DeploymentPath
                : Dlss5CompatibilityService.ResolveDeploymentPath(card.InstallPath).Candidates.FirstOrDefault()
                  ?? card.InstallPath;
            var chosen = await ConfirmGameFolderAndPickAsync(card, assessment, suggested);
            if (string.IsNullOrWhiteSpace(chosen)) return;
            assessment = Dlss5CompatibilityService.ConfirmDeploymentPath(assessment, chosen);
            manualForced = true;
        }

        if (!manualForced)
        {
            var freshProbe = await Task.Run(() => ProbeDlss5(compatibility, card));
            var freshAssessment = Dlss5CompatibilityService.Assess(freshProbe, singlePlayerConfirmed: true);
            if (!freshAssessment.CanInstall
                || freshAssessment.Mode != assessment.Mode
                || !string.Equals(freshAssessment.DeploymentPath, assessment.DeploymentPath, StringComparison.OrdinalIgnoreCase))
            {
                var reason = freshAssessment.BlockingReasons.Count > 0
                    ? string.Join("\n", freshAssessment.BlockingReasons)
                    : "The selected binary folder or recommended mode changed after review. Review the game again before installing.";
                await ShowDlss5MessageAsync("DLSS 5 installation stopped", reason);
                return;
            }
            assessment = freshAssessment;
        }

        var canWrite = await Task.Run(() =>
            FileSystemAccessService.CanWriteToDirectory(assessment.DeploymentPath!, out var writeError)
                ? (Allowed: true, Error: (string?)null)
                : (Allowed: false, Error: writeError));
        if (!canWrite.Allowed)
        {
            await _dialogService.ShowGameFolderAdminRequiredDialogAsync(
                card.GameName,
                assessment.DeploymentPath!,
                canWrite.Error);
            return;
        }

        Dlss5CleanupPlan cleanup;
        try { cleanup = await Task.Run(() => Dlss5ComponentService.GetCleanupPlan(assessment.DeploymentPath!, assessment.Mode, selectedProfile)); }
        catch (Exception ex) { await ShowDlss5MessageAsync("Cannot review conflicting components", ex.Message); return; }
        if (cleanup.RequiresConfirmation)
        {
            var explanation = "Adas will remove the previous components, keep recovery copies, and continue installing your selected setup. If the game is running, Adas will ask to close it automatically. No manual file deletion is needed.";
            if (cleanup.Files.Count > 0)
                explanation += "\n\nConflicting files to move into .adas\\preserved:\n• " + string.Join("\n• ", cleanup.Files.Select(file => Path.GetRelativePath(cleanup.Root, file.Path)));
            if (cleanup.SharedLayerReset)
                explanation += "\n\nVulkan uses a shared layer. Ada will finish removal before setting up the new route. If that setup fails, use Repair to continue; shared-layer changes cannot be automatically rolled back.";
            var confirmCleanup = new ContentDialog
            {
                Title = "Remove conflicts and continue?",
                Content = new ScrollViewer { Content = MakeDlss5Text(explanation), MaxHeight = 420 },
                PrimaryButtonText = "Remove conflicts and continue", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close, XamlRoot = Content.XamlRoot,
            };
            if (await DialogService.ShowSafeAsync(confirmCleanup) != ContentDialogResult.Primary) return;
        }

        var runningProcesses = await Task.Run(() => GameProcessService.FindRunningProcesses(card.InstallPath));
        if (runningProcesses.Count > 0)
        {
            var closeGame = new ContentDialog
            {
                Title = $"Close {card.GameName} and continue?",
                Content = MakeDlss5Text(
                    "Windows keeps active ReShade and DLSS add-ons locked while the game is running. Adas will close the game, wait for those files to be released, then continue the selected installation automatically."),
                PrimaryButtonText = "Close game and continue",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot,
            };
            if (await DialogService.ShowSafeAsync(closeGame) != ContentDialogResult.Primary) return;

            var stopErrors = await GameProcessService.StopProcessesAsync(runningProcesses);
            if (stopErrors.Count > 0)
            {
                await ShowDlss5MessageAsync(
                    $"Could not close {card.GameName}",
                    "Adas did not change the installation:\n\n• " + string.Join("\n• ", stopErrors));
                return;
            }
            await Task.Delay(250);
        }

        var progressText = new TextBlock { Text = "Preparing...", TextWrapping = TextWrapping.Wrap };
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 4 };
        var progressPanel = new StackPanel { Spacing = 10 };
        progressPanel.Children.Add(progressText);
        progressPanel.Children.Add(progressBar);
        var progressDialog = new ContentDialog
        {
            Title = "Installing the DLSS 5 suite",
            Content = progressPanel,
            XamlRoot = Content.XamlRoot,
        };
        var progress = new Progress<(string message, double percent)>(update =>
        {
            progressText.Text = update.message;
            progressBar.Value = update.percent;
        });

        try
        {
            _ = progressDialog.ShowAsync();
            progressText.Text = "Moving the suite to the game renderer folder...";
            progressBar.Value = 2;
            var relocationErrors = await Task.Run(() =>
                components.RemoveOtherManagedDeployments(card.InstallPath, assessment.DeploymentPath!));
            if (relocationErrors.Count > 0)
                throw new IOException(
                    "Adas could not remove the previous launcher-folder deployment:\n" +
                    string.Join("\n", relocationErrors));

            var reShadeChannel = ViewModel.ResolveReShadeChannel(card.GameName, card.Source ?? "");
            var overrides = consumerCombo.SelectedIndex switch
            {
                1 => new Dlss5ManualOverrides(Dlss5RenoDxPackage.Feeder455),
                2 => new Dlss5ManualOverrides(Dlss5RenoDxPackage.Native470),
                3 when dfc.IsImported => new Dlss5ManualOverrides(DeepFriedChicken: true),
                _ => (Dlss5ManualOverrides?)null,
            };
            var installResult = await Task.Run(() => components.InstallAsync(
                card.GameName,
                assessment,
                progress,
                reShadeChannel: reShadeChannel,
                store: card.Source,
                profile: selectedProfile,
                cleanupApproval: cleanup,
                overrides: overrides));
            progressDialog.Hide();
            var auxInstaller = App.Services.GetRequiredService<IAuxInstallService>();
            var reShadeRecord = auxInstaller.FindRecord(card.GameName, assessment.DeploymentPath!, AuxInstallService.TypeReShade)
                ?? auxInstaller.FindRecord(card.GameName, assessment.DeploymentPath!, AuxInstallService.TypeReShadeNormal);
            if (reShadeRecord != null)
                MainViewModel.ApplyInstalledReShadeRecord(
                    card,
                    reShadeRecord,
                    AuxInstallService.ReadInstalledVersion(reShadeRecord.InstallPath, reShadeRecord.InstalledAs));
            PopulateDetailPanel(card);

            var resultText = installResult.Message;
            if (installResult.Warnings.Count > 0)
                resultText += "\n\nSetup notes:\n• " + string.Join("\n• ", installResult.Warnings.Distinct(StringComparer.OrdinalIgnoreCase));
            await ShowDlss5MessageAsync("DLSS 5 installation complete", resultText);
        }
        catch (Exception ex)
        {
            progressDialog.Hide();
            _crashReporter.Log($"[Dlss5ManageButton] {ex}");
            await ShowDlss5MessageAsync("DLSS 5 installation failed", ex.Message);
        }
    }

    private async void Dlss5CogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        var compatibility = App.Services.GetRequiredService<Dlss5CompatibilityService>();
        var components = App.Services.GetRequiredService<Dlss5ComponentService>();
        var assessment = Dlss5CompatibilityService.Assess(
            await Task.Run(() => ProbeDlss5(compatibility, card)), singlePlayerConfirmed: true);
        if (!assessment.CanInstall)
        {
            await ShowDlss5MessageAsync("DLSS 5 changes blocked", string.Join("\n", assessment.BlockingReasons));
            return;
        }
        if (!await ConfirmSinglePlayerUseAsync()) return;
        var path = assessment.DeploymentPath ?? card.InstallPath;
        var mode = components.GetInstalledMode(path);
        var installedRecord = Dlss5ComponentService.LoadRecord(path);
        if (Dlss5ComponentService.IsOptiScalerNrProfile(installedRecord?.Profile))
        {
            await ShowDlss5MessageAsync("OptiScaler neural rendering controls",
                "Launch the game with its own DLSS enabled and press Insert. Open DLSS Neural Rendering for the on/off switch, model, intensity, skin/structure and performance controls. The NR-before-SR profile also exposes its split-pipeline and RR supersampling controls there. These settings are live in OptiScaler, not the ReShade panel.");
            return;
        }
        if (installedRecord?.Profile == Dlss5InstallProfile.StandaloneAio)
        {
            await ShowAioSettingsAsync(path, mode);
            return;
        }
        if (installedRecord?.Profile == Dlss5InstallProfile.OpenGlBridge)
        {
            await ShowRenoDxSettingsAsync(path, installedRecord.Profile);
            return;
        }
        if (!Dlss5CompatibilityService.IsFeederMode(mode))
        {
            if (installedRecord?.Profile is Dlss5InstallProfile.MaximumQuality or Dlss5InstallProfile.ExperimentalUnified)
                await ShowRenoDxSettingsAsync(path, installedRecord.Profile);
            return;
        }

        var configPath = Dlss5ComponentService.GetConfigPath(path, mode);
        var installedProfile = installedRecord?.Profile ?? Dlss5InstallProfile.MaximumQuality;
        var current = new Dictionary<string, string>(Dlss5ComponentService.GetDefaults(mode, installedProfile), StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Dlss5ComponentService.ReadConfig(configPath)) current[pair.Key] = pair.Value;

        var panel = new StackPanel { Spacing = 12, MaxWidth = 660 };
        panel.Children.Add(MakeDlss5StatusCard(
            "DLSS 5 Feeder is configured automatically",
            "Adas manages the renderer mode, HDR and depth detection, motion-vector provider, startup timing, and safe compatibility values for this game.",
            success: true));

        var neuralEnabled = new ToggleSwitch
        {
            Header = "Neural rendering",
            OnContent = "On",
            OffContent = "Off",
            IsOn = !current.TryGetValue("enabled", out var enabledValue) || enabledValue != "0",
        };
        panel.Children.Add(neuralEnabled);

        var settingsRoot = assessment.Is64Bit ? path : Path.Combine(path, "host64");
        var reShadeIniPath = Path.Combine(settingsRoot, "ReShade.ini");
        var usesUnifiedSettings = installedProfile == Dlss5InstallProfile.ExperimentalUnified;
        var styleSection = usesUnifiedSettings ? "RENODX-DLSS" : "RenoDX.DLSS5";
        var styleKey = usesUnifiedSettings ? "DirectNeuralRenderingStyle" : "NRStyle";
        var currentStyle = 0;
        var reShadeSettings = IniTextDocument.Load(reShadeIniPath);
        if (reShadeSettings.TryGetValue(styleSection, styleKey, out var styleValue)
            && int.TryParse(styleValue.Text, out var parsedStyle)
            && parsedStyle is 0 or 1)
            currentStyle = parsedStyle;
        var appearance = new ComboBox
        {
            Header = "Appearance",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        appearance.Items.Add("Natural (recommended)");
        appearance.Items.Add("Cinematic");
        appearance.SelectedIndex = currentStyle;
        panel.Children.Add(appearance);
        panel.Children.Add(MakeDlss5Text(
            "Changes are applied to the game and, for 32-bit titles, to the managed 64-bit helper automatically.",
            ResourceKeys.TextTertiaryBrush));

        var editors = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
        var advanced = new StackPanel { Spacing = 9 };
        advanced.Children.Add(MakeDlss5Text(
            "These values are for diagnosis only. Restore recommended settings if a manual change causes instability."));
        foreach (var pair in current)
        {
            if (pair.Key.Equals("enabled", StringComparison.OrdinalIgnoreCase))
                continue;
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock
            {
                Text = Dlss5ConfigLabel(pair.Key),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            };
            var editor = new TextBox { Text = pair.Value, MinWidth = 220 };
            ToolTipService.SetToolTip(editor, Dlss5ConfigDescription(mode, pair.Key));
            Grid.SetColumn(editor, 1);
            row.Children.Add(label);
            row.Children.Add(editor);
            advanced.Children.Add(row);
            editors[pair.Key] = editor;
        }

        var restoreDefaults = new Button
        {
            Content = "Restore recommended settings",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        restoreDefaults.Click += (_, _) =>
        {
            var defaults = Dlss5ComponentService.GetDefaults(mode, installedProfile);
            foreach (var editor in editors)
                if (defaults.TryGetValue(editor.Key, out var value)) editor.Value.Text = value;
            neuralEnabled.IsOn = true;
            appearance.SelectedIndex = 0;
        };
        advanced.Children.Add(restoreDefaults);

        var importStatus = MakeDlss5Text("");
        var importRuntime = new Button
        {
            Content = "Import local RenoDX / Streamline runtime folder",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        importRuntime.Click += async (_, _) =>
        {
            var selected = await PickFolderAsync();
            if (selected == null) return;
            try
            {
                var files = components.ImportLocalRuntimeFolder(
                    selected,
                    path,
                    hosted64Only: File.Exists(Path.Combine(path, Dlss5ComponentService.FeederAddon32)));
                importStatus.Text = files.Count == 0
                    ? "No recognized runtime files were found."
                    : $"Imported {files.Count} recognized runtime files.";
            }
            catch (Exception ex) { importStatus.Text = ex.Message; }
        };
        advanced.Children.Add(importRuntime);

        var importRuntimeZip = new Button
        {
            Content = "Import local Streamline runtime ZIP",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        importRuntimeZip.Click += async (_, _) =>
        {
            var selected = await PickZipAsync();
            if (selected == null) return;
            try
            {
                var files = components.ImportLocalRuntimeFolder(
                    selected,
                    path,
                    hosted64Only: File.Exists(Path.Combine(path, Dlss5ComponentService.FeederAddon32)));
                importStatus.Text = files.Count == 0
                    ? "No recognized runtime files were found."
                    : $"Imported {files.Count} recognized runtime files.";
            }
            catch (Exception ex) { importStatus.Text = ex.Message; }
        };
        advanced.Children.Add(importRuntimeZip);

        var importReShade = new Button
        {
            Content = "Import local ReShade full add-on installer",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        importReShade.Click += async (_, _) =>
        {
            var selected = await PickExecutableAsync();
            if (selected == null) return;
            try
            {
                var imported = components.ImportReShadeAddonInstaller(selected);
                importStatus.Text = $"Imported and verified {imported}.";
            }
            catch (Exception ex) { importStatus.Text = ex.Message; }
        };
        advanced.Children.Add(importReShade);

        var openLog = new Button
        {
            Content = "Open diagnostic log",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = File.Exists(Dlss5ComponentService.GetLogPath(path, mode)),
        };
        openLog.Click += (_, _) =>
        {
            var logPath = Dlss5ComponentService.GetLogPath(path, mode);
            if (File.Exists(logPath)) Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
        };
        advanced.Children.Add(openLog);

        var checkSetup = new Button
        {
            Content = "Check setup and explain problems",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        checkSetup.Click += async (_, _) =>
        {
            var report = await Task.Run(() => Dlss5DiagnosticService.Diagnose(path, mode, assessment.Is64Bit));
            importStatus.Text = report.ToDisplayText();
        };
        advanced.Children.Add(checkSetup);

        var runDlssNrRepairRequested = false;
        ContentDialog? dialog = null;
        var repairDlssNr = new Button
        {
            Content = "Advanced: restore an official signed DLSS-NR runtime",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        repairDlssNr.Click += (_, _) =>
        {
            runDlssNrRepairRequested = true;
            dialog?.Hide();
        };
        advanced.Children.Add(repairDlssNr);
        advanced.Children.Add(importStatus);
        panel.Children.Add(new Expander
        {
            Header = "Advanced troubleshooting",
            Content = advanced,
            IsExpanded = false,
        });

        dialog = new ContentDialog
        {
            Title = "DLSS 5 settings",
            Content = new ScrollViewer { Content = panel, MaxHeight = 620 },
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        var result = await DialogService.ShowSafeAsync(dialog);
        if (runDlssNrRepairRequested)
        {
            await RunDlssNrRepairAsync(card, singlePlayerConfirmed: true);
            return;
        }
        if (result == ContentDialogResult.Primary)
        {
            var fresh = Dlss5CompatibilityService.Assess(
                await Task.Run(() => ProbeDlss5(compatibility, card)), singlePlayerConfirmed: true);
            if (!fresh.CanInstall
                || !string.Equals(fresh.DeploymentPath, path, StringComparison.OrdinalIgnoreCase)
                || components.GetInstalledMode(path) != mode)
            {
                await ShowDlss5MessageAsync("Configuration change stopped",
                    fresh.BlockingReasons.Count > 0
                        ? string.Join("\n", fresh.BlockingReasons)
                        : "The deployment folder or installed mode changed while the editor was open.");
                return;
            }
            var updated = new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase)
            {
                ["enabled"] = neuralEnabled.IsOn ? "1" : "0",
            };
            foreach (var editor in editors) updated[editor.Key] = editor.Value.Text;
            Dlss5ComponentService.WriteConfig(configPath, updated);

            var ini = IniTextDocument.Load(reShadeIniPath);
            ini.SetValue(styleSection, styleKey, appearance.SelectedIndex == 1 ? "1" : "0");
            if (usesUnifiedSettings)
            {
                ini.SetValue(styleSection, "DirectNeuralRenderingEnabled", neuralEnabled.IsOn ? "1" : "0");
                ini.SetValue(styleSection, "OptionsMode", "0");
            }
            else
            {
                ini.SetValue(styleSection, "NeuralUplift", neuralEnabled.IsOn ? "1" : "0");
            }
            ini.Save(reShadeIniPath);
            await ShowDlss5MessageAsync(
                "DLSS 5 settings applied",
                neuralEnabled.IsOn
                    ? $"Neural rendering is on with the {(appearance.SelectedIndex == 1 ? "Cinematic" : "Natural")} appearance. Adas kept the remaining compatibility settings automatic."
                    : "Neural rendering is off. Adas kept the installed files and automatic compatibility settings in place.");
        }
    }

    private async void Dlss5UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        var components = App.Services.GetRequiredService<Dlss5ComponentService>();
        var path = Dlss5ComponentService.FindInstalledDeploymentPath(card.InstallPath) ?? card.InstallPath;
        var runningProcesses = await Task.Run(() => GameProcessService.FindRunningProcesses(card.InstallPath));
        var runningText = runningProcesses.Count == 0
            ? ""
            : $"\n\n{card.GameName} is currently running. Adas will close it first so Windows releases the loaded add-on files, then continue removal automatically.";

        var result = await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = runningProcesses.Count == 0
                ? "Remove DLSS 5 suite?"
                : $"Close {card.GameName} and remove DLSS 5?",
            Content = MakeDlss5Text(
                "Adas will remove suite-managed Feeder and RenoDX DLSS files, restore tracked originals, and preserve files that were modified after installation." + runningText),
            PrimaryButtonText = runningProcesses.Count == 0 ? "Remove" : "Close game and remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        });
        if (result != ContentDialogResult.Primary) return;

        if (runningProcesses.Count > 0)
        {
            var stopErrors = await GameProcessService.StopProcessesAsync(runningProcesses);
            if (stopErrors.Count > 0)
            {
                await ShowDlss5MessageAsync(
                    $"Could not close {card.GameName}",
                    "Adas did not change the installation:\n\n• " + string.Join("\n• ", stopErrors));
                return;
            }

            await Task.Delay(250);
        }

        var errors = await Task.Run(() => components.Uninstall(path));
        PopulateDetailPanel(card);
        await ShowDlss5MessageAsync(
            errors.Count == 0 ? "DLSS 5 suite removed" : "DLSS 5 removal needs attention",
            errors.Count == 0
                ? "Suite-managed files were removed and tracked originals were restored."
                : "Adas kept the recovery record so removal can be retried:\n\n• " + string.Join("\n• ", errors));
    }

    private async Task RunDlssNrRepairAsync(GameCardViewModel card, bool singlePlayerConfirmed = false)
    {
        if (!singlePlayerConfirmed && !await ConfirmSinglePlayerUseAsync()) return;
        var compatibility = App.Services.GetRequiredService<Dlss5CompatibilityService>();
        var assessment = Dlss5CompatibilityService.Assess(
            await Task.Run(() => ProbeDlss5(compatibility, card)), singlePlayerConfirmed: true);
        if (!assessment.CanInstall || string.IsNullOrWhiteSpace(assessment.DeploymentPath))
        {
            await ShowDlss5MessageAsync("DLSSNR repair blocked", string.Join("\n", assessment.BlockingReasons));
            return;
        }

        var repair = App.Services.GetRequiredService<DlssNrRepairService>();
        var source = await PickFolderAsync();
        if (source == null) return;

        DlssNrRepairPlan plan;
        try
        {
            plan = await Task.Run(() => repair.CreatePlan(
                source,
                assessment.DeploymentPath,
                recurse: true,
                deployIfAddonPresent: true));
        }
        catch (Exception ex)
        {
            await ShowDlss5MessageAsync("Source DLL was not accepted", ex.Message);
            return;
        }

        var preview = new StackPanel { Spacing = 8, MaxWidth = 680 };
        preview.Children.Add(MakeDlss5Text($"Verified source: {plan.SourcePath}"));
        preview.Children.Add(MakeDlss5Text($"Exact build: {DlssNrRepairService.KnownGoodVersion}\nSHA-256: {DlssNrRepairService.KnownGoodSha256}"));
        if (plan.Actions.Count == 0)
            preview.Children.Add(MakeDlss5Text("No invalid or missing target DLLs require repair."));
        else
            foreach (var action in plan.Actions)
                preview.Children.Add(MakeDlss5Text($"• {action.Kind}: {action.TargetPath}\n  {action.Reason}", ResourceKeys.AccentAmberBrush));
        preview.Children.Add(MakeDlss5Text($"Valid NVIDIA-signed files left unchanged: {plan.UnchangedFiles.Count}"));

        var previewDialog = new ContentDialog
        {
            Title = "DLSSNR repair preview — no changes yet",
            Content = new ScrollViewer { Content = preview, MaxHeight = 580 },
            PrimaryButtonText = "Apply verified repairs",
            CloseButtonText = "Cancel",
            IsPrimaryButtonEnabled = plan.ChangeCount > 0,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        previewDialog.Resources["ContentDialogMaxWidth"] = 760.0;
        if (await DialogService.ShowSafeAsync(previewDialog) != ContentDialogResult.Primary) return;

        var freshAssessment = Dlss5CompatibilityService.Assess(
            await Task.Run(() => ProbeDlss5(compatibility, card)), singlePlayerConfirmed: true);
        if (!freshAssessment.CanInstall
            || !string.Equals(freshAssessment.DeploymentPath, assessment.DeploymentPath, StringComparison.OrdinalIgnoreCase))
        {
            await ShowDlss5MessageAsync("DLSSNR repair stopped",
                freshAssessment.BlockingReasons.Count > 0
                    ? string.Join("\n", freshAssessment.BlockingReasons)
                    : "The target folder changed after the preview. Create a new repair preview before applying changes.");
            return;
        }

        IReadOnlyList<DlssNrRepairResult> results;
        try
        {
            results = await Task.Run(() => repair.Execute(plan));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.RunDlssNrRepairAsync] Repair stopped for '{card.GameName}' — {ex}");
            await ShowDlss5MessageAsync(
                "DLSSNR repair stopped",
                $"No success was reported because the repair could not safely continue. {ex.Message}\n\nCreate a new repair preview and try again.");
            return;
        }

        var succeeded = results.Count(value => value.Succeeded);
        var text = $"{succeeded} of {results.Count} repair actions succeeded.";
        foreach (var item in results)
            text += $"\n\n{(item.Succeeded ? "✓" : "✕")} {item.TargetPath}\n{item.Message}"
                + (item.BackupPath == null ? "" : $"\nBackup: {item.BackupPath}");
        await ShowDlss5MessageAsync("DLSSNR repair complete", text);
        PopulateDetailPanel(card);
    }

    private async Task<bool> ConfirmSinglePlayerUseAsync()
    {
        var result = await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = "Confirm offline use",
            Content = MakeDlss5Text(
                "Continue only if this game will be used in single-player/offline mode. Anti-cheat or detected multiplayer evidence cannot be overridden."),
            PrimaryButtonText = "Confirm single-player / offline",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        });
        return result == ContentDialogResult.Primary;
    }

    private async Task<string?> PickExecutableAsync()
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
            picker.FileTypeFilter.Add(".exe");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.PickExecutableAsync] {ex.Message}");
            return null;
        }
    }

    private async Task<string?> PickZipAsync()
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
            picker.FileTypeFilter.Add(".zip");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.PickZipAsync] {ex.Message}");
            return null;
        }
    }

    private async Task ShowDlss5MessageAsync(string title, string message)
    {
        await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer { Content = MakeDlss5Text(message), MaxHeight = 560 },
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
        });
    }

    private static TextBlock MakeDlss5Heading(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock MakeDlss5Text(string text, string brushKey = ResourceKeys.TextSecondaryBrush) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = UIFactory.Brush(brushKey),
        TextWrapping = TextWrapping.Wrap,
    };

    private static Border MakeDlss5StatusCard(string heading, string detail, bool success)
    {
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(success ? ResourceKeys.AccentGreenBrush : ResourceKeys.AccentAmberBrush),
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(MakeDlss5Text(detail));
        return new Border
        {
            Background = UIFactory.Brush(success ? ResourceKeys.AccentGreenBgBrush : ResourceKeys.AccentAmberBgBrush),
            BorderBrush = UIFactory.Brush(success ? ResourceKeys.AccentGreenBorderBrush : ResourceKeys.AccentAmberDimBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Child = content,
        };
    }

    private static string Dlss5ConfigLabel(string key) => key switch
    {
        "mode" => "Renderer mode",
        "hdr" => "HDR detection",
        "depth_inverted" => "Depth direction",
        "flags" => "DLSS flags",
        "reset_every" => "Reset history",
        "warmup_rebuild" => "Warm-up rebuild",
        "rebuild" => "Manual rebuild",
        "log_frames" => "Detailed log frames",
        "create_delay" => "Startup delay",
        "preset" => "DLSS render preset",
        "host_window" => "64-bit helper window",
        "work_resolution" => "Work resolution (%)",
        "gpu_timeout_ms" => "GPU timeout (ms)",
        "work_upscale" => "Expand-back filter",
        "work_sharpness" => "Expand-back sharpness",
        "mv_scale_x" => "Motion scale X",
        "mv_scale_y" => "Motion scale Y",
        _ => key.Replace('_', ' '),
    };

    private static string Dlss5ConfigDescription(Dlss5DeploymentMode mode, string key)
    {
        return key switch
        {
            "enabled" => "0 disables the Feeder; 1 enables it.",
            "mode" => "0 inert, 1 transport test, 2 full DLSS path.",
            "hdr" => "-1 auto, 0 force SDR, 1 force HDR.",
            "depth_inverted" => "-1 follows ReShade, 0/1 forces depth orientation.",
            "flags" => "Raw DLSS feature creation flags; -1 uses automatic flags.",
            "reset_every" => "1 discards temporal history every frame for diagnosis.",
            "warmup_rebuild" => "Recreate once after this many delivered frames; 0 disables.",
            "rebuild" => "Change the number to request a one-time manual feature rebuild.",
            "log_frames" => "Number of initial frames logged in detail.",
            "create_delay" => "Frames to wait before creating or rebuilding the DLSS feature after runtime initialization.",
            "preset" => "DLSS render-preset hint. 0 leaves the default; legacy CNN and transformer presets can change temporal behavior.",
            "host_window" => "32-bit games: 1 shows the x64 helper window; 0 hides it after setup.",
            "work_resolution" => "Supported DirectX 11 routes: 50-100% work resolution. Lower values trade image detail for performance. The beta also supports 32-bit DirectX 11; unsupported routes remain at 100%.",
            "work_upscale" => "Latest Feeder D3D11: 0 bilinear, 1 FSR 1, 2 experimental synthetic-jitter SR (can shimmer and cost as much as native). This is not the game's DLSS Quality mode.",
            "work_sharpness" => "Latest Feeder: RCAS sharpness from 0 to 1 for expand-back filters. Upstream default is 0.3.",
            "gpu_timeout_ms" => "Latest Feeder: GPU wait timeout in milliseconds (100-60000). Three consecutive failed frames stop the feed instead of one slow frame disabling the session.",
            "mv_scale_x" or "mv_scale_y" => "Extra motion-vector multiplier. Sign/debug view also exist in the shader UI.",
            _ => "Advanced Feeder setting.",
        };
    }
}
