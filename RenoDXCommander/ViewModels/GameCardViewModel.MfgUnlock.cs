using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

// MFG Ada Unlock status, install state, and computed properties.
// Mirrors the DOF Fix component row pattern (GameCardViewModel.DofFix.cs).
public partial class GameCardViewModel
{
    // ── MFG Unlock observable properties ──────────────────────────────────────────
    [ObservableProperty] private GameStatus _mfgUnlockStatus = GameStatus.NotInstalled;
    [ObservableProperty] private string?    _mfgUnlockInstalledVersion;
    [ObservableProperty] private bool       _mfgUnlockIsInstalling;
    [ObservableProperty] private double     _mfgUnlockProgress;
    [ObservableProperty] private string     _mfgUnlockActionMessage = "";

    // ── MFG Unlock eligibility (set during card build) ─────────────────────────────
    // True only when the manifest/dev feature flag is on AND the detected GPU is RTX 40-series (Ada).
    public bool IsMfgUnlockEligible { get; set; }

    // ── MFG Unlock computed properties ────────────────────────────────────────────

    /// <summary>Row is visible only for eligible (RTX 40-series) machines.</summary>
    public Visibility MfgUnlockRowVisibility =>
        IsMfgUnlockEligible ? Visibility.Visible : Visibility.Collapsed;

    public string MfgUnlockActionLabel => MfgUnlockIsInstalling ? "Installing..."
        : MfgUnlockStatus == GameStatus.UpdateAvailable ? "⬆  Update MFG Unlock"
        : MfgUnlockStatus == GameStatus.Installed ? "↺  Reinstall MFG Unlock"
        : "⬇  Install MFG Unlock";

    public string MfgUnlockBtnBackground  => MfgUnlockStatus == GameStatus.UpdateAvailable ? "#201838" : "#182840";
    public string MfgUnlockBtnForeground  => MfgUnlockStatus == GameStatus.UpdateAvailable ? "#B898E8" : "#7AACDD";
    public string MfgUnlockBtnBorderBrush => MfgUnlockStatus == GameStatus.UpdateAvailable ? "#3A2860" : "#2A4468";

    public Visibility MfgUnlockProgressVisibility => MfgUnlockIsInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MfgUnlockDeleteVisibility   => MfgUnlockStatus == GameStatus.Installed || MfgUnlockStatus == GameStatus.UpdateAvailable
        ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MfgUnlockCogVisibility      => MfgUnlockStatus == GameStatus.Installed || MfgUnlockStatus == GameStatus.UpdateAvailable
        ? Visibility.Visible : Visibility.Collapsed;

    public string MfgUnlockStatusText => MfgUnlockIsInstalling ? "Installing…"
        : MfgUnlockStatus == GameStatus.UpdateAvailable ? "Update"
        : MfgUnlockStatus == GameStatus.Installed ? (MfgUnlockInstalledVersion ?? "Installed")
        : "Ready";
    public string MfgUnlockStatusColor => MfgUnlockIsInstalling ? "#D4A856"
        : MfgUnlockStatus == GameStatus.UpdateAvailable ? "#B898E8"
        : MfgUnlockStatus == GameStatus.Installed ? "#5ECB7D"
        : "#A0AABB";

    public bool IsMfgUnlockNotInstalling => !MfgUnlockIsInstalling;
    public bool IsMfgUnlockInstalled => MfgUnlockStatus == GameStatus.Installed || MfgUnlockStatus == GameStatus.UpdateAvailable;
    public bool MfgUnlockInstallEnabled => !MfgUnlockIsInstalling;

    public Visibility MfgUnlockMessageVisibility => string.IsNullOrEmpty(MfgUnlockActionMessage) ? Visibility.Collapsed : Visibility.Visible;

    // ── Targeted notification: MfgUnlockStatus changed ────────────────────────────
    private void NotifyMfgUnlockStatusDependents()
    {
        OnPropertyChanged(nameof(MfgUnlockActionLabel));
        OnPropertyChanged(nameof(MfgUnlockBtnBackground));
        OnPropertyChanged(nameof(MfgUnlockBtnForeground));
        OnPropertyChanged(nameof(MfgUnlockBtnBorderBrush));
        OnPropertyChanged(nameof(MfgUnlockDeleteVisibility));
        OnPropertyChanged(nameof(MfgUnlockCogVisibility));
        OnPropertyChanged(nameof(MfgUnlockStatusText));
        OnPropertyChanged(nameof(MfgUnlockStatusColor));
        OnPropertyChanged(nameof(IsMfgUnlockInstalled));
        OnPropertyChanged(nameof(MfgUnlockInstallEnabled));
    }

    // ── Targeted notification: MfgUnlockIsInstalling changed ──────────────────────
    private void NotifyMfgUnlockIsInstallingDependents()
    {
        OnPropertyChanged(nameof(MfgUnlockActionLabel));
        OnPropertyChanged(nameof(MfgUnlockProgressVisibility));
        OnPropertyChanged(nameof(IsMfgUnlockNotInstalling));
        OnPropertyChanged(nameof(MfgUnlockInstallEnabled));
        OnPropertyChanged(nameof(MfgUnlockStatusText));
        OnPropertyChanged(nameof(MfgUnlockStatusColor));
    }

    partial void OnMfgUnlockStatusChanged(GameStatus value) => NotifyMfgUnlockStatusDependents();
    partial void OnMfgUnlockIsInstallingChanged(bool value) => NotifyMfgUnlockIsInstallingDependents();
    partial void OnMfgUnlockInstalledVersionChanged(string? value) => OnPropertyChanged(nameof(MfgUnlockStatusText));
    partial void OnMfgUnlockActionMessageChanged(string value) => OnPropertyChanged(nameof(MfgUnlockMessageVisibility));
}
