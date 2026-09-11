namespace RenoDXCommander.Abstractions;

/// <summary>
/// Framework-neutral service-locator hook. The active UI shell assigns <see cref="Services"/> once,
/// immediately after building its DI container, so engine services in Adas.Core can resolve
/// late-bound dependencies without referencing a concrete WinUI/Avalonia <c>App</c> type.
/// Prefer constructor injection; this exists only for the handful of engine call sites that
/// resolve a service lazily to avoid a DI cycle.
/// </summary>
public static class AppServices
{
    /// <summary>The application's root service provider. Set once by the shell at startup.</summary>
    public static IServiceProvider Services { get; set; } = null!;
}
