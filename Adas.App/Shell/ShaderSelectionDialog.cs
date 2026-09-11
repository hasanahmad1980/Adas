using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RenoDXCommander.Services;

namespace Adas.App.Shell;

/// <summary>
/// RenoDX shader-pack picker — the Avalonia stand-in for the WinUI <c>ShaderPopupHelper</c>.
/// Lists every available pack grouped by category with a description and a checkbox, and returns
/// the confirmed pack IDs (dependency-expanded) or <c>null</c> on cancel. The engine consumes the
/// returned IDs as the selection to deploy. Advanced per-file exclusions / saved profiles from the
/// old WinUI popup are not yet ported — the primary select-and-deploy path is.
/// </summary>
public static class ShaderSelectionDialog
{
    public static Task<List<string>?> ShowAsync(Window owner, IShaderPackService service,
        IReadOnlyList<string>? current, bool global)
    {
        var selected = new HashSet<string>(current ?? Enumerable.Empty<string>(),
            System.StringComparer.OrdinalIgnoreCase);

        var boxes = new List<(string Id, CheckBox Box)>();
        var list = new StackPanel { Spacing = 4 };

        foreach (var group in service.AvailablePacks.GroupBy(p => p.Category))
        {
            list.Children.Add(new TextBlock
            {
                Text = group.Key.ToString(),
                FontWeight = FontWeight.SemiBold,
                Margin = new Avalonia.Thickness(0, 10, 0, 2),
                Foreground = Brush("AdasTextSecondaryBrush", owner),
            });

            foreach (var pack in group)
            {
                var check = new CheckBox
                {
                    Content = pack.DisplayName,
                    IsChecked = selected.Contains(pack.Id),
                    Foreground = Brush("AdasTextPrimaryBrush", owner),
                };
                boxes.Add((pack.Id, check));
                list.Children.Add(check);

                var desc = service.GetPackDescription(pack.Id);
                if (!string.IsNullOrWhiteSpace(desc))
                    list.Children.Add(new TextBlock
                    {
                        Text = desc,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 12,
                        Margin = new Avalonia.Thickness(26, 0, 0, 4),
                        Foreground = Brush("AdasTextSecondaryBrush", owner),
                    });
            }
        }

        var primary = new Button { Content = global ? "Deploy" : "Confirm", MinWidth = 110, Classes = { "accent" } };
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
                    new TextBlock { Text = "Select shader packs", FontSize = 17, FontWeight = FontWeight.SemiBold,
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
            Title = "Select shader packs",
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
            var ids = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Id);
            result = service.ExpandPackDependencies(ids).Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
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
