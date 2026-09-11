using System.Collections.Concurrent;
using System.Security;

namespace RenoDXCommander.Services;

/// <summary>PE machine architecture values from the COFF header.</summary>
public enum MachineType : ushort
{
    Native  = 0x0000,
    I386    = 0x014C,
    Itanium = 0x0200,
    x64     = 0x8664,
}

public class PeHeaderService : IPeHeaderService
{
    private readonly record struct ArchitectureCacheEntry(long Length, long LastWriteTicks, MachineType Architecture);
    private readonly record struct ExecutableCacheEntry(string? Path, long ExpiresUtcTicks);

    private static readonly ConcurrentDictionary<string, ArchitectureCacheEntry> ArchitectureCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ExecutableCacheEntry> ExecutableCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ExcludedExecutableDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".adas", "host64", "_CommonRedist", "_Redist", "redist", "redistributable",
        "installer", "installers", "EasyAntiCheat", "BattlEye", "CrashReporter",
    };

    private const int PeHeaderBufferSize = 4096;

    /// <summary>
    /// Reads the PE header of the given file and returns its MachineType.
    /// Returns MachineType.Native on any error (missing file, invalid PE, I/O).
    /// Reads at most 4096 bytes.
    /// </summary>
    public MachineType DetectArchitecture(string exePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(exePath);
            var fileInfo = new FileInfo(fullPath);
            if (ArchitectureCache.TryGetValue(fullPath, out var cached)
                && cached.Length == fileInfo.Length
                && cached.LastWriteTicks == fileInfo.LastWriteTimeUtc.Ticks)
                return cached.Architecture;

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[PeHeaderBufferSize];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            // Validate MZ signature at offset 0
            if (bytesRead < 2 || buffer[0] != (byte)'M' || buffer[1] != (byte)'Z')
            {
                CrashReporter.Log($"[PeHeaderService] Invalid MZ signature in '{exePath}'");
                return MachineType.Native;
            }

            // Read e_lfanew at offset 0x3C (Int32 — offset to PE header)
            if (bytesRead < 0x3C + 4)
            {
                CrashReporter.Log($"[PeHeaderService] File too small to contain e_lfanew: '{exePath}'");
                return MachineType.Native;
            }

            int peOffset = BitConverter.ToInt32(buffer, 0x3C);

            // Validate PE signature at peOffset (bytes 'P','E',0,0)
            if (peOffset < 0 || peOffset + 6 > bytesRead)
            {
                CrashReporter.Log($"[PeHeaderService] PE offset out of range ({peOffset}) in '{exePath}'");
                return MachineType.Native;
            }

            if (buffer[peOffset] != (byte)'P' || buffer[peOffset + 1] != (byte)'E' ||
                buffer[peOffset + 2] != 0 || buffer[peOffset + 3] != 0)
            {
                CrashReporter.Log($"[PeHeaderService] Invalid PE signature in '{exePath}'");
                return MachineType.Native;
            }

            // Read Machine field at PE offset + 4 (UInt16)
            ushort machineValue = BitConverter.ToUInt16(buffer, peOffset + 4);
            var machineType = (MachineType)machineValue;

            ArchitectureCache[fullPath] = new(fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks, machineType);
            if (CrashReporter.VerboseLogging)
                CrashReporter.Log($"[PeHeaderService] Detected {machineType} (0x{machineValue:X4}) for '{exePath}'");
            return machineType;
        }
        catch (FileNotFoundException)
        {
            CrashReporter.Log($"[PeHeaderService] File not found: '{exePath}'");
            return MachineType.Native;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            CrashReporter.Log($"[PeHeaderService] I/O error reading '{exePath}': {ex.Message}");
            return MachineType.Native;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PeHeaderService] Unexpected error reading '{exePath}': {ex.Message}");
            return MachineType.Native;
        }
    }

    /// <summary>
    /// Searches the game directory and common nested binary folders for .exe
    /// files, returning the largest likely game executable. Installer, crash,
    /// anti-cheat, and suite helper folders are ignored.
    /// </summary>
    public string? FindGameExe(string installPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(installPath);
            if (ExecutableCache.TryGetValue(fullPath, out var cached)
                && cached.ExpiresUtcTicks > DateTime.UtcNow.Ticks
                && (cached.Path == null || File.Exists(cached.Path)))
                return cached.Path;

            var dir = new DirectoryInfo(fullPath);
            if (!dir.Exists)
            {
                CrashReporter.Log($"[PeHeaderService] Install directory does not exist: '{installPath}'");
                return null;
            }

            var exeFiles = EnumerateExecutableFiles(dir, maxDepth: 5).ToArray();
            if (exeFiles.Length == 0)
            {
                CrashReporter.Log($"[PeHeaderService] No .exe files found in '{installPath}'");
                ExecutableCache[fullPath] = new(null, DateTime.UtcNow.AddSeconds(10).Ticks);
                return null;
            }

            var likelyGameExecutables = exeFiles
                .Where(file => !IsHelperExecutable(file.Name))
                .ToArray();
            var candidates = likelyGameExecutables.Length > 0 ? likelyGameExecutables : exeFiles;

            var largest = candidates[0];
            for (int i = 1; i < candidates.Length; i++)
            {
                if (candidates[i].Length > largest.Length)
                    largest = candidates[i];
            }

            ExecutableCache[fullPath] = new(largest.FullName, DateTime.UtcNow.AddMinutes(2).Ticks);
            return largest.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            CrashReporter.Log($"[PeHeaderService] Error accessing directory '{installPath}': {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PeHeaderService] Unexpected error scanning '{installPath}': {ex.Message}");
            return null;
        }
    }

    private static IEnumerable<FileInfo> EnumerateExecutableFiles(DirectoryInfo root, int maxDepth)
    {
        var pending = new Queue<(DirectoryInfo Directory, int Depth)>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0)
        {
            var (directory, depth) = pending.Dequeue();
            FileInfo[] files;
            try { files = directory.GetFiles("*.exe", SearchOption.TopDirectoryOnly); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                continue;
            }

            foreach (var file in files)
                yield return file;

            if (depth >= maxDepth) continue;

            DirectoryInfo[] children;
            try { children = directory.GetDirectories(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                continue;
            }

            foreach (var child in children)
            {
                try
                {
                    if ((child.Attributes & FileAttributes.ReparsePoint) != 0
                        || ExcludedExecutableDirectories.Contains(child.Name))
                        continue;
                    pending.Enqueue((child, depth + 1));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { }
            }
        }
    }

    private static bool IsHelperExecutable(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return name.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("VC_redist", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("vcredist", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CrashReporter", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CrashReportClient", StringComparison.OrdinalIgnoreCase)
            || name.Equals("CrashSender", StringComparison.OrdinalIgnoreCase)
            || name.Equals("dxwebsetup", StringComparison.OrdinalIgnoreCase)
            || name.Equals("support", StringComparison.OrdinalIgnoreCase)
            || name.Equals("dlss5-feed-host64", StringComparison.OrdinalIgnoreCase)
            || name.Equals("launcher", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Convenience: finds the game exe and detects its architecture.
    /// Returns MachineType.Native if no exe is found.
    /// </summary>
    public MachineType DetectGameArchitecture(string installPath)
    {
        string? exePath = FindGameExe(installPath);
        if (exePath is null)
        {
            CrashReporter.Log($"[PeHeaderService] No game executable found in '{installPath}', defaulting to Native");
            return MachineType.Native;
        }

        return DetectArchitecture(exePath);
    }
}
