// MainViewModel.Artwork.cs -- background fetch of Steam cover art for library cards.

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Abstractions;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Fetches Steam store cover art for each card in the background and assigns
    /// <see cref="GameCardViewModel.ArtworkImagePath"/> on the UI thread as each one arrives.
    /// Idempotent and disk-cached, so it is safe to call after every card rebuild — cards that
    /// already have art (or whose name recently missed) are skipped cheaply.
    /// </summary>
    internal Task PopulateArtworkAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        var snapshot = cards.ToList();
        return Task.Run(async () =>
        {
            try
            {
                if (AppServices.Services?.GetService<IGameArtworkService>() is not { } svc)
                    return;

                var tasks = snapshot
                    .Where(c => string.IsNullOrEmpty(c.ArtworkImagePath) && !string.IsNullOrEmpty(c.GameName))
                    .Select(async card =>
                    {
                        try
                        {
                            var path = await svc
                                .GetArtworkPathAsync(card.GameName, card.SteamAppId, card.InstallPath, _manifest)
                                .ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(path))
                                DispatcherQueue?.TryEnqueue(() => card.ArtworkImagePath = path);
                        }
                        catch (Exception ex) { _crashReporter.Log($"[PopulateArtworkAsync] '{card.GameName}' — {ex.Message}"); }
                    });

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (Exception ex) { _crashReporter.Log($"[PopulateArtworkAsync] {ex.Message}"); }
        });
    }
}
