using System.IO.Compression;
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
    private static readonly string[] OptionalFiles = { Dx11Bridge };

    private readonly ICrashReporter _crashReporter;
    private readonly string _cacheDir;
    private readonly string _versionFile;

    public DeepFriedChickenService(ICrashReporter crashReporter)
    {
        _crashReporter = crashReporter;
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "deep-fried-chicken");
        _versionFile = Path.Combine(_cacheDir, "imported-version.txt");
    }

    /// <summary>True once the user has imported a valid Deep Fried Chicken release.</summary>
    public bool IsImported =>
        File.Exists(Path.Combine(_cacheDir, AddonFileName)) && File.Exists(Path.Combine(_cacheDir, NvngxShim));

    /// <summary>Absolute path to a cached DFC file (may not exist).</summary>
    public string CachedFile(string name) => Path.Combine(_cacheDir, name);

    /// <summary>Whether the imported release included the optional native-D3D11 bridge.</summary>
    public bool HasDx11Bridge => File.Exists(Path.Combine(_cacheDir, Dx11Bridge));

    /// <summary>The version parsed from the imported archive name (e.g. "v1.4.8-alpha"), or null.</summary>
    public string? ImportedVersion => File.Exists(_versionFile) ? File.ReadAllText(_versionFile).Trim() : null;

    /// <summary>The core DFC files that a neural-consumer deploy should place into the target folder.</summary>
    public IReadOnlyList<string> DeployFiles(bool includeDx11Bridge)
        => includeDx11Bridge && HasDx11Bridge
            ? RequiredFiles.Append(Dx11Bridge).ToArray()
            : RequiredFiles;

    /// <summary>
    /// Imports Deep Fried Chicken from the author's official zip (or an already-extracted folder),
    /// copying the unmodified binaries into the cache. Returns null on success, or an error string.
    /// </summary>
    public async Task<string?> ImportAsync(string sourcePath)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            var wanted = RequiredFiles.Concat(OptionalFiles).ToArray();
            var found = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(sourcePath) && sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(sourcePath);
                foreach (var entry in zip.Entries)
                {
                    var name = Path.GetFileName(entry.FullName);
                    if (string.IsNullOrEmpty(name) || found.ContainsKey(name)
                        || !wanted.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    using var s = entry.Open();
                    using var ms = new MemoryStream();
                    await s.CopyToAsync(ms).ConfigureAwait(false);
                    found[name] = ms.ToArray();
                }
            }
            else if (Directory.Exists(sourcePath))
            {
                foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(file);
                    if (found.ContainsKey(name) || !wanted.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    found[name] = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
                }
            }
            else
            {
                return "The selected Deep Fried Chicken source could not be read.";
            }

            var missing = RequiredFiles.Where(f => !found.ContainsKey(f)).ToArray();
            if (missing.Length > 0)
                return "That doesn't look like a Deep Fried Chicken release — missing: " + string.Join(", ", missing);

            foreach (var (name, bytes) in found)
                await File.WriteAllBytesAsync(Path.Combine(_cacheDir, name), bytes).ConfigureAwait(false);
            File.WriteAllText(_versionFile, DeriveVersion(sourcePath));
            _crashReporter.Log($"[DeepFriedChicken.Import] Imported {found.Count} file(s) from {sourcePath}");
            return null;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DeepFriedChicken.Import] Failed — {ex.Message}");
            return ex.Message;
        }
    }

    private static string DeriveVersion(string sourcePath)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var match = Regex.Match(name, @"v?\d+\.\d+(?:\.\d+)?[-.\w]*", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : (string.IsNullOrWhiteSpace(name) ? "imported" : name);
    }
}
