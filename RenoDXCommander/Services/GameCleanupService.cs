using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

internal sealed record GameCleanupLeftovers(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Directories);

internal sealed record GameCleanupPlan(
    string GameRoot,
    IReadOnlyList<string> Dlss5Deployments,
    IReadOnlyList<AuxInstalledRecord> ReShadeRecords,
    GameCleanupLeftovers Leftovers)
{
    public int ItemCount => Dlss5Deployments.Count + ReShadeRecords.Count
                            + Leftovers.Files.Count + Leftovers.Directories.Count;
}

internal sealed record GameCleanupResult(
    int ManagedInstallationsRemoved,
    int LeftoversArchived,
    string? RecoveryPath,
    IReadOnlyList<string> Errors);

/// <summary>
/// Removes DLSS 5 and ReShade from an entire game tree. Tracked originals are
/// restored first; recognizable untracked leftovers are moved to AppData so
/// the game folder is clean without destroying uncertain files.
/// </summary>
internal sealed class GameCleanupService
{
    private static readonly HashSet<string> KnownFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "renodx-dlss.addon64", "renodx-dlss5.addon64", "renodx-dlss5(2).addon64",
        "renodx-dlss.addon32", "renodx-dlss5.addon32", "renodx-mfgunlock.addon64",
        "dlss5-feed.addon64", "dlss5-feed.addon32", "dlss5-feed-host64.exe",
        "dlss5-bridge.addon64", "dlss5-dx11-bridge.addon64", "dlss5-dx11-bridge.addon32",
        "standalone-dlssnr.addon64", "DLSS5_Feed.fx", "DLSS5_AIO_Feed.fx",
        "dlss5-feed.cfg", "dlss5-feed.log", "dlss5-bridge.cfg", "dlss5-bridge.log",
        "dlss5-dx11-bridge.cfg", "dlss5-dx11-bridge.log", "nvngx_dlssnr.dll", "nvngx.dll",
        "VkLayer_feed_vk.dll", "VkLayer_feed_vk32.dll", "VkLayer_feed_vk.json",
        "VkLayer_feed_vk32.json", "run-with-feed-layer.bat", "run-with-feed-layer32.bat",
    };

    private static readonly HashSet<string> ReShadeProxyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dxgi.dll", "d3d8.dll", "d3d9.dll", "d3d10.dll", "d3d10_1.dll",
        "d3d10core.dll", "d3d11.dll", "d3d12.dll", "opengl32.dll", "winmm.dll",
        "ReShade32.dll", "ReShade64.dll",
    };

    private readonly IAuxInstallService _auxInstallService;
    private readonly IAddonPackService _addonPackService;
    private readonly ICrashReporter _crashReporter;

    public GameCleanupService(
        IAuxInstallService auxInstallService,
        IAddonPackService addonPackService,
        ICrashReporter crashReporter)
    {
        _auxInstallService = auxInstallService;
        _addonPackService = addonPackService;
        _crashReporter = crashReporter;
    }

    public GameCleanupPlan CreatePlan(string gameRoot)
    {
        var root = NormalizeGameRoot(gameRoot);
        var records = _auxInstallService.LoadAll()
            .Where(record => IsReShadeRecord(record) && IsAtOrBelow(root, record.InstallPath))
            .ToArray();
        return new(
            root,
            Dlss5ComponentService.FindInstalledDeploymentPaths(root, int.MaxValue),
            records,
            FindKnownLeftovers(root, AuxInstallService.IsReShadeFileStrict));
    }

    public GameCleanupResult Execute(GameCleanupPlan plan, string gameName)
    {
        var root = NormalizeGameRoot(plan.GameRoot);
        var errors = new List<string>();
        var managedRemoved = 0;

        foreach (var deployment in Dlss5ComponentService.FindInstalledDeploymentPaths(root, int.MaxValue)
                     .OrderByDescending(path => path.Length))
        {
            var uninstallErrors = Dlss5ComponentService.UninstallTrackedFiles(deployment, _crashReporter);
            errors.AddRange(uninstallErrors);
            if (uninstallErrors.Count == 0) managedRemoved++;
        }

        foreach (var record in _auxInstallService.LoadAll()
                     .Where(record => IsReShadeRecord(record) && IsAtOrBelow(root, record.InstallPath))
                     .ToArray())
        {
            try
            {
                _addonPackService.DeployAddonsForGame(
                    gameName, record.InstallPath, is32Bit: false,
                    useGlobalSet: true, perGameSelection: new List<string>());
                _auxInstallService.Uninstall(record);
                managedRemoved++;
            }
            catch (Exception ex)
            {
                errors.Add($"{record.InstallPath}: {ex.Message}");
                _crashReporter.Log($"[GameCleanupService] ReShade removal failed for '{record.InstallPath}' — {ex.Message}");
            }
        }

        var leftovers = FindKnownLeftovers(root, AuxInstallService.IsReShadeFileStrict);
        if (errors.Count > 0)
        {
            leftovers = leftovers with
            {
                Directories = leftovers.Directories
                    .Where(path => !Path.GetFileName(path).Equals(".adas", StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
            };
        }

        string? recoveryPath = null;
        var archived = 0;
        if (leftovers.Files.Count > 0 || leftovers.Directories.Count > 0)
        {
            recoveryPath = BuildRecoveryPath(gameName);
            try
            {
                archived = ArchiveLeftovers(root, leftovers, recoveryPath, errors).Count;
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
                _crashReporter.Log($"[GameCleanupService] Leftover cleanup failed — {ex}");
            }
        }

        RestoreOriginalShaderFolders(root, errors);
        _crashReporter.Log($"[GameCleanupService] '{gameName}': managed={managedRemoved}, archived={archived}, errors={errors.Count}");
        return new(managedRemoved, archived, archived > 0 ? recoveryPath : null, errors);
    }

    internal static GameCleanupLeftovers FindKnownLeftovers(
        string gameRoot,
        Func<string, bool> isReShadeProxy)
    {
        var root = NormalizeGameRoot(gameRoot);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in EnumerateDirectories(root))
        {
            var directoryName = Path.GetFileName(directory);
            if (!directory.Equals(root, StringComparison.OrdinalIgnoreCase)
                && (directoryName.Equals("reshade-shaders", StringComparison.OrdinalIgnoreCase)
                    || directoryName.Equals(".adas", StringComparison.OrdinalIgnoreCase)))
            {
                directories.Add(directory);
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    var name = Path.GetFileName(file);
                    var extension = Path.GetExtension(name);
                    var isReShadeSidecar = name.StartsWith("ReShade", StringComparison.OrdinalIgnoreCase)
                        && (extension.Equals(".ini", StringComparison.OrdinalIgnoreCase)
                            || extension.Equals(".log", StringComparison.OrdinalIgnoreCase));
                    var isDlss5Addon = (name.StartsWith("renodx-dlss", StringComparison.OrdinalIgnoreCase)
                                       || name.StartsWith("dlss5-", StringComparison.OrdinalIgnoreCase)
                                       || name.StartsWith("standalone-dlssnr", StringComparison.OrdinalIgnoreCase))
                                      && (extension.Equals(".addon64", StringComparison.OrdinalIgnoreCase)
                                          || extension.Equals(".addon32", StringComparison.OrdinalIgnoreCase));
                    if (KnownFileNames.Contains(name) || isReShadeSidecar || isDlss5Addon
                        || (ReShadeProxyNames.Contains(name) && isReShadeProxy(file)))
                        files.Add(file);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return new(files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            directories.OrderByDescending(path => path.Length).ToArray());
    }

    internal static IReadOnlyList<string> ArchiveLeftovers(
        string gameRoot,
        GameCleanupLeftovers leftovers,
        string recoveryRoot,
        List<string>? errors = null)
    {
        var root = NormalizeGameRoot(gameRoot);
        var recovery = Path.GetFullPath(recoveryRoot);
        if (IsAtOrBelow(root, recovery))
            throw new InvalidOperationException("The cleanup recovery folder must be outside the game folder.");

        var archived = new List<string>();
        foreach (var file in leftovers.Files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(file)) continue;
            try
            {
                EnsureSafeSource(root, file);
                var destination = GetAvailableRecoveryPath(root, recovery, file);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(file, destination);
                archived.Add(destination);
                if (ReShadeProxyNames.Contains(Path.GetFileName(file)))
                    AuxInstallService.RestoreForeignDll(file);
            }
            catch (Exception ex)
            {
                if (errors == null) throw;
                errors.Add($"{file}: {ex.Message}");
            }
        }

        foreach (var directory in leftovers.Directories
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(path => path.Length))
        {
            if (!Directory.Exists(directory)) continue;
            try
            {
                EnsureSafeSource(root, directory);
                var destination = GetAvailableRecoveryPath(root, recovery, directory);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                Directory.Move(directory, destination);
                archived.Add(destination);
            }
            catch (Exception ex)
            {
                if (errors == null) throw;
                errors.Add($"{directory}: {ex.Message}");
            }
        }
        return archived;
    }

    private static IEnumerable<string> EnumerateDirectories(string root)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();
            yield return directory;
            var name = Path.GetFileName(directory);
            if (!directory.Equals(root, StringComparison.OrdinalIgnoreCase)
                && (name.Equals("reshade-shaders", StringComparison.OrdinalIgnoreCase)
                    || name.Equals(".adas", StringComparison.OrdinalIgnoreCase)))
                continue;
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(directory).ToArray(); }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }
            foreach (var child in children)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        pending.Enqueue(child);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static string NormalizeGameRoot(string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            throw new ArgumentException("The game folder is missing.", nameof(gameRoot));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if (root.Equals(Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A drive root cannot be cleaned.");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("A linked game root cannot be cleaned safely.");
        return root;
    }

    private static bool IsReShadeRecord(AuxInstalledRecord record)
        => record.AddonType.Equals(AuxInstallService.TypeReShade, StringComparison.OrdinalIgnoreCase)
           || record.AddonType.Equals(AuxInstallService.TypeReShadeNormal, StringComparison.OrdinalIgnoreCase);

    private static bool IsAtOrBelow(string root, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        return canonical.Equals(root, StringComparison.OrdinalIgnoreCase)
               || canonical.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureSafeSource(string root, string source)
    {
        var canonical = Path.GetFullPath(source);
        if (!IsAtOrBelow(root, canonical) || canonical.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cleanup path is outside the selected game: {source}");
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, canonical).Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Cleanup will not follow linked paths: {source}");
        }
    }

    private static string GetAvailableRecoveryPath(string root, string recoveryRoot, string source)
    {
        var relative = Path.GetRelativePath(root, source);
        var destination = Path.Combine(recoveryRoot, relative);
        if (!File.Exists(destination) && !Directory.Exists(destination)) return destination;
        return destination + $".{Guid.NewGuid():N}.recovered";
    }

    private static string BuildRecoveryPath(string gameName)
    {
        var safeName = AuxInstallService.SanitizeDirectoryName(gameName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Game";
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "Adas", "CleanupBackups", safeName,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
    }

    private static void RestoreOriginalShaderFolders(string root, List<string> errors)
    {
        foreach (var original in EnumerateDirectories(root)
                     .Where(path => Path.GetFileName(path).Equals("reshade-shaders-original", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            var destination = Path.Combine(Path.GetDirectoryName(original)!, "reshade-shaders");
            if (Directory.Exists(destination)) continue;
            try { Directory.Move(original, destination); }
            catch (Exception ex) { errors.Add($"{original}: {ex.Message}"); }
        }
    }
}
