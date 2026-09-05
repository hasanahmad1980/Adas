using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private async Task ShowRenoDxSettingsAsync(string root, Dlss5InstallProfile profile)
    {
        var unified = profile == Dlss5InstallProfile.ExperimentalUnified;
        var section = unified ? "RENODX-DLSS" : "RenoDX.DLSS5";
        var enabledKey = unified ? "DirectNeuralRenderingEnabled" : "NeuralUplift";
        var styleKey = unified ? "DirectNeuralRenderingStyle" : "NRStyle";
        var ini = IniTextDocument.Load(Path.Combine(root, "ReShade.ini"));

        var enabled = !ini.TryGetValue(section, enabledKey, out var enabledValue)
            || enabledValue.Text != "0";
        var style = ini.TryGetValue(section, styleKey, out var styleValue)
            && int.TryParse(styleValue.Text, out var parsedStyle)
            ? parsedStyle
            : 0;
        var maximumStyle = unified ? 2 : 1;
        style = Math.Clamp(style, 0, maximumStyle);

        var panel = new StackPanel { Spacing = 12, MaxWidth = 560 };
        panel.Children.Add(MakeDlss5StatusCard(
            "Simple neural-rendering controls",
            "Adas saves these settings directly for this game. They remain enabled between launches; the full ReShade interface is optional.",
            success: true));
        var neuralEnabled = new ToggleSwitch
        {
            Header = "Neural rendering",
            OnContent = "On",
            OffContent = "Off",
            IsOn = enabled,
        };
        panel.Children.Add(neuralEnabled);
        var appearance = new ComboBox
        {
            Header = "Appearance",
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
        };
        appearance.Items.Add("Natural (recommended)");
        appearance.Items.Add("Cinematic");
        if (unified) appearance.Items.Add("Model C (experimental)");
        appearance.SelectedIndex = style;
        panel.Children.Add(appearance);
        panel.Children.Add(MakeDlss5Text(
            "Save once, then restart the game. If the add-on reports Waiting or the model does not initialise, use Repair automatically so Adas can verify the complete pipeline.",
            ResourceKeys.TextTertiaryBrush));

        var dialog = new ContentDialog
        {
            Title = unified ? "Experimental DLSS 5 settings" : "DLSS 5 settings",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary) return;

        try
        {
            await Task.Run(() => Dlss5ComponentService.SaveRenoDxUserSettings(
                root, profile, neuralEnabled.IsOn, appearance.SelectedIndex));
            await ShowDlss5MessageAsync(
                "DLSS 5 settings saved",
                neuralEnabled.IsOn
                    ? "Neural rendering will start enabled on the next launch."
                    : "Neural rendering will start disabled on the next launch.");
        }
        catch (Exception ex)
        {
            await ShowDlss5MessageAsync("DLSS 5 settings not saved", ex.Message);
        }
    }
}
