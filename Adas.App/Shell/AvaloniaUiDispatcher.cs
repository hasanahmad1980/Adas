using Avalonia.Threading;
using RenoDXCommander.Abstractions;

namespace Adas.App.Shell;

/// <summary>
/// Avalonia implementation of <see cref="IUiDispatcher"/> over <see cref="Dispatcher.UIThread"/>.
/// Mirrors the WinUI adapter the engine previously used, so all ViewModel/service call sites work
/// unchanged.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public static readonly AvaloniaUiDispatcher Instance = new();

    public bool TryEnqueue(Action work)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            work();
        }
        else
        {
            Dispatcher.UIThread.Post(work);
        }
        return true;
    }

    public bool HasThreadAccess => Dispatcher.UIThread.CheckAccess();
}
