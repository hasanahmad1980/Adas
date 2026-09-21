using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RenoDXCommander.Models;

namespace Adas.App.Shell;

/// <summary>
/// Lists the game folders found by scanning a user-picked directory, each with a checkbox (all checked by
/// default), and returns the confirmed <see cref="GameCandidate"/>s or <c>null</c> on cancel. Used by
/// "Add a specific folder…" so a user can point at a games-library root and pick which detected games to add.
/// Same shape as <see cref="AddonSelectionDialog"/>.
/// </summary>
public static class GameCandidateSelectionDialog
{
    public static Task<List<GameCandidate>?> ShowAsync(Window owner, IReadOnlyList<GameCandidate> candidates)
    {
        var boxes = new List<(GameCandidate Candidate, CheckBox Box)>();
        var list = new StackPanel { Spacing = 6 };

        foreach (var candidate in candidates)
        {
            var check = new CheckBox
            {
                Content = candidate.Name,
                IsChecked = true,
                Foreground = Brush("AdasTextPrimaryBrush", owner),
            };
            boxes.Add((candidate, check));
            list.Children.Add(check);
            list.Children.Add(new TextBlock
            {
                Text = candidate.Path,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Avalonia.Thickness(26, 0, 0, 4),
                Foreground = Brush("AdasTextSecondaryBrush", owner),
            });
        }

        var selectAll = new Button { Content = "Select all", MinWidth = 90 };
        var selectNone = new Button { Content = "Select none", MinWidth = 90 };
        var primary = new Button { Content = "Add selected", MinWidth = 120, Classes = { "accent" } };
        var close = new Button { Content = "Cancel", MinWidth = 90 };

        selectAll.Click += (_, _) => { foreach (var b in boxes) b.Box.IsChecked = true; };
        selectNone.Click += (_, _) => { foreach (var b in boxes) b.Box.IsChecked = false; };

        var count = candidates.Count;
        var root = new Border
        {
            Padding = new Avalonia.Thickness(20),
            Background = Brush("AdasSurfaceBrush", owner),
            Child = new StackPanel
            {
                Spacing = 10,
                MaxWidth = 620,
                Children =
                {
                    new TextBlock { Text = count == 1 ? "1 game found" : $"{count} games found",
                        FontSize = 17, FontWeight = FontWeight.SemiBold,
                        Foreground = Brush("AdasTextPrimaryBrush", owner) },
                    new TextBlock { Text = "Choose which folders to add to your library.",
                        FontSize = 13, TextWrapping = TextWrapping.Wrap,
                        Foreground = Brush("AdasTextSecondaryBrush", owner) },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { selectAll, selectNone },
                    },
                    new ScrollViewer { Content = list, MaxHeight = 420,
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
            Title = "Add games from folder",
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = Brush("AdasBackgroundBrush", owner),
        };

        List<GameCandidate>? result = null;
        primary.Click += (_, _) =>
        {
            result = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Candidate).ToList();
            dialog.Close();
        };
        close.Click += (_, _) => { result = null; dialog.Close(); };

        var tcs = new TaskCompletionSource<List<GameCandidate>?>();
        dialog.Closed += (_, _) => tcs.TrySetResult(result);
        _ = dialog.ShowDialog(owner);
        return tcs.Task;
    }

    private static IBrush Brush(string key, Window owner)
        => owner.TryFindResource(key, out var r) && r is IBrush b ? b : Brushes.Gray;
}
