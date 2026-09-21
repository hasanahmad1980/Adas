using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Detects installed games from various launchers and identifies game engines.
/// </summary>
public interface IGameDetectionService
{
    List<DetectedGame> FindSteamGames();

    List<DetectedGame> FindGogGames();

    List<DetectedGame> FindEpicGames();

    List<DetectedGame> FindEaGames();

    List<DetectedGame> FindXboxGames();

    List<DetectedGame> FindUbisoftGames();

    List<DetectedGame> FindBattleNetGames();

    List<DetectedGame> FindRockstarGames();

    (string installPath, EngineType engine) DetectEngineAndPath(string rootPath);

    /// <summary>
    /// Scans a user-picked folder for game candidates: if the folder itself holds a game executable it is
    /// returned as a single candidate; otherwise each immediate subfolder that contains an executable
    /// (within a few levels, junk folders skipped) is returned. Used by "Add a specific folder…" so the
    /// user can add a games-library root (e.g. D:\Games) and pick which detected games to add.
    /// </summary>
    IReadOnlyList<GameCandidate> FindGameCandidates(string root);

    void ClearEngineCache();

    GameMod? MatchGame(
        DetectedGame game,
        IEnumerable<GameMod> mods,
        Dictionary<string, string>? nameMappings = null);

    string NormalizeName(string name);
}
