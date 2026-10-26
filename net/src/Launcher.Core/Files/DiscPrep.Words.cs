using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace Launcher.Core;

// The words written into the prepared copy: the pins and the bundle's patch list.
public static partial class DiscPrep
{
    /// <summary>The executable's words of a native bundle's patch list (<c>patches.json</c>, written by
    /// port/image/build.py: address, stock word, new word, what; the list the runner checks the executable against
    /// before it boots): the entries below <c>main_rel</c>, each with its place in the executable's file. main.rel's
    /// words are not the launcher's: the game program writes them when main.rel is linked. Empty for no bundle or a
    /// bundle without a list (the fork's kind).</summary>
    /// <param name="dol">The executable, for its section table (stock or patched: the table is the same).</param>
    /// <exception cref="InvalidDataException">The list cannot be used; the message says why.</exception>
    public static (IReadOnlyList<PatchWord> Words, string? Image, int? Version) ExecutableWords(string? bundle, ReadOnlySpan<byte> dol)
    {
        var list = bundle is null ? null : Path.Combine(bundle, PatchListName);
        if (list is null || !File.Exists(list))
        {
            return ([], null, null);
        }
        try
        {
            using var doc = JsonDocument.Parse(Small(list, 4 << 20));
            var root = doc.RootElement;
            if (!root.TryGetProperty("complete", out var complete) || complete.ValueKind != JsonValueKind.True)
            {
                throw new InvalidDataException("The game program's patch list is incomplete (it was built without the game's files).");
            }
            var mainRel = DiscPins.Hex(root.GetProperty("main_rel").GetString()!);
            var words = new List<PatchWord>();
            foreach (var patch in root.GetProperty("patches").EnumerateArray())
            {
                var address = DiscPins.Hex(patch.GetProperty("address").GetString()!);
                if (address >= mainRel)
                {
                    continue;
                }
                var what = patch.TryGetProperty("what", out var w) ? w.GetString() ?? "" : "";
                words.Add(new PatchWord(Executable, FileOffset(dol, address, what), DiscPins.Hex(patch.GetProperty("original").GetString()!),
                    DiscPins.Hex(patch.GetProperty("new").GetString()!), what, address));
            }
            return (words, root.TryGetProperty("image_sha1", out var image) ? image.GetString() : null,
                root.TryGetProperty("version", out var version) && version.TryGetInt32(out var v) ? v : null);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException
            or ArgumentNullException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("The game program's patch list cannot be read: " + e.Message, e);
        }
    }

    /// <summary>Every word a prepared game holds for this bundle: the pins and the bundle's executable words, by file
    /// and offset. A word both name (the executable's pin is in the native list too) must be the same in both.</summary>
    /// <exception cref="InvalidDataException">The list cannot be used, or it and the pins disagree.</exception>
    public static (IReadOnlyList<PatchWord> Words, string? Image, int? Version) Words(DiscPins pins, string? bundle, ReadOnlySpan<byte> dol)
    {
        ArgumentNullException.ThrowIfNull(pins);
        var listed = ExecutableWords(bundle, dol);
        var all = new SortedDictionary<(string, int), PatchWord>();
        foreach (var word in pins.Words.Concat(listed.Words))
        {
            if (all.TryGetValue((word.File, word.Offset), out var first) && (first.Original != word.Original || first.New != word.New))
            {
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                    $"The game program's patch list and the launcher's pins disagree about {word.File} at 0x{word.Offset:X}."));
            }
            all[(word.File, word.Offset)] = word;
        }
        return ([.. all.Values], listed.Image, listed.Version);
    }

    /// <summary>Writes a word over its stock word (or finds it written).</summary>
    private static void Write(Span<byte> file, PatchWord word)
    {
        if (word.Offset < 0 || word.Offset + 4 > file.Length)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The word at 0x{word.Offset:X} ({word.What}) is outside {word.File}."));
        }
        var found = BinaryPrimitives.ReadUInt32BigEndian(file[word.Offset..]);
        if (found != word.Original && found != word.New)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                $"{word.File} holds {found:X8} at 0x{word.Offset:X} ({word.What}), neither {word.Original:X8} nor {word.New:X8}: the game program's patch list does not fit these game files."));
        }
        BinaryPrimitives.WriteUInt32BigEndian(file[word.Offset..], word.New);
    }

    /// <summary>Where an address is in the executable's file: its section table has 18 sections (file offsets at 0,
    /// addresses at 0x48, sizes at 0x90).</summary>
    private static int FileOffset(ReadOnlySpan<byte> dol, uint address, string what)
    {
        for (var i = 0; dol.Length >= 0x100 && i < 18; i++)
        {
            var at = BinaryPrimitives.ReadUInt32BigEndian(dol[(i * 4)..]);
            var start = BinaryPrimitives.ReadUInt32BigEndian(dol[(0x48 + (i * 4))..]);
            var size = BinaryPrimitives.ReadUInt32BigEndian(dol[(0x90 + (i * 4))..]);
            if (size > 0 && address >= start && address - start + 4 <= size && at + (long)(address - start) + 4 <= dol.Length)
            {
                return (int)(at + (address - start));
            }
        }
        throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
            $"The game program's patch list names {address:X8} ({what}), which is in no section of the executable."));
    }
}
