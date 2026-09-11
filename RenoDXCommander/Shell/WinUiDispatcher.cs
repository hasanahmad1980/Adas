using RenoDXCommander.Abstractions;

namespace RenoDXCommander.Services;

/// <summary>
/// WinUI implementation of <see cref="IUiDispatcher"/>: wraps a real
/// <see cref="Microsoft.UI.Dispatching.DispatcherQueue"/>. Method shape mirrors WinUI's own
/// <c>TryEnqueue(Action)</c> so every existing call site compiles unchanged once the property
/// type is switched to the abstraction. Retired when the Avalonia shell takes over.
/// </summary>
internal sealed class WinUiDispatcher : IUiDispatcher
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dq;

    public WinUiDispatcher(Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        => _dq = dispatcherQueue;

    /// <summary>Wraps <see cref="Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread"/>; null if the current thread has no queue.</summary>
    public static WinUiDispatcher? ForCurrentThread()
    {
        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        return dq is null ? null : new WinUiDispatcher(dq);
    }

    public bool TryEnqueue(Action work) => _dq.TryEnqueue(() => work());

    public bool HasThreadAccess => _dq.HasThreadAccess;
}
