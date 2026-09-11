using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.ViewModels;

/// <summary>
/// DXVK install, uninstall, update, and configuration command handlers.
/// Requirements: 10.1, 10.2, 10.3, 10.4, 9.3, 9.4, 22.1
/// </summary>
public partial class MainViewModel
{
    // ── DXVK Install ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Installs DXVK for the given game card.
    /// Shows a first-time warning dialog if not yet acknowledged this session.
    /// Sets <c>DxvkIsInstalling</c> during the operation to disable controls.
    /// </summary>
    /// <summary>Fixed body text for the first-run DXVK advanced-feature warning (rendered by the shell).</summary>
    public const string DxvkWarningMessage =
        "⚠ ADVANCED FEATURE — USE AT YOUR OWN RISK\n\n"
        + "DXVK is an unofficial DirectX-to-Vulkan translation layer.\n"
        + "No support will be provided if a game is not compatible.\n\n"
        + "WHO SHOULD USE THIS:\n"
        + "• Primarily benefits older DX8/DX9 games (e.g. FFXIV, Morrowind)\n"
        + "• Enables ReShade compute shaders on games that don't support them natively\n"
        + "• Can reduce CPU-bound stuttering in older titles\n\n"
        + "IMPORTANT WARNINGS:\n"
        + "• Anti-cheat games may ban players using DXVK\n"
        + "• Game overlays (Steam, NVIDIA, RTSS) may conflict or stop working\n"
        + "• Exclusive fullscreen is blocked — use borderless windowed\n"
        + "• First launch will be slow due to shader compilation (improves on subsequent runs)\n"
        + "• Some games may crash or have graphical glitches with DXVK\n\n"
        + "Do you want to continue?";

    public async Task InstallDxvkAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        // Check for manifest-driven install warning
        if (!await CheckInstallWarningAsync(card.GameName, "dxvk")) return;

        // ── DXVK warning (shown unless user has opted out via checkbox) ─────────
        if (ConfirmWithOptOutDialog is not null && !_settingsViewModel.DxvkWarningDismissed)
        {
            var (confirmed, dontShowAgain) = await ConfirmWithOptOutDialog("⚠ DXVK Warning", DxvkWarningMessage);
            if (!confirmed) return;

            if (dontShowAgain)
            {
                _settingsViewModel.DxvkWarningDismissed = true;
                SaveSettingsPublic();
            }
        }

        // ── Install ──────────────────────────────────────────────────────
        // Resolve the per-game DXVK variant and set it on the service before install
        var resolvedVariant = ResolveDxvkVariant(card.GameName, card.Source ?? "");
        var savedVariant = _dxvkService.SelectedVariant;
        _dxvkService.SelectedVariant = resolvedVariant;
        _dxvkService.LiliumPresetIndex = GetLiliumPreset(card.GameName, card.Source ?? "");

        card.DxvkIsInstalling = true;
        card.DxvkActionMessage = "Installing DXVK...";
        card.DxvkProgress = 0;
        try
        {
            // Ensure the resolved variant's staging is ready
            if (!_dxvkService.IsStagingReady)
                await _dxvkService.EnsureStagingAsync();

            await _dxvkService.InstallAsync(card,
                new Progress<(string message, double percent)>(p =>
                {
                    card.DxvkActionMessage = p.message;
                    card.DxvkProgress = p.percent;
                }));

            card.DxvkActionMessage = "✅ DXVK installed!";
            card.NotifyAll();
            card.FadeMessage(m => card.DxvkActionMessage = m, card.DxvkActionMessage);
            SaveLibrary();

            // Persist Vulkan rendering path if direct DX9 mode switched the game to Vulkan
            if (card.DxvkRecord?.InstalledDlls.Contains("d3d9.dll") == true && card.VulkanRenderingPath == "Vulkan")
                SetVulkanRenderingPath(card.GameName, "Vulkan", card.Source ?? "");
        }
        catch (Exception ex)
        {
            card.DxvkActionMessage = $"❌ Install failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallDxvk", ex, note: $"Game: {card.GameName}");
        }
        finally
        {
            card.DxvkIsInstalling = false;
            _dxvkService.SelectedVariant = savedVariant;
        }
    }

    // ── DXVK Uninstall ────────────────────────────────────────────────────────────

    /// <summary>
    /// Uninstalls DXVK from the given game card.
    /// Removes deployed DLLs, restores backups, and cleans up the tracking record.
    /// </summary>
    public void UninstallDxvk(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        card.DxvkIsInstalling = true;
        card.DxvkActionMessage = "Removing DXVK...";
        try
        {
            _dxvkService.Uninstall(card);
            
            // Clear persisted Vulkan rendering path — Lilium HDR uninstall resets to DirectX
            SetVulkanRenderingPath(card.GameName, "DirectX", card.Source ?? "");
            
            card.DxvkActionMessage = "✖ DXVK removed.";
            card.NotifyAll();
            card.FadeMessage(m => card.DxvkActionMessage = m, card.DxvkActionMessage);
            SaveLibrary();
        }
        catch (Exception ex)
        {
            card.DxvkActionMessage = $"❌ Uninstall failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UninstallDxvk", ex, note: $"Game: {card.GameName}");
        }
        finally
        {
            card.DxvkIsInstalling = false;
        }
    }

    // ── DXVK Update ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates DXVK for the given game card.
    /// Re-stages the latest release if needed and re-deploys DLLs.
    /// </summary>
    public async Task UpdateDxvkAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        card.DxvkIsInstalling = true;
        card.DxvkActionMessage = "Updating DXVK...";
        card.DxvkProgress = 0;
        try
        {
            // Resolve per-game variant and switch before update
            var resolvedVariant = ResolveDxvkVariant(card.GameName, card.Source ?? "");
            var savedVariant = _dxvkService.SelectedVariant;
            _dxvkService.SelectedVariant = resolvedVariant;

            await _dxvkService.EnsureStagingAsync(new Progress<(string message, double percent)>(p =>
            {
                card.DxvkActionMessage = p.message;
                card.DxvkProgress = p.percent;
            }));

            await _dxvkService.UpdateAsync(card,
                new Progress<(string message, double percent)>(p =>
                {
                    card.DxvkActionMessage = p.message;
                    card.DxvkProgress = p.percent;
                }));

            _dxvkService.SelectedVariant = savedVariant;

            card.DxvkActionMessage = "✅ DXVK updated!";
            card.DxvkStatus = GameStatus.Installed;
            card.NotifyAll();
            card.FadeMessage(m => card.DxvkActionMessage = m, card.DxvkActionMessage);
            SaveLibrary();
        }
        catch (Exception ex)
        {
            card.DxvkActionMessage = $"❌ Update failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UpdateDxvk", ex, note: $"Game: {card.GameName}");
        }
        finally
        {
            card.DxvkIsInstalling = false;
        }
    }

    // ── DXVK Copy dxvk.conf ───────────────────────────────────────────────────────

    /// <summary>
    /// Copies the dxvk.conf template to the game directory.
    /// </summary>
    public void CopyDxvkConf(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            _dxvkService.CopyConfToGame(card);
            card.DxvkActionMessage = "✅ dxvk.conf copied to game folder.";
            card.FadeMessage(m => card.DxvkActionMessage = m, card.DxvkActionMessage);
        }
        catch (Exception ex)
        {
            card.DxvkActionMessage = $"❌ {ex.Message}";
        }
    }

    // ── DXVK Toggle Handler ───────────────────────────────────────────────────────

    /// <summary>
    /// Handles the DxvkEnabled toggle change.
    /// When toggled ON, triggers the DXVK install flow.
    /// When toggled OFF, triggers the DXVK uninstall flow.
    /// </summary>
    public async Task HandleDxvkToggleAsync(GameCardViewModel card, bool enabled)
    {
        card.DxvkEnabled = enabled;

        if (enabled)
        {
            await InstallDxvkAsync(card);

            // If install failed (status didn't change to Installed), revert the toggle
            if (card.DxvkStatus != GameStatus.Installed && card.DxvkStatus != GameStatus.UpdateAvailable)
            {
                card.DxvkEnabled = false;
            }
        }
        else
        {
            UninstallDxvk(card);

            // Re-resolve Graphics API from manifest/user overrides after uninstall.
            // The DxvkService sets a hardcoded DX9 default for Lilium HDR uninstall,
            // but the proper value may differ based on manifest overrides.
            if (!string.IsNullOrEmpty(card.InstallPath))
            {
                card.DetectedApis = _DetectAllApisForCard(card.InstallPath, card.GameName, card.Source);
                card.IsDualApiGame = GraphicsApiDetector.IsDualApi(card.DetectedApis);
                card.GraphicsApi = DetectGraphicsApi(card.InstallPath, EngineType.Unknown, card.GameName, card.Source);
            }

            // Deploy shaders after ReShade is restored as DX proxy.
            // The DxvkService reinstalls ReShade but can't resolve shader packs,
            // so we deploy them here where we have access to the shader resolver.
            if (card.RsStatus == GameStatus.Installed && !string.IsNullOrEmpty(card.InstallPath))
            {
                DeployShadersForCard(card.GameName);
            }
        }

        SaveLibrary();

        // Rebuild the detail/overrides panel so the Update Inclusion section
        // picks up the new DxvkEnabled state immediately.
        card.NotifyAll();
    }
}
