using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Adas.App.Shell;

/// <summary>
/// Lightweight modal confirmation dialogs — the Avalonia stand-in for the WinUI ContentDialogs the
/// engine used to build inline. Rendered as small owned windows; the engine only ever needs a
/// yes/no answer (optionally with a "don't show again" opt-out), which is what these return.
/// </summary>
public static class DialogHost
{
    /// <summary>Continue/Cancel confirmation. Returns true if the user chose to continue.</summary>
    public static async Task<bool> ConfirmAsync(Window owner, string title, string message,
        string primaryText = "Continue", string closeText = "Cancel")
    {
        var (confirmed, _) = await ShowAsync(owner, title, message, primaryText, closeText, showOptOut: false);
        return confirmed;
    }

    /// <summary>Warning with a "don't show again" checkbox. Returns (confirmed, dontShowAgain).</summary>
    public static Task<(bool confirmed, bool dontShowAgain)> ConfirmWithOptOutAsync(Window owner,
        string title, string message, string primaryText = "Continue", string closeText = "Cancel")
        => ShowAsync(owner, title, message, primaryText, closeText, showOptOut: true);

    private static Task<(bool confirmed, bool dontShowAgain)> ShowAsync(Window owner, string title,
        string message, string primaryText, string closeText, bool showOptOut)
    {
        bool result = false;
        var optOut = new CheckBox
        {
            Content = "Don't show this warning again",
            IsVisible = showOptOut,
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
            Foreground = Brush("AdasTextSecondaryBrush", owner),
        };

        var body = new SelectableTextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = Brush("AdasTextPrimaryBrush", owner),
        };

        var primary = new Button { Content = primaryText, MinWidth = 110, Classes = { "accent" } };
        var close = new Button { Content = closeText, MinWidth = 90 };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 16, 0, 0),
            Children = { close, primary },
        };

        var root = new Border
        {
            Padding = new Avalonia.Thickness(20),
            Background = Brush("AdasSurfaceBrush", owner),
            Child = new StackPanel
            {
                Spacing = 10,
                MaxWidth = 520,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold,
                        Foreground = Brush("AdasTextPrimaryBrush", owner) },
                    new ScrollViewer { Content = body, MaxHeight = 380,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
                    optOut,
                    buttons,
                },
            },
        };

        var dialog = new Window
        {
            Title = title,
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = Brush("AdasBackgroundBrush", owner),
        };

        primary.Click += (_, _) => { result = true; dialog.Close(); };
        close.Click += (_, _) => { result = false; dialog.Close(); };

        var tcs = new TaskCompletionSource<(bool, bool)>();
        dialog.Closed += (_, _) => tcs.TrySetResult((result, optOut.IsChecked == true));
        _ = dialog.ShowDialog(owner);
        return tcs.Task;
    }

    private static IBrush Brush(string key, Window owner)
        => owner.TryFindResource(key, out var r) && r is IBrush b ? b : Brushes.Gray;
}
