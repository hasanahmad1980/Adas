using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Adas.App.Converters;

/// <summary>
/// Loads a <see cref="Bitmap"/> from a local file path for binding to <c>Image.Source</c>.
/// Returns null (no image) when the path is empty or the file is missing/unreadable, so the
/// bound control can stay hidden until <c>GameArtworkService</c> has cached the art.
/// </summary>
public sealed class PathToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
        {
            try
            {
                if (File.Exists(path))
                    return new Bitmap(path);
            }
            catch { /* corrupt/locked file — render nothing */ }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
