using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

/// <summary>
/// Per-game settings dialog for the MFG Ada Unlock add-on. Reads and writes the
/// <c>[RenoDX.MFGUnlock]</c> section of the game's reshade.ini. Changes take effect
/// the next time the game is launched (or live via the in-game ReShade add-on panel).
/// </summary>
public static class MfgUnlockDialog
{
    public static async Task ShowAsync(
        MfgUnlockService service,
        string gameName,
        string installPath,
        XamlRoot xamlRoot)
    {
        var config = service.ReadConfig(installPath);

        var panel = new StackPanel { Spacing = 8, MinWidth = 320 };

        panel.Children.Add(new TextBlock
        {
            Text = "Unlocks DLSS Multi Frame Generation on RTX 40-series (Ada). Single-player only — " +
                   "may trigger anti-cheat online. Changes apply on next launch.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            Margin = new Thickness(0, 0, 0, 4),
        });

        var enabledCheck = new CheckBox
        {
            Content = "Enabled",
            IsChecked = config.Enabled != 0,
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        };
        ToolTipService.SetToolTip(enabledCheck, "Master switch for the MFG Ada Unlock add-on.");
        panel.Children.Add(enabledCheck);

        // ── Max frame count (2–6) ──
        panel.Children.Add(Label("Max Frame Count"));
        var maxItems = new[] { "2x", "3x", "4x", "5x", "6x" };
        var maxCombo = new ComboBox
        {
            ItemsSource = maxItems,
            SelectedIndex = Math.Clamp(config.MaxCount, 2, 6) - 2,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
        };
        ToolTipService.SetToolTip(maxCombo, "Upper limit for the frame-generation multiplier.");
        panel.Children.Add(maxCombo);

        // ── Force multiplier (0 = respect game, else 2–6) ──
        panel.Children.Add(Label("Force Multiplier"));
        var forceItems = new[] { "Respect game", "2x", "3x", "4x", "5x", "6x" };
        var forceCombo = new ComboBox
        {
            ItemsSource = forceItems,
            SelectedIndex = config.ForceMultiplier is >= 2 and <= 6 ? config.ForceMultiplier - 1 : 0,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
        };
        ToolTipService.SetToolTip(forceCombo, "Force a specific multiplier, or respect the game's own selection.");
        panel.Children.Add(forceCombo);

        var temporalCheck = Toggle("Temporal fix (interpolation correction)", config.TemporalFix != 0);
        var flipCheck = Toggle("Force flip metering off (needed for 3x+ on Ada)", config.ForceFlipMeteringOff != 0);
        var ceilingCheck = Toggle("Raise frame ceiling (older plugins up to 6x)", config.RaiseFrameCeiling != 0);
        var otaCheck = Toggle("Force OTA driver plugin set", config.ForceOTAPlugins != 0);
        panel.Children.Add(temporalCheck);
        panel.Children.Add(flipCheck);
        panel.Children.Add(ceilingCheck);
        panel.Children.Add(otaCheck);

        var dialog = new ContentDialog
        {
            Title = $"MFG Ada Unlock — {gameName}",
            Content = new ScrollViewer { Content = panel, MaxHeight = 460 },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var result = await DialogService.ShowSafeAsync(dialog);
        if (result != ContentDialogResult.Primary) return;

        config.Enabled = enabledCheck.IsChecked == true ? 1 : 0;
        config.MaxCount = maxCombo.SelectedIndex + 2;
        config.ForceMultiplier = forceCombo.SelectedIndex == 0 ? 0 : forceCombo.SelectedIndex + 1;
        config.TemporalFix = temporalCheck.IsChecked == true ? 1 : 0;
        config.ForceFlipMeteringOff = flipCheck.IsChecked == true ? 1 : 0;
        config.RaiseFrameCeiling = ceilingCheck.IsChecked == true ? 1 : 0;
        config.ForceOTAPlugins = otaCheck.IsChecked == true ? 1 : 0;

        service.WriteConfig(installPath, config);
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        Margin = new Thickness(0, 8, 0, 0),
    };

    private static CheckBox Toggle(string text, bool isChecked) => new()
    {
        Content = text,
        IsChecked = isChecked,
        FontSize = 12,
        Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
    };
}
