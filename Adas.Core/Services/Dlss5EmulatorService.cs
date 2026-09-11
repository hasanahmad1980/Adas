using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

internal sealed record Dlss5EmulatorProfile(string Name, string[] Executables, GraphicsApiType[] Renderers, string Hint);
internal sealed record Dlss5EmulatorInstallation(Dlss5EmulatorProfile Profile, string Executable);

internal static class Dlss5EmulatorService
{
    // Renderer choices informed by DLSS5-Swapper / DLSS5-Autopilot (MIT); see THIRD_PARTY_NOTICES.
    private static readonly GraphicsApiType[] DirectX = { GraphicsApiType.DirectX11, GraphicsApiType.DirectX12, GraphicsApiType.Vulkan, GraphicsApiType.OpenGL };
    private static readonly GraphicsApiType[] VulkanGl = { GraphicsApiType.Vulkan, GraphicsApiType.OpenGL };
    internal static readonly Dlss5EmulatorProfile[] Profiles =
    {
        new("DuckStation", new[] { "duckstation-qt-x64.exe", "duckstation-qt-x64-releaseltcg.exe", "duckstation-nogui-x64.exe", "duckstation.exe" }, DirectX, "Settings → Graphics → Renderer"),
        new("PCSX2", new[] { "pcsx2-qt.exe", "pcsx2x64.exe", "pcsx2x64-avx2.exe", "pcsx2.exe" }, DirectX, "Settings → Graphics → Renderer"),
        new("Dolphin", new[] { "dolphin.exe", "dolphinqt.exe" }, DirectX, "Graphics → Backend"),
        new("PPSSPP", new[] { "ppssppwindows64.exe", "ppssppwindows.exe" }, new[] { GraphicsApiType.DirectX11, GraphicsApiType.Vulkan, GraphicsApiType.OpenGL }, "Settings → Graphics → Backend"),
        new("Xenia", new[] { "xenia.exe", "xenia_canary.exe" }, new[] { GraphicsApiType.DirectX12, GraphicsApiType.Vulkan }, "Match Xenia's configured graphics backend. DirectX 12 is recommended; HUD/motion compatibility varies."),
        new("Cemu", new[] { "cemu.exe" }, VulkanGl, "Options → General settings → Graphics"),
        new("RPCS3", new[] { "rpcs3.exe" }, VulkanGl, "Configuration → GPU → Renderer"),
        new("Ryujinx / Ryubing", new[] { "ryujinx.exe", "ryujinx.ava.exe", "ryujinx.headless.sdl2.exe" }, VulkanGl, "Settings → Graphics → Backend"),
        new("Yuzu / Suyu / Eden / Citron", new[] { "yuzu.exe", "suyu.exe", "eden.exe", "citron.exe", "sudachi.exe" }, VulkanGl, "Graphics → API"),
        new("shadPS4", new[] { "shadps4.exe" }, new[] { GraphicsApiType.Vulkan }, "Vulkan renderer"),
        new("Azahar / Citra / Lime3DS", new[] { "azahar.exe", "citra.exe", "citra-qt.exe", "lime3ds.exe" }, VulkanGl, "Graphics → API"),
        new("melonDS", new[] { "melonds.exe" }, new[] { GraphicsApiType.OpenGL }, "Use the hardware OpenGL renderer, not software rendering"),
        new("Flycast", new[] { "flycast.exe" }, new[] { GraphicsApiType.DirectX11, GraphicsApiType.Vulkan, GraphicsApiType.OpenGL }, "Video → Renderer"),
        new("xemu", new[] { "xemu.exe" }, VulkanGl, "Match the backend supported by your installed xemu build"),
        new("Vita3K", new[] { "vita3k.exe" }, VulkanGl, "Settings → GPU → Backend renderer"),
        new("RetroArch", new[] { "retroarch.exe" }, DirectX, "Settings → Drivers → Video (the selected core must support this driver)"),
        new("mGBA", new[] { "mgba.exe" }, new[] { GraphicsApiType.OpenGL }, "Use the OpenGL renderer"),
        new("Snes9x", new[] { "snes9x-x64.exe", "snes9x.exe" }, new[] { GraphicsApiType.DirectX9 }, "Display configuration → Output method → Direct3D"),
        new("Play!", new[] { "play.exe" }, VulkanGl, "Graphics → Renderer"),
    };

    internal static Dlss5EmulatorProfile? ForExecutable(string path)
        => Profiles.FirstOrDefault(profile => profile.Executables.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase));

    internal static Dlss5EmulatorInstallation? FindInstallation(string root, string? settingsRoot = null)
    {
        var candidates = FindCandidates(root);
        if (candidates.Select(item => item.Profile.Name).Distinct().Count() != 1) return null;
        try
        {
            var preferred = SettingsPath(root, settingsRoot) + ".exe.json";
            if (File.Exists(preferred))
            {
                var executable = JsonSerializer.Deserialize<string>(File.ReadAllText(preferred));
                var match = candidates.FirstOrDefault(item => item.Executable.Equals(executable, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return candidates.FirstOrDefault();
    }

    internal static IReadOnlyList<Dlss5EmulatorInstallation> FindCandidates(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<Dlss5EmulatorInstallation>();
        var candidates = new[] { root, Path.Combine(root, "bin") }.Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly))
            .Select(path => (Path: path, Profile: ForExecutable(path))).Where(item => item.Profile != null).ToArray();
        return candidates.OrderBy(item => Array.FindIndex(item.Profile!.Executables,
            name => name.Equals(Path.GetFileName(item.Path), StringComparison.OrdinalIgnoreCase)))
            .Select(item => new Dlss5EmulatorInstallation(item.Profile!, item.Path)).ToArray();
    }

    private static string SettingsPath(string executable, string? settingsRoot)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(executable).ToUpperInvariant())));
        return Path.Combine(settingsRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "Adas", "EmulatorRenderers"), key + ".json");
    }

    internal static GraphicsApiType? LoadRenderer(Dlss5EmulatorInstallation installation, string? settingsRoot = null)
    {
        try
        {
            var path = SettingsPath(installation.Executable, settingsRoot);
            if (!File.Exists(path)) return null;
            var api = JsonSerializer.Deserialize<GraphicsApiType>(File.ReadAllText(path));
            return installation.Profile.Renderers.Contains(api) ? api : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    internal static void SaveRenderer(Dlss5EmulatorInstallation installation, GraphicsApiType api, string? settingsRoot = null)
    {
        if (!installation.Profile.Renderers.Contains(api)) throw new ArgumentException("This renderer is not supported by this emulator profile.");
        var path = SettingsPath(installation.Executable, settingsRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pending = path + ".tmp";
        File.WriteAllText(pending, JsonSerializer.Serialize(api));
        File.Move(pending, path, overwrite: true);
    }

    internal static void SaveExecutable(string root, Dlss5EmulatorInstallation installation, string? settingsRoot = null)
    {
        if (!FindCandidates(root).Any(item => item.Executable.Equals(installation.Executable, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Select an emulator executable inside this installation.");
        var path = SettingsPath(root, settingsRoot) + ".exe.json";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(installation.Executable));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
