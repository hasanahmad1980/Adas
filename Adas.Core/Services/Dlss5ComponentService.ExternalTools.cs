using System.Diagnostics;
using System.IO.Compression;

namespace RenoDXCommander.Services;

public sealed partial class Dlss5ComponentService
{
    internal const string NeuralScreenVersion = "1.10.0";
    private const string NeuralScreenUrl =
        "https://github.com/perseval-BLR/DLSS5-NeuralScreen/releases/download/v1.10.0/neuralscreen-v1.10.0-full.zip";
    private const string NeuralScreenSha256 =
        "DCF2EB7800695E3CEB793C36905C6DED397315B3A07957AC0010FDFD3D4D5009";
    private static readonly SemaphoreSlim NeuralScreenCacheLock = new(1, 1);

    /// <summary>
    /// Launches DLSS5-NeuralScreen — a standalone whole-desktop real-time Neural Rendering
    /// overlay. It is not a per-game route: it processes the whole screen (or one selected
    /// window) and touches no game files. Adas downloads the author's pinned release archive
    /// at run time (never bundled — ~215 MB, and it carries NVIDIA's leaked pre-release
    /// nvngx_dlssnr.dll which Adas must not redistribute), verifies its SHA-256, extracts it
    /// once into the per-user tool cache, and starts NeuralScreen.exe. RTX 30/40/50 only.
    /// </summary>
    public async Task LaunchNeuralScreenAsync(CancellationToken cancellationToken = default)
    {
        var toolDirectory = await EnsureNeuralScreenAsync(cancellationToken).ConfigureAwait(false);
        Process.Start(new ProcessStartInfo(Path.Combine(toolDirectory, "NeuralScreen.exe"))
        {
            UseShellExecute = true,
            WorkingDirectory = toolDirectory,
        });
    }

    internal const string FullScreenWrapperVersion = "1.1.0";
    private const string FullScreenWrapperUrl =
        "https://github.com/ThioJoe/Full-Screen-DLSS5-Wrapper/releases/download/v1.1.0/FullScreenWrapperForDLSS5.exe";
    private const string FullScreenWrapperSha256 =
        "974EA3A6A79675A5FFCB7A91C565F14CDC247790A85C7431C548CECA9420AB49";
    internal const string FullScreenWrapperMinimumDriver = "616.64";
    private static readonly SemaphoreSlim FullScreenWrapperCacheLock = new(1, 1);

    // The wrapper accepts an NGX DLL only when Windows trusts its Authenticode signature (chain to a
    // Microsoft root) and the signer is NVIDIA — the leaked/altered builds fail this. Same predicate.
    private static bool IsWrapperTrustedNvidia(string path) => DlssNrRepairService.IsTrustedNvidiaSigned(path);

    // Newest-first scan of an existing %LOCALAPPDATA%\RHI\<cache> tree for a copy the wrapper will
    // accept, so a signed DLL already on disk is used without any download or manifest fetch.
    private static string? FindCachedTrustedNvidia(string cacheSubdir, string dllName)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", cacheSubdir);
        if (!Directory.Exists(root)) return null;
        try
        {
            return Directory.EnumerateFiles(root, dllName, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault(IsWrapperTrustedNvidia);
        }
        catch { return null; }
    }

    /// <summary>
    /// True for RTX 40/30/20 (or an unknown/absent NVIDIA GPU) rather than RTX 50. DLSS 5 Neural
    /// Rendering ships only a Blackwell (sm_120) compute kernel, so below-Blackwell cards can't run
    /// it — no arch spoof helps, because there is no Ada/Ampere/Turing machine code to fall back to.
    /// </summary>
    public static bool IsBelowBlackwell(string? gpuName)
        => !string.IsNullOrWhiteSpace(gpuName)
           && !System.Text.RegularExpressions.Regex.IsMatch(gpuName, @"RTX\s*50\d\d",
               System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Warnings before launching ThioJoe's wrapper. Its DLSS 5 Neural Rendering needs an RTX 50-series
    /// GPU and driver 616.64+; on RTX 40/30/20 the wrapper starts but Neural Rendering cannot initialize.
    /// </summary>
    public static IReadOnlyList<string> FullScreenWrapperWarnings(string? gpuName, string? driverVersion)
    {
        var warnings = new List<string>();
        if (IsBelowBlackwell(gpuName))
            warnings.Add($"Detected GPU '{gpuName}' is not RTX 50-series. DLSS 5 Neural Rendering ships only a "
                + "Blackwell compute kernel, so it cannot run on this card — the wrapper will open but Neural "
                + "Rendering will not initialize. (DLSS Super Resolution upgrades are unaffected.)");
        else if (string.IsNullOrWhiteSpace(gpuName))
            warnings.Add("No NVIDIA GPU was detected. The wrapper's DLSS 5 Neural Rendering requires an "
                + "RTX 50-series card; on other cards it will open but Neural Rendering will not initialize.");
        if (Version.TryParse(driverVersion, out var driver) && driver < Version.Parse(FullScreenWrapperMinimumDriver))
            warnings.Add($"Driver {driverVersion} is older than {FullScreenWrapperMinimumDriver}, which the wrapper requires.");
        return warnings;
    }

    /// <summary>
    /// Launches ThioJoe's Full-Screen Wrapper for DLSS5: a signed single exe that captures the screen (or one
    /// window) with Windows screen capture and shows a click-through DLSS 5 output window on top. It touches no
    /// game files. Adas downloads the pinned release exe (never bundled; the repo has no licence), checks its
    /// SHA-256, and places nvngx_dlssnr.dll beside it from the NeuralScreen package or Adas's DLSS cache, plus
    /// nvngx_dlss.dll when available for the super resolution options.
    /// </summary>
    public async Task<string> LaunchFullScreenWrapperAsync(IDlssStreamlineService? streamline,
        IProgress<string>? progress = null, string? gpuName = null, CancellationToken cancellationToken = default)
    {
        var toolDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "Adas", "ExternalTools", $"FullScreenWrapper-{FullScreenWrapperVersion}");
        var executable = Path.Combine(toolDirectory, "FullScreenWrapperForDLSS5.exe");
        var notes = new List<string>();

        await FullScreenWrapperCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(toolDirectory);
            if (!File.Exists(executable) || !FileHelper.ComputeSha256(executable).Equals(FullScreenWrapperSha256, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report("Downloading Full-Screen Wrapper for DLSS5…");
                var download = executable + ".download";
                try
                {
                    await DownloadFileAsync(FullScreenWrapperUrl, download, cancellationToken).ConfigureAwait(false);
                    if (!FileHelper.ComputeSha256(download).Equals(FullScreenWrapperSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"The wrapper download did not match the official v{FullScreenWrapperVersion} SHA-256. Nothing was launched.");
                    File.Move(download, executable, overwrite: true);
                }
                finally { DeleteIfExists(download); }
            }

            var nr = Path.Combine(toolDirectory, "nvngx_dlssnr.dll");
            // The wrapper opens the DLL and demands a signature Windows trusts whose chain reaches a
            // Microsoft root AND whose signer is NVIDIA. NeuralScreen ships the leaked/altered 310.8.0
            // build (Authenticode HashMismatch), which the wrapper rejects with "is not the correct
            // file". So require an untouched NVIDIA-signed copy, and prefer the DLSS manifest cache
            // (genuine NVIDIA files) over the NeuralScreen bundle.
            if (!IsWrapperTrustedNvidia(nr))
            {
                progress?.Report("Getting a signed nvngx_dlssnr.dll…");
                // Prefer any already-cached signed copy (fast, offline). Only download as a last
                // resort — and never force the manifest's "newest", which is often an unsigned dev/SF
                // build the wrapper would reject anyway.
                string? source = FindCachedTrustedNvidia("DLSS-NR", "nvngx_dlssnr.dll");
                if (source is null)
                {
                    try
                    {
                        var neuralScreen = await EnsureNeuralScreenAsync(cancellationToken).ConfigureAwait(false);
                        source = Directory.EnumerateFiles(neuralScreen, "nvngx_dlssnr.dll", SearchOption.AllDirectories)
                            .FirstOrDefault(IsWrapperTrustedNvidia);
                    }
                    catch (Exception ex) { _crashReporter.Log($"[FullScreenWrapper] NeuralScreen runtime unavailable: {ex.Message}"); }
                }
                if (source is null && streamline is not null)
                {
                    try
                    {
                        progress?.Report("Downloading a signed nvngx_dlssnr.dll…");
                        if (streamline.DlssnrVersions.Count == 0) await streamline.FetchManifestAsync().ConfigureAwait(false);
                        var cached = await streamline.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
                        if (cached is not null && IsWrapperTrustedNvidia(cached)) source = cached;
                    }
                    catch (Exception ex) { _crashReporter.Log($"[FullScreenWrapper] DLSS cache runtime unavailable: {ex.Message}"); }
                }
                if (source is null)
                    throw new FileNotFoundException(
                        "Adas couldn't find an NVIDIA-signed nvngx_dlssnr.dll, which the wrapper requires. "
                        + "The DLSS 5 cache and NeuralScreen bundles only hold the leaked (unsigned) build. "
                        + "Copy a genuine nvngx_dlssnr.dll from a DLSS 5 game or the NVIDIA DLSS SDK into "
                        + toolDirectory + " and try again.");
                File.Copy(source, nr, overwrite: true);
            }

            var sr = Path.Combine(toolDirectory, "nvngx_dlss.dll");
            if (!IsWrapperTrustedNvidia(sr) && streamline is not null)
            {
                DeleteIfExists(sr);
                progress?.Report("Getting a signed nvngx_dlss.dll…");
                var cachedSr = FindCachedTrustedNvidia("DLSS", "nvngx_dlss.dll");
                if (cachedSr is not null) { File.Copy(cachedSr, sr, overwrite: true); }
                else
                try
                {
                    if (streamline.DlssVersions.Count == 0) await streamline.FetchManifestAsync().ConfigureAwait(false);
                    if (await streamline.EnsureNewestDlssCachedAsync().ConfigureAwait(false) is { } dlss && IsWrapperTrustedNvidia(dlss))
                        File.Copy(dlss, sr, overwrite: true);
                }
                catch (Exception ex) { _crashReporter.Log($"[FullScreenWrapper] nvngx_dlss.dll unavailable: {ex.Message}"); }
            }
            if (!IsWrapperTrustedNvidia(sr)) notes.Add("A signed nvngx_dlss.dll wasn't available, so the super resolution options are off.");
        }
        finally { FullScreenWrapperCacheLock.Release(); }

        // Neural Rendering only runs on RTX 50 (the DLL ships a Blackwell-only cubin), so there is no
        // injection trick that helps below Blackwell — start the wrapper plainly and let it surface its
        // own state. The pre-launch warnings already told the user what to expect on their GPU.
        using (var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = toolDirectory,
        })) { }
        return string.Join(" ", notes);
    }

    private async Task<string> EnsureNeuralScreenAsync(CancellationToken cancellationToken)
    {
        var toolDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "Adas", "ExternalTools", $"NeuralScreen-{NeuralScreenVersion}");
        var executable = Path.Combine(toolDirectory, "NeuralScreen.exe");
        var sentinel = Path.Combine(toolDirectory, $".verified-{NeuralScreenSha256}");

        await NeuralScreenCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(executable) || !File.Exists(sentinel))
            {
                if (Directory.Exists(toolDirectory)) Directory.Delete(toolDirectory, recursive: true);
                Directory.CreateDirectory(toolDirectory);
                var archive = Path.Combine(toolDirectory, "neuralscreen.zip");
                try
                {
                    await DownloadFileAsync(NeuralScreenUrl, archive, cancellationToken).ConfigureAwait(false);
                    if (!FileHelper.ComputeSha256(archive).Equals(NeuralScreenSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("NeuralScreen download did not match the official v1.10.0 SHA-256. Nothing was launched.");
                    ZipFile.ExtractToDirectory(archive, toolDirectory, overwriteFiles: true);
                    if (!File.Exists(executable))
                        throw new FileNotFoundException("NeuralScreen.exe was not found in the extracted archive.");
                    await File.WriteAllTextAsync(sentinel, NeuralScreenSha256, cancellationToken).ConfigureAwait(false);
                }
                finally { DeleteIfExists(archive); }
            }
        }
        finally { NeuralScreenCacheLock.Release(); }
        return toolDirectory;
    }

    private const string OneClickUrl =
        "https://github.com/faisalkindi/DLSS5oneclick/releases/download/v0.13.15/dlss5oneclick.exe";
    private const string OneClickSha256 =
        "29766BBA49B02D0EFB7EBEB23F5FF9B7B83642799A0CE30188669D375699E6C4";

    public async Task LaunchOneClickAsync(string gameFolder, CancellationToken cancellationToken = default)
    {
        gameFolder = Path.GetFullPath(gameFolder);
        if (!Directory.Exists(gameFolder))
            throw new DirectoryNotFoundException($"Game folder not found: {gameFolder}");

        var toolDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "Adas", "ExternalTools", $"DLSS5oneclick-{OneClickVersion}");
        Directory.CreateDirectory(toolDirectory);
        var executable = Path.Combine(toolDirectory, "dlss5oneclick.exe");
        if (!File.Exists(executable)
            || !FileHelper.ComputeSha256(executable).Equals(OneClickSha256, StringComparison.OrdinalIgnoreCase))
        {
            var temporary = executable + $".{Guid.NewGuid():N}.download";
            try
            {
                await DownloadFileAsync(OneClickUrl, temporary, cancellationToken).ConfigureAwait(false);
                ValidatePortableExecutable(temporary, 1024 * 1024, "dlss5oneclick.exe", expectedMachine: 0x8664);
                if (!FileHelper.ComputeSha256(temporary).Equals(OneClickSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("OneClick download did not match the official v0.13.15 SHA-256. Nothing was launched.");
                File.Move(temporary, executable, overwrite: true);
            }
            finally { DeleteIfExists(temporary); }
        }

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = toolDirectory,
        };
        start.ArgumentList.Add(gameFolder);
        Process.Start(start);
    }
}
