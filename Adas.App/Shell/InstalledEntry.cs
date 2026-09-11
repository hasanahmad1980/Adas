namespace Adas.App.Shell;

/// <summary>One row in the installed-overview / history window: a game and the Adas components
/// currently deployed into it.</summary>
public sealed record InstalledEntry(string GameName, string Source, string Summary, string InstallPath);
