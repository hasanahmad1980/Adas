using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RenoDXCommander.Services;

namespace Adas.App.Shell;

/// <summary>
/// RenoDX add-on picker — lists every available addon pack with a checkbox and description, and
/// returns the confirmed package names or <c>null</c> on cancel. Used for the global enabled-addon
/// set (the WinUI Settings "RenoDX add-ons" selection). Same shape as <see cref="ShaderSelectionDialog"/>.
/// </summary>
public static class AddonSelectionDialog
{
    public static Task<List<string>?> ShowAsync(Window owner, IAddonPackService service,
        IReadOnlyList<string>? current)
    {
        var selected = new HashSet<string>(current ?? Enumerable.Empty<string>(),
            System.StringComparer.OrdinalIgnoreCase);

        var boxes = new List<(string Name, CheckBox Box)>();
        var list = new StackPanel { Spacing = 6 };

        foreach (var pack in service.AvailablePacks)
        {
            var check = new CheckBox
            {
                Content = pack.PackageName,
                IsChecked = selected.Contains(pack.PackageName),
                Foreground = Brush("AdasTextPrimaryBrush", owner),
            };
            boxes.Add((pack.PackageName, check));
            list.Children.Add(check);

            if (!string.IsNullOrWhiteSpace(pack.PackageDescription))
                list.Children.Add(new TextBlock
                {
                    Text = pack.PackageDescription,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Margin = new Avalonia.Thickness(26, 0, 0, 4),
                    Foreground = Brush("AdasTextSecondaryBrush", owner),
                });
        }

        var primary = new Button { Content = "Confirm", MinWidth = 110, Classes = { "accent" } };
        var close = new Button { Content = "Cancel", MinWidth = 90 };

        var root = new Border
        {
            Padding = new Avalonia.Thickness(20),
            Background = Brush("AdasSurfaceBrush", owner),
            Child = new StackPanel
            {
                Spacing = 10,
                MaxWidth = 560,
                Children =
                {
                    new TextBlock { Text = "RenoDX add-ons", FontSize = 17, FontWeight = FontWeight.SemiBold,
                        Foreground = Brush("AdasTextPrimaryBrush", owner) },
                    new ScrollViewer { Content = list, MaxHeight = 460,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Avalonia.Thickness(0, 12, 0, 0),
                        Children = { close, primary },
                    },
                },
            },
        };

        var dialog = new Window
        {
            Title = "RenoDX add-ons",
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = Brush("AdasBackgroundBrush", owner),
        };

        List<string>? result = null;
        primary.Click += (_, _) =>
        {
            result = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Name).ToList();
            dialog.Close();
        };
        close.Click += (_, _) => { result = null; dialog.Close(); };

        var tcs = new TaskCompletionSource<List<string>?>();
        dialog.Closed += (_, _) => tcs.TrySetResult(result);
        _ = dialog.ShowDialog(owner);
        return tcs.Task;
    }

    private static IBrush Brush(string key, Window owner)
        => owner.TryFindResource(key, out var r) && r is IBrush b ? b : Brushes.Gray;
}
