namespace RenoDXCommander.Models;

public class ShaderProfile
{
    public string Name { get; set; } = "Profile";
    public List<string> SelectedPacks { get; set; } = new();
    // packId → list of excluded filenames
    public Dictionary<string, List<string>> FileExclusions { get; set; } = new();
}
