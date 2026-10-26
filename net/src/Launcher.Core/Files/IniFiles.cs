using System.Text;

namespace Launcher.Core;

/// <summary>An emulator settings file as it stands on disk: its lines, each character one byte of the file (so a
/// byte that is no UTF-8 comes back out as it went in), whether it starts with a byte order mark, and how its lines
/// end.</summary>
public sealed record IniText(IReadOnlyList<string> Lines, bool Mark = false, string End = "\n")
{
    /// <summary>A file that is not there: no lines, and the line ends of this system for when it is made.</summary>
    public static IniText None { get; } = new([], false, Environment.NewLine);
}

/// <summary>
/// The emulator's settings files read and written without loss. The launcher changes a few lines it knows in files
/// that are the game's and the player's: every other line must come through a write byte for byte, whatever letters
/// it holds (a file edited by hand in the system's own code page is not UTF-8), with the file's own line ends and its
/// mark, if it has one. A write goes beside the file and is moved over it, so one that is cut short, or fails, leaves
/// the file as it was and nothing beside it.
/// </summary>
public static class IniFiles
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The file's text; <see cref="IniText.None"/> when it is not there.</summary>
    /// <exception cref="IOException">It is longer than <paramref name="maxBytes"/>, or cannot be read.</exception>
    public static IniText Read(string file, int maxBytes)
    {
        if (!File.Exists(file))
        {
            return IniText.None;
        }
        if (new FileInfo(file).Length > maxBytes)
        {
            throw new IOException($"{Path.GetFileName(file)} is longer than {maxBytes / 1024} KiB");
        }
        var bytes = File.ReadAllBytes(file).AsSpan();
        var mark = bytes.StartsWith(Encoding.UTF8.Preamble);
        var text = Encoding.Latin1.GetString(mark ? bytes[3..] : bytes);
        var end = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\n', StringComparison.Ordinal) ? "\n" : Environment.NewLine;
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);   // the last line's own end
        }
        return new IniText(lines, mark, end);
    }

    /// <summary>Writes the lines in the place of the file that <paramref name="was"/> read: its mark, its line ends.
    /// Raises what a write can; nothing is left beside the file when it does.</summary>
    public static void Write(string file, IniText was, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(was);
        ArgumentNullException.ThrowIfNull(lines);
        var text = Encoding.Latin1.GetBytes(string.Concat(lines.Select(l => l + was.End)));
        Online.Protocol.AtomicFile.WriteAllBytes(file, was.Mark ? [.. Encoding.UTF8.Preamble, .. text] : text);
    }

    /// <summary>A value of the file as text: the file's bytes read as UTF-8, as the emulator reads a name; bytes
    /// that are no UTF-8 stay the characters they were read as.</summary>
    public static string FromFile(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.All(c => c < 0x80) || value.Any(c => c > 0xFF))
        {
            return value;
        }
        try
        {
            return Strict.GetString(Encoding.Latin1.GetBytes(value));
        }
        catch (ArgumentException)
        {
            return value;
        }
    }

    /// <summary>A text as the file holds it: its UTF-8 bytes, a character a byte.</summary>
    public static string ToFile(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.All(c => c < 0x80) ? value : Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(value));
    }
}

/// <summary>The lines of an emulator settings file (<c>[Section]</c>, <c>Key = Value</c>), read and changed one key
/// at a time, as the emulator reads them (its Common/IniFile.cpp): names without regard to case, every section of
/// one name one section, and of a key written more than once the last line.</summary>
public static class IniLines
{
    /// <summary>A key's value, trimmed; null when the section or the key is not there.</summary>
    public static string? Get(IReadOnlyList<string> lines, string section, string key)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var at = All(lines, section, key);
        return at.Count == 0 ? null : lines[at[^1]].Split('=', 2)[1].Trim();
    }

    /// <summary>The lines with a key set, or, with no value, taken out. One line of the key is left, the one the
    /// emulator would read (the last), so what is set is what is in force; a line that says the value already is
    /// left as it stands. A key that is not there is added at the end of its section (the last of that name; a new
    /// one at the file's end when there is none). Every other line is kept, in its place.</summary>
    public static IReadOnlyList<string> Set(IReadOnlyList<string> lines, string section, string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var result = new List<string>(lines);
        var at = All(result, section, key);
        if (value is not null && at.Count > 0)
        {
            var last = at[^1];
            if (result[last].Split('=', 2)[1].Trim() != value)
            {
                result[last] = $"{key} = {value}";
            }
            at.RemoveAt(at.Count - 1);
        }
        for (var i = at.Count - 1; i >= 0; i--)
        {
            result.RemoveAt(at[i]);   // the lines before it said nothing any more, or the key is taken out
        }
        if (value is null || at.Count > 0 || All(result, section, key).Count > 0)
        {
            return result;
        }
        var (start, end) = LastSection(result, section);
        if (start < 0)
        {
            result.Add($"[{section}]");
            result.Add($"{key} = {value}");
            return result;
        }
        // after the section's last line that says something: blank lines before the next section stay where they are
        while (end > start && string.IsNullOrWhiteSpace(result[end - 1]))
        {
            end--;
        }
        result.Insert(end, $"{key} = {value}");
        return result;
    }

    /// <summary>Every line of a key, in every section of the name, in the file's order.</summary>
    private static List<int> All(IReadOnlyList<string> lines, string section, string key)
    {
        var found = new List<int>();
        var inside = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('['))
            {
                inside = Names(t, section);
            }
            else if (inside && lines[i].Split('=', 2) is { Length: 2 } parts && parts[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(i);
            }
        }
        return found;
    }

    /// <summary>The last section of a name: the first line after its header, and one past its last; (-1, -1) when
    /// there is none.</summary>
    private static (int Start, int End) LastSection(List<string> lines, string section)
    {
        var (start, end) = (-1, -1);
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (!t.StartsWith('['))
            {
                continue;
            }
            if (start >= 0 && end < 0)
            {
                end = i;
            }
            if (Names(t, section))
            {
                (start, end) = (i + 1, -1);
            }
        }
        return start < 0 ? (-1, -1) : (start, end < 0 ? lines.Count : end);
    }

    private static bool Names(string header, string section) => header.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
}
