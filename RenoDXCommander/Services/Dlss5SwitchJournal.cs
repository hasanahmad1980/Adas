using System.Text.Json;

namespace RenoDXCommander.Services;

/// <summary>Write-ahead recovery for a game-local pipeline switch, not a whole-game backup.</summary>
internal sealed class Dlss5SwitchJournal : IDisposable
{
    private sealed record Entry(string Relative, string? Backup, string? Hash, bool IsDirectory = false, bool Existed = false);
    private sealed class State
    {
        public bool Committed { get; set; }
        public List<Entry> Entries { get; set; } = new();
    }

    private const string Folder = ".adas/switch-recovery";
    private static readonly AsyncLocal<Dlss5SwitchJournal?> Active = new();
    internal static Dlss5SwitchJournal? Current => Active.Value;
    internal static void BeforeWrite(string path)
    {
        var journal = Current;
        if (journal != null && Path.GetFullPath(path).StartsWith(journal._root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            journal.Capture(path);
    }
    private readonly string _root;
    private readonly string _directory;
    private readonly State _state;
    private readonly HashSet<string> _captured = new(StringComparer.OrdinalIgnoreCase);

    internal Dlss5SwitchJournal(string root)
    {
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        _directory = Resolve(_root, Folder);
        if (Current != null || File.Exists(Path.Combine(_directory, "journal.json")))
            throw new InvalidOperationException("An earlier profile switch needs recovery first. Close the game and run Repair.");
        _state = new State();
        Directory.CreateDirectory(_directory);
        Save();
        Active.Value = this;
    }

    private Dlss5SwitchJournal(string root, State state)
    {
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        _directory = Resolve(_root, Folder);
        _state = state;
    }

    internal void Capture(string path)
    {
        var relative = Path.GetRelativePath(_root, Path.GetFullPath(path));
        var full = Resolve(_root, relative);
        if (full.StartsWith(_directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The recovery journal cannot modify itself.");
        if (_captured.Contains(full)) return;
        if (Directory.Exists(full))
        {
            CaptureDirectory(full);
            // Only called for explicitly owned legacy shader folders, never the game root.
            foreach (var child in Directory.EnumerateFileSystemEntries(full)) Capture(child);
            return;
        }
        string? backup = null;
        string? hash = null;
        if (File.Exists(full))
        {
            using (var access = new FileStream(full, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            backup = $"{_state.Entries.Count}.bin";
            var destination = Path.Combine(_directory, backup);
            File.Copy(full, destination, overwrite: true);
            hash = FileHelper.ComputeSha256(destination);
            if (!hash.Equals(FileHelper.ComputeSha256(full), StringComparison.OrdinalIgnoreCase))
                throw new IOException("A file changed while preparing the switch. Close the game and retry.");
        }
        _state.Entries.Add(new(relative, backup, hash));
        Save(); // The undo data is durable before any caller is allowed to mutate the target.
        _captured.Add(full);
    }

    internal void CaptureMove(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            CaptureDirectory(source);
            CaptureDirectory(destination);
            foreach (var child in Directory.EnumerateFileSystemEntries(source))
                CaptureMove(child, Path.Combine(destination, Path.GetFileName(child)));
            return;
        }
        Capture(source);
        Capture(destination);
    }

    private void CaptureDirectory(string path)
    {
        var relative = Path.GetRelativePath(_root, path);
        var full = Resolve(_root, relative);
        var previous = _state.Entries.FindIndex(entry => entry.Relative.Equals(relative, StringComparison.OrdinalIgnoreCase));
        if (previous >= 0)
        {
            var entry = _state.Entries[previous];
            if (!entry.IsDirectory && entry.Backup == null)
                _state.Entries[previous] = entry with { IsDirectory = true };
        }
        else _state.Entries.Add(new(relative, null, null, true, Directory.Exists(full)));
        Save();
        _captured.Add(full);
    }

    internal void Commit()
    {
        _state.Committed = true;
        Save();
        try { Cleanup(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // Committed marker makes cleanup retryable.
    }

    internal static bool Recover(string root)
    {
        var directory = Resolve(root, Folder);
        var manifest = Path.Combine(directory, "journal.json");
        if (!File.Exists(manifest)) return false;
        if (new FileInfo(manifest).Length > 8 * 1024 * 1024)
            throw new InvalidDataException("Invalid profile recovery record.");
        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(manifest))
            ?? throw new InvalidDataException("Invalid profile recovery record.");
        var journal = new Dlss5SwitchJournal(root, state);
        var needsRecovery = !state.Committed;
        if (needsRecovery) journal.Rollback();
        else journal.Cleanup();
        return needsRecovery;
    }

    internal void Rollback()
    {
        // Validate every destination and snapshot before restoring any file.
        foreach (var entry in _state.Entries)
        {
            var target = Resolve(_root, entry.Relative);
            if (target.StartsWith(_directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid recovery destination.");
            if (entry.Backup == null) continue;
            if (Path.GetFileName(entry.Backup) != entry.Backup || !entry.Backup.EndsWith(".bin", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid recovery snapshot name.");
            var snapshot = Resolve(_directory, entry.Backup);
            if (!File.Exists(snapshot) || FileHelper.ComputeSha256(snapshot) != entry.Hash)
                throw new InvalidDataException("A recovery snapshot is missing or changed. The journal was kept; do not remove it.");
        }
        foreach (var entry in _state.Entries.AsEnumerable().Reverse())
        {
            var target = Resolve(_root, entry.Relative);
            if (entry.IsDirectory)
            {
                if (entry.Existed) Directory.CreateDirectory(target);
                else if (Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any()) Directory.Delete(target);
            }
            else if (entry.Backup == null) { if (File.Exists(target)) File.Delete(target); }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(_directory, entry.Backup), target, overwrite: true);
            }
        }
        _state.Committed = true;
        Save();
        try { Cleanup(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void Save()
    {
        var pending = Path.Combine(_directory, "journal.pending");
        using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, _state);
            stream.Flush(flushToDisk: true);
        }
        File.Move(pending, Path.Combine(_directory, "journal.json"), overwrite: true);
    }

    private void Cleanup()
    {
        // Only journal-owned files, never recurse across game data or junctions.
        foreach (var entry in _state.Entries.Where(e => e.Backup != null))
            File.Delete(Resolve(_directory, entry.Backup!));
        File.Delete(Path.Combine(_directory, "journal.pending"));
        File.Delete(Path.Combine(_directory, "journal.json"));
    }

    internal static string Resolve(string root, string relative)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/', '\\').Any(part => part == ".."))
            throw new InvalidDataException("A profile path escapes the game folder.");
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A profile path escapes the game folder.");
        for (var current = full; current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Profile switches cannot follow linked files or folders.");
        }
        return full;
    }

    public void Dispose() { if (Active.Value == this) Active.Value = null; }
}
