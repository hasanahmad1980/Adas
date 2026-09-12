using CommunityToolkit.Mvvm.ComponentModel;

namespace RenoDXCommander.ViewModels;

// Cover-art state: the Steam AppID this card resolves to, and the local path to its cached artwork.
public partial class GameCardViewModel
{
    /// <summary>Steam AppID for this title when known (from ACF/manifest); used to fetch store art.</summary>
    public int? SteamAppId { get; set; }

    /// <summary>Local file path to cached cover art (Steam library portrait), or null until fetched.</summary>
    [ObservableProperty] private string? _artworkImagePath;

    /// <summary>True once cover art has been downloaded and cached for this card.</summary>
    public bool HasArtwork => !string.IsNullOrEmpty(ArtworkImagePath);

    partial void OnArtworkImagePathChanged(string? value) => OnPropertyChanged(nameof(HasArtwork));
}
