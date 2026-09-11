using Avalonia.Controls;
using RenoDXCommander.ViewModels;

namespace Adas.App.Shell;

/// <summary>
/// Connects the <see cref="MainViewModel"/> shell-callback seams (dialogs, UI dispatcher) to the
/// Avalonia window. This is the Avalonia equivalent of the wiring the WinUI MainWindow did in its
/// constructor.
/// </summary>
public static class DialogWiring
{
    public static void Attach(MainViewModel vm, Window window)
    {
        vm.SetDispatcher(AvaloniaUiDispatcher.Instance);

        vm.ConfirmContinueDialog = (title, message) =>
            DialogHost.ConfirmAsync(window, title, message);

        vm.ConfirmWithOptOutDialog = (title, message) =>
            DialogHost.ConfirmWithOptOutAsync(window, title, message);

        vm.ConfirmForeignDxgiOverwrite = (_, message) =>
            DialogHost.ConfirmAsync(window, "Existing dxgi.dll found", message, "Overwrite", "Cancel");

        vm.ShowVulkanAdminRequiredDialog = () =>
            DialogHost.ConfirmAsync(window, "Administrator required",
                "Installing the Vulkan layer requires running Adas as administrator. Please relaunch as admin and try again.",
                "OK", "Close");

        vm.ShowVulkanLayerWarningDialog = () =>
            DialogHost.ConfirmAsync(window, "Global Vulkan layer",
                "This installs a global Vulkan layer that affects all Vulkan applications on this system. Continue?");
    }
}
