using System.Diagnostics;
using System.IO.Compression;

namespace RenoDXCommander.Services;

public sealed partial class Dlss5ComponentService
{
    internal const string NeuralScreenVersion = "1.7.0";
    private const string NeuralScreenUrl =
        "https://github.com/perseval-BLR/DLSS5-NeuralScreen/releases/download/v1.7.0/neuralscreen-v1.7.0-full.zip";
    private const string NeuralScreenSha256 =
        "301B351383020D98D5C521C288371B43661D741D719CBB9B43BEDE63331E32DE";
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
                        throw new InvalidDataException("NeuralScreen download did not match the official v1.7.0 SHA-256. Nothing was launched.");
                    ZipFile.ExtractToDirectory(archive, toolDirectory, overwriteFiles: true);
                    if (!File.Exists(executable))
                        throw new FileNotFoundException("NeuralScreen.exe was not found in the extracted archive.");
                    await File.WriteAllTextAsync(sentinel, NeuralScreenSha256, cancellationToken).ConfigureAwait(false);
                }
                finally { DeleteIfExists(archive); }
            }
        }
        finally { NeuralScreenCacheLock.Release(); }

        Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = toolDirectory,
        });
    }

    private const string OneClickUrl =
        "https://github.com/faisalkindi/DLSS5oneclick/releases/download/v0.13.14/dlss5oneclick.exe";
    private const string OneClickSha256 =
        "A53159C004CD06F0FB506772EB7319A834067CA3D1D119A399843D22E212BCD6";

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
                    throw new InvalidDataException("OneClick download did not match the official v0.13.14 SHA-256. Nothing was launched.");
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
