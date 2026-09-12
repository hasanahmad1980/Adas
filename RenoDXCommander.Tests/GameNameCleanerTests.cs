using System.Collections.Generic;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Covers the messy-folder-name → Steam-search cleaning and fuzzy matching that drives cover-art
/// resolution, plus emulator recognition. Pure logic — no network.
/// </summary>
public class GameNameCleanerTests
{
    [Theory]
    // Repack: dots-for-spaces + trailing scene group stripped.
    [InlineData("Assetto.Corsa.EVO-InsaneRamZes", "Assetto Corsa EVO")]
    // Edition noise removed.
    [InlineData("Call Of Duty 2 Collector's Edition", "Call Of Duty 2")]
    // Bracketed repacker + version noise removed.
    [InlineData("Cyberpunk 2077 [FitGirl Repack] v2.1", "Cyberpunk 2077")]
    // A clean name is left alone.
    [InlineData("Alien: Isolation", "Alien: Isolation")]
    public void Clean_strips_scene_and_edition_noise(string raw, string expected)
        => Assert.Equal(expected, GameNameCleaner.Clean(raw));

    [Fact]
    public void SearchCandidates_offers_a_shorter_fallback_for_long_names()
    {
        var candidates = GameNameCleaner.SearchCandidates("Assetto.Corsa.EVO-InsaneRamZes");
        Assert.Equal("Assetto Corsa EVO", candidates[0]);
        Assert.NotEmpty(candidates);
    }

    [Fact]
    public void BestMatch_picks_the_right_result_over_a_close_decoy()
    {
        var results = new List<(int, string)>
        {
            (1, "Call of Duty"),
            (2, "Call of Duty 2"),
            (3, "Call of Duty: Modern Warfare"),
        };
        Assert.Equal(2, GameNameCleaner.BestMatch("Call Of Duty 2 Collector's Edition", results));
    }

    [Fact]
    public void BestMatch_matches_repack_folder_name_to_store_title()
    {
        var results = new List<(int, string)> { (244210, "Assetto Corsa"), (805550, "Assetto Corsa Competizione") };
        Assert.NotNull(GameNameCleaner.BestMatch("Assetto.Corsa-CODEX", results));
    }

    [Fact]
    public void BestMatch_returns_null_when_nothing_is_close()
    {
        var results = new List<(int, string)> { (1, "Stardew Valley"), (2, "Terraria") };
        Assert.Null(GameNameCleaner.BestMatch("Assetto.Corsa-CODEX", results));
    }

    [Theory]
    [InlineData("RPCS3", true)]
    [InlineData("Dolphin-x64", true)]
    [InlineData("pcsx2-v1.7.0", true)]
    [InlineData("Alien Isolation", false)]
    public void TryGetEmulatorArtUrl_recognises_emulators(string raw, bool expected)
    {
        var matched = GameNameCleaner.TryGetEmulatorArtUrl(raw, out var url);
        Assert.Equal(expected, matched);
        if (expected) Assert.StartsWith("https://", url);
    }
}
