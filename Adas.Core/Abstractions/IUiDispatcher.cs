namespace RenoDXCommander.Abstractions;

/// <summary>
/// Framework-neutral replacement for WinUI's <c>Microsoft.UI.Dispatching.DispatcherQueue</c>.
/// The method name and signature deliberately mirror <c>DispatcherQueue.TryEnqueue(Action)</c> so
/// every existing <c>DispatcherQueue?.TryEnqueue(() =&gt; …)</c> call site in the ViewModels compiles
/// unchanged; only the property type and the concrete adapter differ per UI framework.
/// The WinUI shell wraps a real DispatcherQueue; the Avalonia shell wraps <c>Dispatcher.UIThread</c>;
/// tests can supply a synchronous inline implementation.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Marshals <paramref name="work"/> onto the UI thread. Returns false if it could not be queued.</summary>
    bool TryEnqueue(Action work);

    /// <summary>True when the caller is already on the UI thread (no marshaling needed).</summary>
    bool HasThreadAccess { get; }
}

/// <summary>
/// Neutral access point for the current thread's UI dispatcher. The active UI shell assigns
/// <see cref="CurrentThreadFactory"/> at startup (WinUI wraps DispatcherQueue.GetForCurrentThread,
/// Avalonia wraps Dispatcher.UIThread) so engine code can capture the UI dispatcher without
/// referencing a concrete UI framework.
/// </summary>
public static class UiDispatcher
{
    /// <summary>Set by the shell; returns an <see cref="IUiDispatcher"/> for the calling (UI) thread, or null.</summary>
    public static Func<IUiDispatcher?>? CurrentThreadFactory { get; set; }

    /// <summary>Captures the current thread's UI dispatcher via the shell-supplied factory, if any.</summary>
    public static IUiDispatcher? ForCurrentThread() => CurrentThreadFactory?.Invoke();
}

/// <summary>
/// Requests an application restart (used by the self-update subsystem after applying an update).
/// Implemented by the active UI shell.
/// </summary>
public interface IAppRestart
{
    /// <summary>Relaunch the application and terminate the current process.</summary>
    void RestartApplication();
}
