using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Immutable identity + request for one DLSS 5 operation (assess / preview / cleanup / install /
/// remove / repair / retry / recovery). Background discovery refreshes cards in place, so a
/// long-running operation must capture what it needs ONCE and never reread mutable card paths,
/// store keys, or the user's Graphics API choice while awaiting. Filesystem existence is still
/// revalidated immediately before any mutation — the snapshot fixes identity, not the disk.
/// </summary>
internal sealed record Dlss5GameOperation(
    string GameName,
    string InstallPath,
    string Source,
    bool Is32Bit,
    GraphicsApiType? ApiOverride = null)
{
    /// <summary>Captures the card's identity/paths. The Graphics API override is left unresolved
    /// (<see langword="null"/>); callers that honour the user's choice should use the overload below.</summary>
    public static Dlss5GameOperation Capture(GameCardViewModel card)
        => new(
            card.GameName,
            Path.GetFullPath(card.InstallPath),
            card.Source ?? "",
            card.Is32Bit);

    /// <summary>Captures the card's identity/paths together with the resolved Graphics API override,
    /// so the whole operation assesses and installs against one API choice even if the card changes.</summary>
    public static Dlss5GameOperation Capture(GameCardViewModel card, GraphicsApiType? apiOverride)
        => new(
            card.GameName,
            Path.GetFullPath(card.InstallPath),
            card.Source ?? "",
            card.Is32Bit,
            apiOverride);
}
