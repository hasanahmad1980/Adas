using Adas.App.Shell;
using Adas.App.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander;
using RenoDXCommander.Abstractions;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace Adas.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Framework-neutral crash hooks + UI-thread dispatcher factory (mirrors the old WinUI shell).
        CrashReporter.RegisterCore();
        UiDispatcher.CurrentThreadFactory = () => AvaloniaUiDispatcher.Instance;

        var services = new ServiceCollection();
        services.AddAdasEngine();
        Services = services.BuildServiceProvider();
        AppServices.Services = Services;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<MainViewModel>();
            var window = new MainWindow { DataContext = vm };
            DialogWiring.Attach(vm, window);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
