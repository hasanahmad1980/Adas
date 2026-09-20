using RenoDXCommander.ViewModels;

namespace Adas.App.Shell;

/// <summary>One row in the installed-overview / history window: a game and the Adas components
/// currently deployed into it. <see cref="Card"/> is carried so the page can run per-row
/// Repair/Remove maintenance against the same card the library uses.</summary>
public sealed record InstalledEntry(string GameName, string Source, string Summary, string InstallPath, GameCardViewModel? Card = null);
