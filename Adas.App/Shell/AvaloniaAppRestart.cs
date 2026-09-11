using System.Diagnostics;
using Avalonia.Controls.ApplicationLifetimes;
using RenoDXCommander.Abstractions;

namespace Adas.App.Shell;

/// <summary>Avalonia implementation of <see cref="IAppRestart"/> used by the self-update subsystem.</summary>
public sealed class AvaloniaAppRestart : IAppRestart
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;

    public AvaloniaAppRestart(IClassicDesktopStyleApplicationLifetime lifetime) => _lifetime = lifetime;

    public void RestartApplication()
    {
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(exe))
        {
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); }
            catch { /* best-effort relaunch */ }
        }
        _lifetime.Shutdown();
    }
}
