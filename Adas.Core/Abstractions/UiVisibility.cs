namespace RenoDXCommander.Abstractions;

/// <summary>
/// Framework-neutral replacement for WinUI's <c>Microsoft.UI.Xaml.Visibility</c>. Member names and
/// order deliberately mirror the WinUI enum (<c>Visible = 0</c>, <c>Collapsed = 1</c>) so that every
/// existing <c>Visibility.Visible</c> / <c>Visibility.Collapsed</c> call site and every
/// <c>Visibility</c>-typed property in the ViewModels compiles unchanged behind a
/// <c>using Visibility = RenoDXCommander.Abstractions.UiVisibility;</c> alias.
/// The Avalonia shell binds these through a converter to <c>Control.IsVisible</c>.
/// </summary>
public enum UiVisibility
{
    Visible = 0,
    Collapsed = 1,
}
