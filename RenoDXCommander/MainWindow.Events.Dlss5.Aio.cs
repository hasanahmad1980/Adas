using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private async Task ShowAioSettingsAsync(string root, Dlss5DeploymentMode mode)
    {
        var path = Path.Combine(root, "ReShade.ini");
        var ini = IniTextDocument.Load(path);
        var controls = new Dictionary<string, Func<string>>();
        var panel = new StackPanel { Spacing = 12, MaxWidth = 600 };
        panel.Children.Add(MakeDlss5Text("NR changes the rendered appearance. DLAA/upscaling improves reconstruction. Frame generation adds synthetic frames and can cause uneven pacing. These are separate controls."));
        string Read(string key) => ini.TryGetValue(Dlss5ComponentService.AioSection, key, out var value)
            ? value.Text : Dlss5ComponentService.AioDefaults[key];
        void Toggle(string key, string label, bool enabled = true)
        {
            var control = new ToggleSwitch { Header = label, IsOn = Read(key) == "1", IsEnabled = enabled };
            panel.Children.Add(control);
            if (enabled) controls[key] = () => control.IsOn ? "1" : "0";
        }
        void Slider(string key, string label, double minimum, double maximum)
        {
            var control = new Slider
            {
                Header = label, Minimum = minimum, Maximum = maximum, StepFrequency = 0.05,
                Value = double.TryParse(Read(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 1,
            };
            panel.Children.Add(control);
            controls[key] = () => control.Value.ToString("0.##", CultureInfo.InvariantCulture);
        }
        Toggle("Enabled", "Enable the complete AIO pipeline");
        Toggle("NeuralRendering", "Neural rendering (NR)");
        var fgAvailable = File.Exists(Path.Combine(root, "nvngx_dlssg.dll"));
        Toggle("FrameGeneration", "Frame generation (experimental)", fgAvailable);
        if (!fgAvailable) panel.Children.Add(MakeDlss5Text("Frame generation needs nvngx_dlssg.dll. NR and DLAA/upscaling can still be used."));
        Slider("Intensity", "Neural rendering intensity", 0, 2);
        Slider("LocalTone", "Local tone strength", 0, 2);
        Slider("LocalStructure", "Detail / structure strength", 0, 2);
        Slider("SkinStructure", "Skin / character structure (−1 = automatic)", -1, 1);
        Toggle("ShowProxyFps", "Show AIO's FPS counter");
        Toggle("NrRejectionMask", "VORT rejection mask (experimental; may suppress NR)");
        Slider("NrRejectionStrength", "Rejection strength (0 = bypass)", 0, 1);
        panel.Children.Add(MakeDlss5Text($"Leave VORT guidance and rejection masking off for normal use; both can add substantial per-frame cost or suppress the neural effect. AIO {Dlss5ComponentService.AioVersion} uses buffered presentation to reduce waiting between frames, keeps DLSS Preset L for lower smearing, and shows performance timings in its ReShade panel."));
        var advanced = new StackPanel { Spacing = 8 };
        var early = new ToggleSwitch
        {
            Header = "Early output initialization (D3D12 compatibility only)",
            IsOn = Read("EarlyProxyInitialization") == "1",
            IsEnabled = mode is Dlss5DeploymentMode.NativeDirectX12 or Dlss5DeploymentMode.Dx12Feeder,
        };
        advanced.Children.Add(early);
        advanced.Children.Add(MakeDlss5Text("Leave this off unless a D3D12 game hangs while AIO creates its output. AIO 2.0 automatically chooses attached or detached presentation and window virtualization. If a prior session broke startup, hold F8 while launching to request its serialized safe-start path."));
        panel.Children.Add(new Expander { Header = "Compatibility", Content = advanced });
        if (early.IsEnabled) controls["EarlyProxyInitialization"] = () => early.IsOn ? "1" : "0";
        panel.Children.Add(MakeDlss5Text("Close the game before saving here, then restart it. For live adjustment use ReShade's Standalone DLSS-NR + SR panel. F10 compares original and processed presentation; it is not the NR-only switch. Leave the VORT motion and AIO Feed techniques unchecked in Home—the add-on runs them itself."));
        var closed = new CheckBox { Content = "The game is closed." };
        panel.Children.Add(closed);
        var dialog = new ContentDialog
        {
            Title = $"Standalone AIO {Dlss5ComponentService.AioVersion}",
            Content = new ScrollViewer { Content = panel, MaxHeight = 590 },
            PrimaryButtonText = "Save for next launch", CloseButtonText = "Cancel",
            IsPrimaryButtonEnabled = false, XamlRoot = Content.XamlRoot,
        };
        closed.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
        closed.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary) return;
        try
        {
            var settings = controls.ToDictionary(item => item.Key, item => item.Value());
            await Task.Run(() => Dlss5ComponentService.SaveAioUserSettings(root, settings));
            await ShowDlss5MessageAsync("AIO settings saved", "Restart the game to apply them. Verify the picture and the active mode in ReShade.");
        }
        catch (Exception ex) { await ShowDlss5MessageAsync("AIO settings not saved", ex.Message); }
    }
}
