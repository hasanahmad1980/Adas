using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Immutable game identity used for one DLSS 5 operation. Background discovery refreshes cards
/// in place, so long-running dialogs must not keep rereading mutable card paths or store keys.
/// </summary>
internal sealed record Dlss5GameOperation(
    string GameName,
    string InstallPath,
    string Source,
    bool Is32Bit)
{
    public static Dlss5GameOperation Capture(GameCardViewModel card)
        => new(
            card.GameName,
            Path.GetFullPath(card.InstallPath),
            card.Source ?? "",
            card.Is32Bit);
}
