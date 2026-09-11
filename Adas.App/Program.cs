using Avalonia;

namespace Adas.App;

internal static class Program
{
    // Avalonia entry point. Keep this minimal — do not call anything that touches
    // the UI framework before AppBuilder is fully constructed.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
