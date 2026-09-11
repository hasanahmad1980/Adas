using System.Text;

namespace RenoDXCommander.Services;

/// <summary>
/// Minimal, formatting-preserving INI editor for files consumed by ReShade.
/// ReShade treats some key names as case-sensitive, so managed writes always
/// use the canonical spelling while retaining unrelated comments and lines.
/// </summary>
internal sealed class IniTextDocument
{
    internal sealed record Value(string Key, string Text);

    private readonly List<string> _lines;
    private readonly Encoding _encoding;
    private readonly string _newLine;
    private readonly bool _trailingNewLine;

    private IniTextDocument(List<string> lines, Encoding encoding, string newLine, bool trailingNewLine)
    {
        _lines = lines;
        _encoding = encoding;
        _newLine = newLine;
        _trailingNewLine = trailingNewLine;
    }

    public static IniTextDocument Load(string path)
    {
        if (!File.Exists(path))
            return new IniTextDocument(new List<string>(), new UTF8Encoding(false), Environment.NewLine, false);

        var bytes = File.ReadAllBytes(path);
        var (encoding, preambleLength) = DetectEncoding(bytes);
        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var trailingNewLine = text.EndsWith("\n", StringComparison.Ordinal);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n').ToList();
        if (trailingNewLine && lines.Count > 0)
            lines.RemoveAt(lines.Count - 1);
        return new IniTextDocument(lines, encoding, newLine, trailingNewLine);
    }

    public bool TryGetValue(string section, string key, out Value value)
    {
        foreach (var index in FindKeyLines(section, key))
        {
            var separator = _lines[index].IndexOf('=');
            var actualKey = _lines[index][..separator].Trim();
            value = new Value(actualKey, _lines[index][(separator + 1)..]);
            return true;
        }

        value = new Value(key, "");
        return false;
    }

    public void SetValue(string section, string key, string value)
    {
        var matches = FindKeyLines(section, key).ToArray();
        if (matches.Length > 0)
        {
            var first = matches[0];
            var indentationLength = _lines[first].Length - _lines[first].TrimStart().Length;
            var indentation = _lines[first][..indentationLength];
            _lines[first] = $"{indentation}{key}={value}";
            for (var index = matches.Length - 1; index > 0; index--)
                _lines.RemoveAt(matches[index]);
            return;
        }

        var (found, insertAt) = FindSectionInsertionPoint(section);
        if (!found)
        {
            if (_lines.Count > 0 && _lines[^1].Length > 0)
                _lines.Add("");
            if (section.Length > 0)
                _lines.Add($"[{section}]");
            insertAt = _lines.Count;
        }
        _lines.Insert(insertAt, $"{key}={value}");
    }

    public void RemoveValue(string section, string key)
    {
        foreach (var index in FindKeyLines(section, key).Reverse())
            _lines.RemoveAt(index);
    }

    public void Save(string path)
    {
        Dlss5SwitchJournal.BeforeWrite(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = string.Join(_newLine, _lines);
        if (_trailingNewLine && (_lines.Count > 0 || text.Length > 0))
            text += _newLine;

        var preamble = _encoding.GetPreamble();
        var content = _encoding.GetBytes(text);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (preamble.Length > 0) stream.Write(preamble);
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private IEnumerable<int> FindKeyLines(string section, string key)
    {
        var currentSection = "";
        for (var index = 0; index < _lines.Count; index++)
        {
            if (TryParseSection(_lines[index], out var parsedSection))
            {
                currentSection = parsedSection;
                continue;
            }
            if (!currentSection.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;

            var trimmed = _lines[index].TrimStart();
            if (trimmed.Length == 0 || trimmed[0] is ';' or '#') continue;
            var separator = trimmed.IndexOf('=');
            if (separator <= 0) continue;
            if (trimmed[..separator].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                yield return index;
        }
    }

    internal IEnumerable<(string Section, string Key, string Text)> Values()
    {
        var section = "";
        foreach (var line in _lines)
        {
            if (TryParseSection(line, out var parsed)) { section = parsed; continue; }
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] is ';' or '#') continue;
            var separator = trimmed.IndexOf('=');
            if (separator > 0) yield return (section, trimmed[..separator].Trim(), trimmed[(separator + 1)..]);
        }
    }

    private (bool Found, int InsertAt) FindSectionInsertionPoint(string section)
    {
        if (section.Length == 0)
        {
            var firstSection = _lines.FindIndex(line => TryParseSection(line, out _));
            return (true, firstSection < 0 ? _lines.Count : firstSection);
        }

        var found = false;
        var insertAt = _lines.Count;
        for (var index = 0; index < _lines.Count; index++)
        {
            if (!TryParseSection(_lines[index], out var parsedSection)) continue;
            if (found) return (true, index);
            if (parsedSection.Equals(section, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                insertAt = index + 1;
            }
        }
        return (found, insertAt);
    }

    private static bool TryParseSection(string line, out string section)
    {
        var trimmed = line.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[')
        {
            var close = trimmed.IndexOf(']');
            if (close > 1)
            {
                section = trimmed[1..close].Trim();
                return true;
            }
        }
        section = "";
        return false;
    }

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()))
            return (new UTF8Encoding(true), Encoding.UTF8.GetPreamble().Length);
        if (bytes.AsSpan().StartsWith(Encoding.Unicode.GetPreamble()))
            return (Encoding.Unicode, Encoding.Unicode.GetPreamble().Length);
        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.GetPreamble()))
            return (Encoding.BigEndianUnicode, Encoding.BigEndianUnicode.GetPreamble().Length);
        return (new UTF8Encoding(false), 0);
    }
}
