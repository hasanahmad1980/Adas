using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

/// <summary>
/// Turns a messy install/folder name into something the Steam Store search can match, and scores
/// candidate results fuzzily. Folder names in the wild look like <c>Assetto.Corsa.EVO-InsaneRamZes</c>,
/// <c>Call Of Duty 2 Collector's Edition</c> or <c>BeamNG.drive-InsaneRamZes</c> — dots-for-spaces,
/// trailing scene/repack group tags, and edition/version noise all defeat an exact-name lookup.
/// Also recognises stand-alone emulators (which are never on the Steam store under the game's name)
/// and maps them to a canonical logo.
/// </summary>
public static class GameNameCleaner
{
    // Scene/repack release groups and generic packaging noise. Matched as whole, case-insensitive
    // tokens so we never chop a real word (e.g. the "Gold" in "Pokemon Gold" survives — it is not here).
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // release groups / repackers
        "codex","plaza","flt","rune","empress","tenoke","dodi","fitgirl","elamigos","skidrow",
        "reloaded","hoodlum","tinyiso","razor1911","prophet","cpy","steampunks","chronos","darksiders",
        "insaneramzes","gog","rune","kaos","doge","tenoke","goldberg","online","p2p","0xdeadc0de",
        // packaging noise
        "repack","repacks","proper","readnfo","incl","dlc","dlcs","update","updates","crackfix","cracked",
        "multi","multi2","multi3","multi4","multi5","multi6","multi7","multi8","multi9","multi10","multi11",
        "multi12","edition","editions","collector","collectors","collectorsedition","deluxe","ultimate",
        "definitive","goty","complete","bundle","anniversary","standard","digital","remake","enhanced",
        "build","win64","win32","x64","x86",
    };

    // Small stop-word set dropped only when comparing token sets (never from the display name).
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    { "the","of","a","an","and" };

    /// <summary>
    /// Ordered list of search terms to try against the Steam Store, most-specific first. Usually one
    /// entry; a shorter fallback is added when the name carries obvious edition/subtitle noise.
    /// </summary>
    public static IReadOnlyList<string> SearchCandidates(string raw)
    {
        var primary = Clean(raw);
        if (string.IsNullOrWhiteSpace(primary))
            return Array.Empty<string>();

        var list = new List<string> { primary };

        // Fallback: drop a trailing subtitle after a colon/dash ("Game: The Subtitle" -> "Game").
        var words = primary.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 4)
        {
            var trimmed = string.Join(' ', words.Take(Math.Max(2, words.Length - 2)));
            if (!string.Equals(trimmed, primary, StringComparison.OrdinalIgnoreCase))
                list.Add(trimmed);
        }

        return list;
    }

    /// <summary>Produces a display-friendly cleaned title (dots→spaces, noise/version/tags stripped).</summary>
    public static string Clean(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var s = raw.Trim();

        // Strip bracketed/parenthetical chunks: "[FitGirl Repack]", "(2023)", "{...}".
        s = Regex.Replace(s, @"[\[\(\{][^\]\)\}]*[\]\)\}]", " ");

        // Repack/scene folders use no spaces but dots/underscores as separators and a trailing "-Group".
        // If there are no spaces yet, turn separators into spaces so tokenisation works.
        if (!s.Contains(' '))
        {
            // Drop a trailing "-Group" scene tag (single token, no spaces) before splitting.
            s = Regex.Replace(s, @"-[A-Za-z0-9]+$", "");
            s = s.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        }
        else
        {
            s = s.Replace('_', ' ');
        }

        // Strip version/build noise anywhere: v1.2.3, 1.02, build 12345.
        s = Regex.Replace(s, @"\bv?\d+(\.\d+){1,3}\b", " ");
        s = Regex.Replace(s, @"\bbuild\s*\d+\b", " ", RegexOptions.IgnoreCase);

        // Trademark symbols.
        s = s.Replace("™", "").Replace("®", "").Replace("©", "").Replace("'", "'");

        // Tokenise and drop noise tokens; keep the rest in order. Compare against a punctuation-free
        // key so possessives/edition tags match ("Collector's" → "collectors").
        var kept = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => !NoiseTokens.Contains(Regex.Replace(t, "[^A-Za-z0-9]", "")))
                    .ToList();

        var cleaned = string.Join(' ', kept);
        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
        return cleaned.Length == 0 ? raw.Trim() : cleaned;
    }

    /// <summary>
    /// Picks the best Steam Store result for <paramref name="raw"/> from <paramref name="results"/>,
    /// or null when nothing clears the similarity gate. Leans on Steam's own relevance ranking but
    /// guards against unrelated top hits.
    /// </summary>
    public static int? BestMatch(string raw, IReadOnlyList<(int Id, string Name)> results)
    {
        if (results is null || results.Count == 0) return null;

        var q = Tokens(Clean(raw));
        if (q.Count == 0) return null;

        int? bestId = null;
        double best = 0;
        for (int i = 0; i < results.Count; i++)
        {
            var (id, name) = results[i];
            var c = Tokens(Clean(name));
            if (c.Count == 0) continue;

            var score = Score(q, c);
            // Nudge toward Steam's ranking so ties resolve to the more relevant hit.
            score -= i * 0.01;
            if (score > best)
            {
                best = score;
                bestId = id;
            }
        }

        return best >= 0.6 ? bestId : null;
    }

    private static double Score(List<string> q, List<string> c)
    {
        var qn = string.Concat(q);
        var cn = string.Concat(c);
        if (qn == cn) return 1.0;
        if (cn.StartsWith(qn, StringComparison.Ordinal) || qn.StartsWith(cn, StringComparison.Ordinal))
            return 0.9;

        var qs = new HashSet<string>(q, StringComparer.Ordinal);
        var cs = new HashSet<string>(c, StringComparer.Ordinal);
        int inter = qs.Count(cs.Contains);
        if (inter == 0) return 0;

        double covByCand = (double)inter / cs.Count;   // how much of the Steam title the query covers
        double covByQuery = (double)inter / qs.Count;   // how much of the query the Steam title covers
        // Require the Steam result to be well-covered (so "Call of Duty" doesn't win for "Call of Duty 2"),
        // and reward mutual overlap.
        return (covByCand * 0.7) + (covByQuery * 0.3);
    }

    private static List<string> Tokens(string s)
        => Regex.Split(s.ToLowerInvariant(), @"[^a-z0-9]+")
                .Where(t => t.Length > 0 && !StopWords.Contains(t))
                .ToList();

    // ── Emulators ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recognises a stand-alone console emulator from its folder/exe name and returns a logo URL.
    /// Emulators aren't on the Steam store under a game name, so cover-art search always misses them;
    /// this gives the card a recognisable logo instead. Best-effort — a dead URL just falls back to
    /// "no art". Wikimedia thumbnails are used (rasterised PNG via the width parameter).
    /// </summary>
    public static bool TryGetEmulatorArtUrl(string raw, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var n = Regex.Replace(raw.ToLowerInvariant(), @"[^a-z0-9]", "");
        foreach (var (needle, file) in EmulatorLogos)
        {
            if (n.Contains(needle))
            {
                url = "https://commons.wikimedia.org/wiki/Special:FilePath/" +
                      Uri.EscapeDataString(file) + "?width=600";
                return true;
            }
        }
        return false;
    }

    // needle (normalised, no separators) → Wikimedia Commons file name. Ordered longest-first so
    // "pcsx2" is tested before a hypothetical "pcs".
    private static readonly (string Needle, string File)[] EmulatorLogos =
    {
        ("retroarch",   "RetroArch logo.svg"),
        ("duckstation", "DuckStation logo.svg"),
        ("ryujinx",     "Ryujinx Logo.png"),
        ("dolphin",     "Dolphin Emulator logo.svg"),
        ("citra",       "Citra-emu logo.svg"),
        ("ppsspp",      "PPSSPP logo.svg"),
        ("scummvm",     "ScummVM logo.svg"),
        ("xenia",       "Xenia logo.png"),
        ("rpcs3",       "RPCS3 logo.svg"),
        ("pcsx2",       "PCSX2 Logo.svg"),
        ("yuzu",        "Yuzu emulator logo.svg"),
        ("cemu",        "Cemu logo.png"),
        ("mgba",        "MGBA logo.svg"),
        ("snes9x",      "Snes9x logo.png"),
        ("project64",   "Project64 logo.png"),
    };
}
