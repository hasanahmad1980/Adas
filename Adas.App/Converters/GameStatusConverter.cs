using System.Globalization;
using Adas.App.Shell;
using Avalonia.Data.Converters;
using Avalonia.Media;
using RenoDXCommander.Models;

namespace Adas.App.Converters;

/// <summary>Maps <see cref="GameStatus"/> to a short human label.</summary>
public sealed class GameStatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is GameStatus s ? s switch
        {
            GameStatus.Installed => "Installed",
            GameStatus.UpdateAvailable => "Update available",
            GameStatus.Available => "Available",
            _ => "Not installed",
        } : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Colours a route row: green = installed/recommended, amber = available/experimental, red = unsupported.</summary>
public sealed class RouteStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is RouteOption r
            ? new SolidColorBrush(Color.Parse(r.Installed || r.Recommended ? "#3FB950" : r.Supported ? "#E3B341" : "#F85149"))
            : new SolidColorBrush(Color.Parse("#9BA6B4"));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps <see cref="GameStatus"/> to a status colour brush (green/amber/accent/grey).</summary>
public sealed class GameStatusColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is GameStatus s ? s switch
        {
            GameStatus.Installed => new SolidColorBrush(Color.Parse("#3FB950")),
            GameStatus.UpdateAvailable => new SolidColorBrush(Color.Parse("#E3B341")),
            GameStatus.Available => new SolidColorBrush(Color.Parse("#4C8BF5")),
            _ => new SolidColorBrush(Color.Parse("#6B7684")),
        } : new SolidColorBrush(Color.Parse("#6B7684"));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
