using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

/// <summary>
/// Checks GitHub Releases for a newer version of Adas and downloads the installer if requested.
/// </summary>
public class UpdateService : IUpdateService
{
    private readonly HttpClient _http;
    private readonly GitHubETagCache _etagCache;

    public UpdateService(HttpClient http, GitHubETagCache etagCache)
    {
        _http = http;
        _etagCache = etagCache;
    }
    // GitHub API endpoint for the latest Adas release. Releases are tagged "vX.Y.Z" and named
    // "Adas X.Y.Z", both of which RdxcVersion.TryParse understands. /releases/latest returns the
    // newest non-prerelease, which is exactly what the installer flow ships.
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/hasanahmad1980/Adas/releases/latest";

    // The asset filename to look for when updating (the Inno Setup output, see Adas Setup.iss).
    private static readonly string[] InstallerFileNames = ["Adas-Setup.exe"];

    /// <summary>
    /// Returns the current app version from the assembly metadata. Prefers the entry assembly
    /// (Adas.exe) so the version reflects the shipped app rather than this engine library; both
    /// are stamped from <c>Adas Setup.iss</c>'s <c>MyAppVersion</c> at publish time
    /// (see <c>tools/build-adas.ps1</c>). Falls back to the executing assembly for unit tests.
    /// </summary>
    public Version CurrentVersion =>
        (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version
            ?? new Version(0, 0, 0, 0);

    /// <summary>
    /// Silently checks GitHub for a newer release. When <paramref name="betaOptIn"/> is true,
    /// queries both the stable and beta endpoints and uses <see cref="VersionResolver"/> to
    /// pick the winning version. Returns the parsed remote version and the download URL for
    /// the installer asset, or null if no update is available or the check fails.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(bool betaOptIn = false)
    {
        try
        {
            // Adas ships a single stable channel via /releases/latest. (betaOptIn is retained for
            // interface compatibility but there is no separate Adas beta feed to query.)
            _ = betaOptIn;
            var stable = await FetchReleaseAsync(LatestReleaseApiUrl).ConfigureAwait(false);
            (RdxcVersion version, string downloadUrl, string? sha256, string? releasePageUrl)? beta = null;

            // Build the current version as an RdxcVersion for the resolver
            var current = CurrentVersion;
            var betaNum = current.Revision > 0 ? (int?)current.Revision : null;
            var currentRdxc = new RdxcVersion(current.Major, current.Minor, current.Build, betaNum);

            // Use VersionResolver to determine which update (if any) to offer
            RdxcVersion? winner = VersionResolver.Resolve(
                currentRdxc,
                stable?.version,
                beta?.version);

            if (winner == null)
            {
                CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] Up to date (local={currentRdxc.ToDisplayString()})");
                return null;
            }

            // Determine the download URL from the winning source
            string? winnerDownloadUrl = null;
            string? winnerSha = null;
            string? winnerReleasePage = null;
            if (stable.HasValue && winner.Value == stable.Value.version)
                (winnerDownloadUrl, winnerSha, winnerReleasePage) = (stable.Value.downloadUrl, stable.Value.sha256, stable.Value.releasePageUrl);
            else if (beta.HasValue && winner.Value == beta.Value.version)
                (winnerDownloadUrl, winnerSha, winnerReleasePage) = (beta.Value.downloadUrl, beta.Value.sha256, beta.Value.releasePageUrl);

            if (string.IsNullOrEmpty(winnerDownloadUrl))
            {
                CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] Winner {winner.Value.ToDisplayString()} has no download URL");
                return null;
            }

            var cmp = new Version(current.Major, current.Minor, current.Build);
            var rmt = new Version(winner.Value.Major, winner.Value.Minor, winner.Value.Build);

            CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] Update available {currentRdxc.ToDisplayString()} → {winner.Value.ToDisplayString()}");
            return new UpdateInfo
            {
                CurrentVersion = cmp,
                RemoteVersion  = rmt,
                DownloadUrl    = winnerDownloadUrl,
                DisplayVersion = winner.Value.ToDisplayString(),
                ExpectedSha256 = winnerSha,
                ReleasePageUrl = winnerReleasePage,
            };
        }
        catch (Exception ex)
        {
            // Update check should never crash the app — swallow and log
            CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] Check failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Fetches a single GitHub release endpoint, parses the version with <see cref="RdxcVersion.TryParse"/>,
    /// and extracts the installer download URL. Returns null if the endpoint fails or the version cannot be parsed.
    /// </summary>
    private async Task<(RdxcVersion version, string downloadUrl, string? sha256, string? releasePageUrl)?> FetchReleaseAsync(string apiUrl)
    {
        var json = await _etagCache.GetWithETagAsync(_http, apiUrl, $"Adas/{CurrentVersion}").ConfigureAwait(false);
        if (json == null)
        {
            CrashReporter.Log($"[UpdateService.FetchReleaseAsync] GitHub API returned error for {apiUrl}");
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Parse version from release name or tag_name using RdxcVersion
        var releaseName = root.TryGetProperty("name", out var name) ? name.GetString() : null;
        var tagName = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
        var releasePageUrl = root.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null;

        if (!RdxcVersion.TryParse(releaseName, out var version) &&
            !RdxcVersion.TryParse(tagName, out version))
        {
            CrashReporter.Log($"[UpdateService.FetchReleaseAsync] Could not parse version from tag='{tagName}' name='{releaseName}'");
            return null;
        }

        // Find the installer asset download URL (Adas-Setup.exe).
        string? downloadUrl = null;
        string? sha256 = null;
        string? sumsUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var assetName = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.Equals(assetName, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)
                    && asset.TryGetProperty("browser_download_url", out var su))
                    sumsUrl = su.GetString();
            }
            foreach (var installerName in InstallerFileNames)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var assetName = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.Equals(assetName, installerName, StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var url)
                            ? url.GetString() : null;
                        sha256 = ParseDigest(asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null);
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(downloadUrl)) break;
            }
        }

        if (string.IsNullOrEmpty(downloadUrl))
        {
            CrashReporter.Log($"[UpdateService.FetchReleaseAsync] Version {version.ToDisplayString()} found but no installer asset (tried {string.Join(", ", InstallerFileNames)})");
            return null;
        }

        // Fall back to a SHA256SUMS.txt asset when GitHub didn't expose a digest.
        if (sha256 is null && sumsUrl is not null)
        {
            try
            {
                var sums = await _http.GetStringAsync(sumsUrl).ConfigureAwait(false);
                sha256 = FindSumFor(sums, Path.GetFileName(new Uri(downloadUrl).LocalPath));
            }
            catch (Exception ex) { CrashReporter.Log($"[UpdateService.FetchReleaseAsync] SHA256SUMS fetch failed — {ex.Message}"); }
        }

        return (version, downloadUrl, sha256, releasePageUrl);
    }

    /// <summary>Parses a GitHub asset digest ("sha256:hex") into lowercase hex, or null.</summary>
    internal static string? ParseDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        var m = Regex.Match(digest.Trim(), "^sha256:([0-9a-fA-F]{64})$");
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>Finds the hash for <paramref name="fileName"/> in sha256sum-style text ("hex  name" or "hex *name").</summary>
    internal static string? FindSumFor(string sums, string fileName)
    {
        foreach (var line in sums.Split((char)10))
        {
            var m = Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if (m.Success && string.Equals(m.Groups[2].Value.Trim(), fileName, StringComparison.OrdinalIgnoreCase))
                return m.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>True when the file's SHA-256 matches <paramref name="expected"/> (hex, any case).</summary>
    internal static bool FileMatchesSha256(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when <paramref name="value"/> is a syntactically valid SHA-256 (exactly 64 hex chars).</summary>
    internal static bool IsValidSha256(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value.Trim(), "^[0-9a-fA-F]{64}$");

    /// <summary>
    /// Downloads the installer to a temporary file, verifies its SHA-256 against
    /// <paramref name="expectedSha256"/>, and returns a launchable path only when verification
    /// succeeds. A missing, malformed, mismatched, or otherwise unavailable checksum is an explicit
    /// verification failure that returns <c>null</c> and leaves no accepted installer — an unverified
    /// installer must never reach <see cref="LaunchInstallerAndExit"/>.
    /// </summary>
    public async Task<string?> DownloadInstallerAsync(
        string downloadUrl,
        IProgress<(string msg, double pct)>? progress = null,
        string? expectedSha256 = null)
    {
        // Fail closed before spending any bandwidth: without a valid expected hash we can never
        // verify the download, so there is nothing safe to launch.
        if (!IsValidSha256(expectedSha256))
        {
            CrashReporter.Log("[UpdateService.DownloadInstallerAsync] No valid SHA-256 for the release; refusing to download an unverifiable installer");
            progress?.Report(("This update can't be verified automatically — install it from the Adas releases page.", 0));
            return null;
        }
        var expected = expectedSha256!.Trim();

        // Download into a unique .part file so a partial or rejected download is never mistaken for a
        // verified installer, and is promoted to the final name only after the hash matches.
        var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
        if (string.IsNullOrEmpty(fileName)) fileName = InstallerFileNames[0];
        var finalPath = Path.Combine(Path.GetTempPath(), fileName);
        var partPath = Path.Combine(Path.GetTempPath(), $"{fileName}.{Guid.NewGuid():N}.part");
        try
        {
            progress?.Report(("Downloading update...", 0));

            var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Adas", CurrentVersion.ToString()));

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;

            await using (var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            await using (var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1024 * 1024, useAsync: true))
            {
                var buffer = new byte[1024 * 1024]; // 1 MB
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var pct = (double)totalRead / totalBytes * 100;
                        progress?.Report(($"Downloading update... {pct:F0}%", pct));
                    }
                }
            }

            // A truncated download (Content-Length promised more than arrived) is a verification failure too.
            if (totalBytes > 0 && new FileInfo(partPath).Length != totalBytes)
            {
                TryDelete(partPath);
                CrashReporter.Log($"[UpdateService.DownloadInstallerAsync] Truncated download ({new FileInfo(partPath).Length}/{totalBytes} bytes) — discarded");
                progress?.Report(("The update download was incomplete — nothing was installed.", 0));
                return null;
            }

            progress?.Report(("Verifying update checksum...", 100));
            if (!FileMatchesSha256(partPath, expected))
            {
                TryDelete(partPath);
                CrashReporter.Log($"[UpdateService.DownloadInstallerAsync] Checksum mismatch — discarded {partPath}");
                progress?.Report(("Update checksum did not match — the download was discarded.", 0));
                return null;
            }

            // Verified: promote to the stable name only now.
            TryDelete(finalPath);
            File.Move(partPath, finalPath);
            progress?.Report(("Download complete.", 100));
            CrashReporter.Log($"[UpdateService.DownloadInstallerAsync] Verified installer at {finalPath}");
            return finalPath;
        }
        catch (Exception ex)
        {
            TryDelete(partPath);
            CrashReporter.Log($"[UpdateService.DownloadInstallerAsync] Download failed — {ex.Message}");
            progress?.Report(($"Download failed: {ex.Message}", 0));
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort cleanup */ }
    }

    /// <summary>
    /// Launches the downloaded installer and requests the app to close.
    /// </summary>
    public void LaunchInstallerAndExit(string installerPath, Action closeApp)
    {
        try
        {
            CrashReporter.Log($"[UpdateService.LaunchInstaller] Launching installer {installerPath}");
            Process.Start(new ProcessStartInfo
            {
                FileName        = installerPath,
                UseShellExecute = true,
            });

            // Give the installer a moment to start, then close the app
            closeApp();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UpdateService.LaunchInstaller] Failed to launch installer — {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a version string from a release tag or name.
    /// Handles formats like: "1.2.2", "v1.2.2", "RHI-1.2.2", "RHI 1.2.2", "RHI v1.2.2" etc.
    /// </summary>
    private Version? ParseVersion(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        // Match the first occurrence of a version-like pattern (digits.digits.digits[.digits])
        var match = Regex.Match(input, @"(\d+\.\d+\.\d+(?:\.\d+)?)");
        if (!match.Success) return null;

        return Version.TryParse(match.Groups[1].Value, out var ver) ? ver : null;
    }
}

/// <summary>
/// Contains information about an available update.
/// </summary>
public class UpdateInfo
{
    public required Version CurrentVersion { get; init; }
    public required Version RemoteVersion  { get; init; }
    public required string  DownloadUrl    { get; init; }
    public string? DisplayVersion { get; init; }
    /// <summary>SHA-256 (lowercase hex) GitHub published for the installer asset, when available.</summary>
    public string? ExpectedSha256 { get; init; }
    /// <summary>The GitHub release page, offered to the user when the installer can't be auto-verified.</summary>
    public string? ReleasePageUrl { get; init; }
}

/// <summary>
/// Represents a RHI version with optional beta suffix.
/// Examples: "1.4.8", "1.4.8-beta1", "1.4.8 beta 1"
/// </summary>
public readonly record struct RdxcVersion(int Major, int Minor, int Build, int? BetaNumber = null)
    : IComparable<RdxcVersion>
{
    /// <summary>True when this version has a beta suffix.</summary>
    public bool IsBeta => BetaNumber.HasValue;

    /// <summary>Base version tuple without beta suffix, used for priority comparison.</summary>
    public (int, int, int) BaseVersion => (Major, Minor, Build);

    /// <summary>
    /// Compares by (Major, Minor, Build) first, then by beta status:
    /// stable (no beta) is greater than any beta at the same base version,
    /// and a higher beta number is greater than a lower one.
    /// </summary>
    public int CompareTo(RdxcVersion other)
    {
        int cmp = Major.CompareTo(other.Major);
        if (cmp != 0) return cmp;

        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0) return cmp;

        cmp = Build.CompareTo(other.Build);
        if (cmp != 0) return cmp;

        // Same base version — stable > beta, higher beta > lower beta
        if (!IsBeta && !other.IsBeta) return 0;
        if (!IsBeta && other.IsBeta) return 1;   // stable > beta
        if (IsBeta && !other.IsBeta) return -1;  // beta < stable

        // Both are beta — compare beta numbers
        return BetaNumber!.Value.CompareTo(other.BetaNumber!.Value);
    }

    // Regex: optional leading text (e.g. "HDRX-", "v"), then Major.Minor.Build, optional beta suffix
    private static readonly Regex VersionPattern = new(
        @"(\d+)\.(\d+)\.(\d+)(?:[\s-]*beta[\s-]*(\d+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses version strings such as "1.4.8", "1.4.8-beta1", "1.4.8 beta 1",
    /// "v1.4.8-beta2", "RHI-1.4.8-beta1". Returns false and logs when input is invalid.
    /// </summary>
    public static bool TryParse(string? input, out RdxcVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(input))
        {
            CrashReporter.Log($"[RdxcVersion.TryParse] Cannot parse null or empty version string");
            return false;
        }

        var match = VersionPattern.Match(input);
        if (!match.Success)
        {
            CrashReporter.Log($"[RdxcVersion.TryParse] Cannot parse version from '{input}'");
            return false;
        }

        int major = int.Parse(match.Groups[1].Value);
        int minor = int.Parse(match.Groups[2].Value);
        int build = int.Parse(match.Groups[3].Value);
        int? betaNumber = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : null;

        version = new RdxcVersion(major, minor, build, betaNumber);
        return true;
    }

    /// <summary>
    /// Formats to display string: "1.4.8" for stable, "1.4.8 beta 1" for beta.
    /// </summary>
    public string ToDisplayString() =>
        IsBeta ? $"{Major}.{Minor}.{Build} beta {BetaNumber}" : $"{Major}.{Minor}.{Build}";
}

/// <summary>
/// Determines which update (if any) to offer based on current version,
/// latest stable, and latest beta.
/// </summary>
internal static class VersionResolver
{
    /// <summary>
    /// Returns the version to offer, or null if up to date.
    /// Rules:
    ///   - Stable always wins over beta at same or higher base version
    ///   - Beta only offered when its base version exceeds both latest stable's
    ///     and current version's base version
    ///   - Only offer if candidate's base version > current base version
    /// </summary>
    public static RdxcVersion? Resolve(
        RdxcVersion current,
        RdxcVersion? latestStable,
        RdxcVersion? latestBeta)
    {
        var currentBase = current.BaseVersion;

        // Determine if stable is a valid candidate (base version > current base version)
        bool stableValid = latestStable.HasValue
            && latestStable.Value.BaseVersion.CompareTo(currentBase) > 0;

        // Determine if beta is a valid candidate:
        //   - base version > current base version, OR
        //   - same base version but current is a beta with a lower beta number
        //   - base version > latest stable base version (beta only wins when ahead of stable)
        bool betaValid = latestBeta.HasValue
            && (latestBeta.Value.BaseVersion.CompareTo(currentBase) > 0
                || (latestBeta.Value.BaseVersion == currentBase
                    && current.IsBeta
                    && latestBeta.Value.CompareTo(current) > 0))
            && (!latestStable.HasValue
                || latestBeta.Value.BaseVersion.CompareTo(latestStable.Value.BaseVersion) > 0);

        // Beta takes priority when it's ahead of both stable and current
        if (betaValid)
            return latestBeta;

        if (stableValid)
            return latestStable;

        return null;
    }
}
