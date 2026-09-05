using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private async Task<bool> ChooseDlss5EmulatorRendererAsync(Dlss5EmulatorInstallation installation, string root)
    {
        var executable = new ComboBox { Header = "Emulator executable you launch", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var candidate in Dlss5EmulatorService.FindCandidates(root).Where(item => item.Profile.Name == installation.Profile.Name))
        {
            var item = new ComboBoxItem { Content = Path.GetRelativePath(root, candidate.Executable), Tag = candidate };
            executable.Items.Add(item);
            if (candidate.Executable == installation.Executable) executable.SelectedItem = item;
        }
        var choices = new ComboBox { Header = "Renderer selected inside your emulator", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var api in installation.Profile.Renderers)
            choices.Items.Add(new ComboBoxItem { Content = api.ToString().Replace("DirectX", "DirectX "), Tag = api });
        var saved = Dlss5EmulatorService.LoadRenderer(installation);
        choices.SelectedIndex = saved.HasValue ? Array.IndexOf(installation.Profile.Renderers, saved.Value) : 0;
        var content = new StackPanel { Spacing = 12, MaxWidth = 540 };
        if (executable.Items.Count > 1) content.Children.Add(executable);
        content.Children.Add(MakeDlss5Text(installation.Profile.Hint));
        content.Children.Add(choices);
        content.Children.Add(MakeDlss5Text("Choose the same renderer as the emulator. Adas remembers your choice and uses Feeder; it does not change the emulator's settings. Compatibility varies by game/core. Use offline games only."));
        var dialog = new ContentDialog
        {
            Title = $"DLSS setup for {installation.Profile.Name}", Content = content,
            PrimaryButtonText = "Continue", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot,
        };
        if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary) return false;
        try
        {
            var selected = (Dlss5EmulatorInstallation)((ComboBoxItem)executable.SelectedItem).Tag;
            Dlss5EmulatorService.SaveRenderer(selected, (GraphicsApiType)((ComboBoxItem)choices.SelectedItem).Tag);
            Dlss5EmulatorService.SaveExecutable(root, selected);
            return true;
        }
        catch (Exception ex) { await ShowDlss5MessageAsync("Could not save emulator renderer", ex.Message); return false; }
    }
}
