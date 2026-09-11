using System.Globalization;
using Avalonia.Data.Converters;
using RenoDXCommander.Abstractions;

namespace Adas.App.Converters;

/// <summary>
/// Binds the engine's framework-neutral <see cref="UiVisibility"/> to an Avalonia
/// <c>Control.IsVisible</c> bool (Visible → true, Collapsed → false).
/// </summary>
public sealed class UiVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is UiVisibility v ? v == UiVisibility.Visible : value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? UiVisibility.Visible : UiVisibility.Collapsed;
}

/// <summary>Inverts a bool — for "IsEnabled = not IsBusy" style bindings.</summary>
public sealed class BoolNegationConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
