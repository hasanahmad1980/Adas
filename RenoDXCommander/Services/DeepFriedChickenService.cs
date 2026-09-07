using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

/// <summary>
/// Local-import manager for the "Deep Fried Chicken" neural consumer — an alternative to the
/// RenoDX DLSS5 consumer that runs in the same place (beside the game exe for 64-bit games, or
/// inside host64 for 32-bit Feeder games).
///
/// Its licence (© Alexander) forbids bundling/redistribution, so Adas never ships it. The user
/// imports the author's official zip once; Adas caches the UNMODIFIED binaries and the DLSS 5
/// installer deploys them wherever the neural consumer goes, removing the RenoDX consumer.
/// See THIRD_PARTY_NOTICES.md.
/// </summary>
public sealed class DeepFriedChickenService
{
    public const string AddonFileName = "deep-fried-chicken.addon64";
    public const string NvngxShim = "deep-fried-chicken-nvngx.dll";
    public const string ConfigFileName = "deep-fried-chicken.cfg";
    public const string Dx11Bridge = "dlss5-dx11-bridge.addon64";

    /// <summary>
    /// The files a Deep Fried Chicken deploy places and owns — the three required files. These are
    /// also the files to retire when the RenoDX consumer takes over from a prior DFC install.
    /// (The optional Dx11Bridge is never deployed by Adas and shares a name with an obsolete RenoDX
    /// bridge that is cleaned up separately, so it is not part of this set.)
    /// </summary>
    public static readonly string[] RequiredFiles = { AddonFileName, NvngxShim, ConfigFileName };
    private static readonly string[] OptionalReleaseFiles = { Dx11Bridge };
    private const string ChecksumFileName = "SHA256SUMS.txt";
    private const long MaximumImportedFileBytes = 32 * 1024 * 1024;

    private readonly ICrashReporter _crashReporter;
    private readonly string _cacheDir;
    private readonly string _versionFile;

    public DeepFriedChickenService(ICrashReporter crashReporter, string? cacheDirectory = null)
    {
        _crashReporter = crashReporter;
        _cacheDir = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "deep-fried-chicken");
        _versionFile = Path.Combine(_cacheDir, "imported-version.txt");
    }

    /// <summary>True once the user has imported a valid Deep Fried Chicken release.</summary>
    public bool IsImported =>
        RequiredFiles.All(name => IsNonEmptyFile(Path.Combine(_cacheDir, name)));

    /// <summary>
    /// Detects DFC as the selected consumer without requiring every support file to be intact.
    /// This lets Repair preserve the user's selection when a partial legacy install is damaged.
    /// </summary>
    public static bool IsSelectedConsumer(string deploymentPath, bool is64Bit)
    {
        var consumerFolder = is64Bit
            ? ModInstallService.GetAddonDeployPath(deploymentPath)
            : Path.Combine(deploymentPath, "host64");
        return IsNonEmptyFile(Path.Combine(consumerFolder, AddonFileName))
               && !IsNonEmptyFile(Path.Combine(consumerFolder, Dlss5ComponentService.RenoDxDeploymentName))
               && !IsNonEmptyFile(Path.Combine(consumerFolder, Renodx5AddonService.AddonFileName));
    }

    /// <summary>Absolute path to a cached DFC file (may not exist).</summary>
    public string CachedFile(string name) => Path.Combine(_cacheDir, name);

    /// <summary>The version parsed from the imported archive name (e.g. "v1.4.8-alpha"), or null.</summary>
    public string? ImportedVersion => File.Exists(_versionFile) ? File.ReadAllText(_versionFile).Trim() : null;

    /// <summary>The core DFC files that a neural-consumer deploy should place into the target folder.</summary>
    public IReadOnlyList<string> DeployFiles() => RequiredFiles;

    /// <summary>
    /// Reuses an existing verified cache, or imports the newest official-looking DFC archive from
    /// the user's Downloads folder. The archive is still supplied by the user; Adas never bundles
    /// or redistributes the restricted binaries.
    /// </summary>
    public async Task<bool> EnsureImportedFromDefaultLocationsAsync(string? userProfile = null)
    {
        if (IsImported) return true;
        var archive = FindDefaultArchive(userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (archive == null) return false;
        return await ImportAsync(archive).ConfigureAwait(false) == null && IsImported;
    }

    internal static string? FindDefaultArchive(string userProfile)
    {
        var roots = new[]
        {
            Path.Combine(userProfile, "Downloads", "DLSS5"),
            Path.Combine(userProfile, "Downloads"),
        };
        return roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.zip", SearchOption.TopDirectoryOnly))
            .Where(path => Regex.IsMatch(
                Path.GetFileName(path),
                "deep[-_ ]?fried[-_ ]?chicken",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
    }

    /// <summary>
    /// Imports Deep Fried Chicken from the author's official zip (or an already-extracted folder),
    /// copying the unmodified binaries into the cache. Returns null on success, or an error string.
    /// </summary>
    public async Task<string?> ImportAsync(string sourcePath)
    {
        string? stagingDirectory = null;
        try
        {
            var wanted = RequiredFiles.Concat(OptionalReleaseFiles).ToArray();
            var found = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            byte[]? checksumBytes = null;

            if (File.Exists(sourcePath) && sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(sourcePath);
                foreach (var entry in zip.Entries)
                {
                    var name = Path.GetFileName(entry.FullName);
                    if (string.IsNullOrEmpty(name)) continue;
                    var isChecksum = name.Equals(ChecksumFileName, StringComparison.OrdinalIgnoreCase);
                    if ((!isChecksum && !wanted.Contains(name, StringComparer.OrdinalIgnoreCase))
                        || found.ContainsKey(name)) continue;
                    if (entry.Length <= 0 || entry.Length > MaximumImportedFileBytes)
                        return $"The release contains an empty or unexpectedly large file: {name}.";
                    using var s = entry.Open();
                    using var ms = new MemoryStream();
                    await s.CopyToAsync(ms).ConfigureAwait(false);
                    if (isChecksum)
                        checksumBytes ??= ms.ToArray();
                    else
                        found[name] = ms.ToArray();
                }
            }
            else if (Directory.Exists(sourcePath))
            {
                foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(file);
                    var isChecksum = name.Equals(ChecksumFileName, StringComparison.OrdinalIgnoreCase);
                    if ((!isChecksum && !wanted.Contains(name, StringComparer.OrdinalIgnoreCase))
                        || found.ContainsKey(name)) continue;
                    var length = new FileInfo(file).Length;
                    if (length <= 0 || length > MaximumImportedFileBytes)
                        return $"The release contains an empty or unexpectedly large file: {name}.";
                    var bytes = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
                    if (isChecksum)
                        checksumBytes ??= bytes;
                    else
                        found[name] = bytes;
                }
            }
            else
            {
                return "The selected Deep Fried Chicken source could not be read.";
            }

            var missing = RequiredFiles.Where(f => !found.ContainsKey(f)).ToArray();
            if (missing.Length > 0)
                return "That doesn't look like a Deep Fried Chicken release — missing: " + string.Join(", ", missing);

            if (checksumBytes == null)
                return $"That release does not contain {ChecksumFileName}, so Adas cannot verify its integrity.";
            var checksumError = ValidateChecksums(found, checksumBytes);
            if (checksumError != null) return checksumError;

            var cacheParent = Path.GetDirectoryName(Path.GetFullPath(_cacheDir))
                ?? throw new InvalidOperationException("The Deep Fried Chicken cache path has no parent folder.");
            Directory.CreateDirectory(cacheParent);
            stagingDirectory = Path.Combine(cacheParent, $".deep-fried-chicken-import-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDirectory);
            foreach (var name in RequiredFiles)
                await File.WriteAllBytesAsync(Path.Combine(stagingDirectory, name), found[name]).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(stagingDirectory, Path.GetFileName(_versionFile)),
                DeriveVersion(sourcePath)).ConfigureAwait(false);
            ReplaceCacheDirectory(stagingDirectory, cacheParent);
            stagingDirectory = null;
            _crashReporter.Log($"[DeepFriedChicken.Import] Verified and imported {RequiredFiles.Length} core file(s) from {sourcePath}");
            return null;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DeepFriedChicken.Import] Failed — {ex.Message}");
            return ex.Message;
        }
        finally
        {
            if (stagingDirectory != null)
                TryDeleteDirectory(stagingDirectory);
        }
    }

    private static string? ValidateChecksums(
        IReadOnlyDictionary<string, byte[]> files,
        byte[] checksumBytes)
    {
        var checksums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Encoding.UTF8.GetString(checksumBytes).Split('\n'))
        {
            var match = Regex.Match(line.Trim(), @"^([A-Fa-f0-9]{64})\s+\*?(.+?)\s*$");
            if (!match.Success) continue;
            checksums[Path.GetFileName(match.Groups[2].Value.Trim())] = match.Groups[1].Value;
        }

        var integrityFiles = new[] { AddonFileName, NvngxShim }
            .Concat(files.ContainsKey(Dx11Bridge) ? new[] { Dx11Bridge } : Array.Empty<string>());
        foreach (var name in integrityFiles)
        {
            if (!checksums.TryGetValue(name, out var expected))
                return $"{ChecksumFileName} does not contain a SHA-256 entry for {name}.";
            var actual = Convert.ToHexString(SHA256.HashData(files[name]));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                return $"SHA-256 verification failed for {name}. The selected release may be damaged or modified.";
        }
        return null;
    }

    private void ReplaceCacheDirectory(string stagingDirectory, string cacheParent)
    {
        var backupDirectory = Path.Combine(cacheParent, $".deep-fried-chicken-backup-{Guid.NewGuid():N}");
        var movedExisting = false;
        var replacementSucceeded = false;
        try
        {
            if (Directory.Exists(_cacheDir))
            {
                Directory.Move(_cacheDir, backupDirectory);
                movedExisting = true;
            }
            Directory.Move(stagingDirectory, _cacheDir);
            replacementSucceeded = true;
        }
        catch
        {
            if (movedExisting && !Directory.Exists(_cacheDir) && Directory.Exists(backupDirectory))
                Directory.Move(backupDirectory, _cacheDir);
            throw;
        }
        finally
        {
            if (replacementSucceeded && Directory.Exists(backupDirectory))
                TryDeleteDirectory(backupDirectory);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch { }
    }

    private static string DeriveVersion(string sourcePath)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var match = Regex.Match(name, @"v?\d+\.\d+(?:\.\d+)?[-.\w]*", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : (string.IsNullOrWhiteSpace(name) ? "imported" : name);
    }

    private static bool IsNonEmptyFile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
