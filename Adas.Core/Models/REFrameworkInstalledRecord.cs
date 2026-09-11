namespace RenoDXCommander.Models;

public class REFrameworkInstalledRecord
{
    public string GameName { get; set; } = "";
    public string InstallPath { get; set; } = "";
    /// <summary>Store/platform where this game is installed (Steam, Xbox, Epic, etc.).</summary>
    public string Store { get; set; } = "";
    public string InstalledVersion { get; set; } = "";
    public DateTime InstalledAt { get; set; }
}
