using System.Diagnostics;

namespace RenoDXCommander.Services;

public sealed partial class Dlss5ComponentService
{
    private const string OneClickUrl =
        "https://github.com/faisalkindi/DLSS5oneclick/releases/download/v0.11.15/dlss5oneclick.exe";
    private const string OneClickSha256 =
        "5815439F1524DA02E934BE9C4CF77FFEC6E3C05E0A05AC7030B6DD1B3043EDB8";

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
                    throw new InvalidDataException("OneClick download did not match the official v0.11.15 SHA-256. Nothing was launched.");
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
