using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fzgx.GameCube.Disc;
using Launcher.Core;
using Tomlyn;
using Tomlyn.Model;

namespace DiscPinsWriter;

/// <summary>
/// Writes <c>net/src/Launcher.Core/DiscPins.json</c>, the resource compiled into Launcher.Core that says which disc
/// the launcher prepares the game's files from (<see cref="DiscPrep"/>, <see cref="Launcher.Core.DiscPins"/>): the
/// game's id, the SHA-256 of each file the native build was made from, one digest over every file of the game, and the
/// pins (the words that make main.rel load at the address the native module is built for). Nothing in it is typed by
/// hand:
/// <list type="bullet">
/// <item>the hashes are read from your disc image (<c>--disc</c>), through the launcher's own reader
/// (<see cref="DiscImage"/>);</item>
/// <item>what is pinned is the files by name and content, not where an image has them (the same files are the
/// same game): <c>header_layout</c> holds the header's eight words that say where things lie (0x420 to 0x43F), read
/// from the disc, which the launcher writes over whatever an image has there; <c>sys/fst.bin</c>'s size and hash are
/// of the table as the launcher writes it from the names and sizes alone (<see cref="DiscImage.CanonicalTable"/>);</item>
/// <item>which files are named comes from the kit's manifest (<c>recomp/manifest.toml</c>: every [[module]]'s
/// disc_path) after the disc's five system files;</item>
/// <item>the pins come from the manifest's [[pin]] entries (which file, which offsets, the SHA-1 of the stock file),
/// the stock word from the disc and the new word from the hand-prepared game folder (<c>--tree</c>, the extracted game
/// every check of the native module ran on). The tool fails unless that folder's file differs from the disc's in
/// exactly the words the manifest names.</item>
/// </list>
/// The words of the executable that depend on the mod image (the bundle's patches.json) are not in the file: they
/// change with every image, and the launcher reads them from the bundle when it prepares.
/// <para><c>--check</c> compares the committed resource with what the disc gives and writes nothing.
/// <c>--compare PREPARED REFERENCE</c> compares two game folders file by file and says every difference, with the
/// differing words of a file of the same size: the check of a prepared folder against the hand-prepared one. Exit
/// status 0 when they are the same.</para>
/// <para>Run from net/: <c>dotnet run --project tools/DiscPinsWriter -- [--disc IMAGE] [--tree FOLDER] [--check]</c>. A
/// console project and not a build step: it reads the developer's own disc and hand-prepared folder, which no build
/// has, and it is run when the pinned disc or the manifest's pins change, not at every build.</para>
/// </summary>
internal static class Program
{
    private const string Usage =
        "DiscPinsWriter [--disc IMAGE] [--tree FOLDER] [--manifest FILE] [--check] | --compare PREPARED REFERENCE";

    /// <summary>What the file says made it.</summary>
    private const string MadeBy =
        "net/tools/DiscPinsWriter from the disc image, recomp/manifest.toml and the hand-prepared game folder; not edited by hand";

    private static readonly string Repository = Metadata("RepositoryFolder");

    private static readonly string Output = Path.Combine(Repository, "net", "src", "Launcher.Core", "DiscPins.json");

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception e) when (e is ToolException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine("DiscPinsWriter: " + e.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        string? disc = DiscDefault(), tree = Path.Combine(WorkFolder(), "extracted", "GFZE01");
        var manifest = Path.Combine(Repository, "recomp", "manifest.toml");
        var check = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ToolException($"{args[i]} needs a value. {Usage}");
            switch (args[i])
            {
                case "--disc":
                    disc = Next();
                    break;
                case "--tree":
                    tree = Next();
                    break;
                case "--manifest":
                    manifest = Next();
                    break;
                case "--check":
                    check = true;
                    break;
                case "--compare" when i + 2 < args.Length:
                    var (lines, same) = Compare(args[i + 1], args[i + 2]);
                    lines.ForEach(Console.WriteLine);
                    Console.WriteLine($"{same} files are the same, {lines.Count} difference(s)");
                    return lines.Count == 0 ? 0 : 1;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        if (string.IsNullOrEmpty(disc))
        {
            throw new ToolException("no disc image: --disc IMAGE, or set DISC_DEFAULT to your own F-Zero GX (USA) ISO (a row " +
                                    "\"DISC_DEFAULT<tab>/mnt/c/.../F-Zero GX (USA).iso\" in ~/.config/piconet/pins.tsv)");
        }
        var text = Pins(disc, tree, manifest);
        if (check)
        {
            var same = File.Exists(Output) && File.ReadAllText(Output) == text;
            Console.WriteLine(same ? "DiscPins.json is what the disc gives" : "DiscPins.json differs from what the disc gives: run DiscPinsWriter");
            return same ? 0 : 1;
        }
        File.WriteAllText(Output, text);
        using var written = JsonDocument.Parse(text);
        Console.WriteLine($"{Output}: {written.RootElement.GetProperty("named").EnumerateObject().Count()} named files, " +
                          $"{written.RootElement.GetProperty("words").GetArrayLength()} words");
        return 0;
    }

    private static string Metadata(string key) =>
        typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value ?? "";

    /// <summary>Your disc image: the row DISC_DEFAULT of the runner's port/setup/pins.tsv (empty there; RunnerPins) or,
    /// over it, of ~/.config/piconet/pins.tsv, as port/setup/lib.sh reads it.</summary>
    private static string? DiscDefault()
    {
        string? disc = null;
        foreach (var table in new[] { Metadata("RunnerPins"), Home(".config", "piconet", "pins.tsv") })
        {
            if (table.Length > 0 && File.Exists(table))
            {
                disc = File.ReadLines(table).Select(line => line.Split('\t'))
                    .FirstOrDefault(cells => cells[0] == "DISC_DEFAULT" && cells.Length >= 2 && cells[1].Length > 0)?[1] ?? disc;
            }
        }
        return disc;
    }

    /// <summary>The port's work folder, as port/setup/work_folder.py gives it: the PC's own (PORT_WORK_DEFAULT in
    /// ~/.config/piconet/work-folder.env), else build/work in this checkout.</summary>
    private static string WorkFolder()
    {
        var local = Home(".config", "piconet", "work-folder.env");
        var own = File.Exists(local)
            ? File.ReadLines(local).Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("PORT_WORK_DEFAULT=", StringComparison.Ordinal))
            : null;
        return own is null ? Path.Combine(Repository, "build", "work") : own["PORT_WORK_DEFAULT=".Length..];
    }

    private static string Home(params string[] parts) =>
        Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), .. parts]);

    /// <summary>The resource's text from the disc, the manifest and the hand-prepared folder.</summary>
    private static string Pins(string discPath, string tree, string manifestPath)
    {
        using var disc = Open(discPath);
        var (files, folders) = disc.Table();
        var byName = files.ToDictionary(f => "files/" + f.Path, StringComparer.Ordinal);
        foreach (var system in disc.SystemFiles)
        {
            byName[system.Path] = system;
        }
        byte[] Read(string path) =>
            byName.TryGetValue(path, out var file) ? ReadFile(disc, file) : throw new ToolException($"the disc has no {path}");

        var manifest = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(manifestPath))
            ?? throw new ToolException($"{manifestPath} is no TOML table");
        var game = (string)((TomlTable)manifest["game"])["id"];
        if (disc.GameId != game)
        {
            throw new ToolException($"the disc is {disc.GameId}, the manifest's game is {game}");
        }
        var modules = ((TomlTableArray)manifest["module"]).ToList();
        var named = Named(disc, files, modules);
        var words = ((TomlTableArray)manifest["pin"]).SelectMany(pin => Words(pin, modules, Read, tree)).ToList();

        var hashed = files.Select(f => (f.Path, Size: (long)f.Length, Sha256: Convert.ToHexStringLower(SHA256.HashData(ReadFile(disc, f))))).ToList();
        var boot = Read("sys/boot.bin");
        var layout = Enumerable.Range(0, DiscImage.LayoutWords)
            .Select(i => BinaryPrimitives.ReadUInt32BigEndian(boot.AsSpan(DiscImage.LayoutAt + (4 * i)))).ToList();
        var table = DiscImage.CanonicalTable(files.Select(f => (f.Path, f.Length)), folders, layout[DiscImage.LayoutFilesAt]);
        var resource = new Resource(disc.GameId, disc.Disc, disc.Revision, files.Count, folders.Count, hashed.Sum(f => f.Size),
            DiscPrep.ContentDigest(hashed), [.. named.Select(path => (path, path == "sys/fst.bin" ? table : Read(path)))], layout, words);
        return Text(resource);
    }

    /// <summary>The files checked by name: the disc's five system files, then every module's files, each once.</summary>
    private static List<string> Named(DiscImage disc, IReadOnlyList<DiscFile> files, List<TomlTable> modules)
    {
        var named = disc.SystemFiles.Select(f => f.Path).ToList();
        foreach (var module in modules)
        {
            var pattern = (string)module["disc_path"];
            var glob = Glob(pattern);
            var found = files.Select(f => "files/" + f.Path).Where(p => glob.IsMatch(p)).Order(StringComparer.Ordinal).ToList();
            if (found.Count == 0 && disc.SystemFiles.Any(f => f.Path == pattern))
            {
                found.Add(pattern);
            }
            if (found.Count == 0)
            {
                throw new ToolException($"the disc has no {pattern}");
            }
            named.AddRange(found.Where(p => !named.Contains(p)));
        }
        return named;
    }

    /// <summary>A pin's words: the stock word from the disc, the new one from the hand-prepared folder, whose file
    /// must differ from the disc's in exactly the words the pin names.</summary>
    private static List<PinWord> Words(TomlTable pin, List<TomlTable> modules, Func<string, byte[]> read, string tree)
    {
        var name = (string)pin["name"];
        var path = (string)modules.First(m => (string)m["name"] == (string)pin["module"])["disc_path"];
        var stock = read(path);
        // the manifest names the stock file by its SHA-1 (an identity, as git's: no secret rests on it)
#pragma warning disable CA5350
        var sha1 = Convert.ToHexStringLower(SHA1.HashData(stock));
#pragma warning restore CA5350
        if (sha1 != (string)pin["relies_on_sha1"])
        {
            throw new ToolException($"{path} on the disc is not the file the pin {name} relies on");
        }
        var pinned = File.ReadAllBytes(Path.Combine(tree, path));
        var writes = ((TomlArray)pin["writes"]).Cast<TomlTable>().ToList();
        var offsets = writes.Select(w => (long)w["file_offset"]).Order().ToList();
        var differing = Enumerable.Range(0, (stock.Length + 3) / 4).Select(i => i * 4)
            .Where(i => !stock.AsSpan(i, Math.Min(4, stock.Length - i))
                .SequenceEqual(pinned.AsSpan(Math.Min(i, pinned.Length), Math.Clamp(pinned.Length - i, 0, 4))))
            .Select(i => (long)i).ToList();
        if (pinned.Length != stock.Length || !differing.SequenceEqual(offsets))
        {
            throw new ToolException($"{path} in {tree} differs from the disc at [{string.Join(", ", differing.Select(Hex))}], " +
                                    $"the manifest's pin {name} names [{string.Join(", ", offsets.Select(Hex))}]");
        }
        return writes.Select(w =>
        {
            var at = (int)(long)w["file_offset"];
            return new PinWord(path, at, BinaryPrimitives.ReadUInt32BigEndian(stock.AsSpan(at)),
                BinaryPrimitives.ReadUInt32BigEndian(pinned.AsSpan(at)), name, w.TryGetValue("address", out var a) ? (long)a : null);
        }).ToList();
    }

    /// <summary>The resource as text, in the form Python's json.dumps(indent=1) writes it: one space
    /// a level, ASCII only, a line end after it.</summary>
    private static string Text(Resource r)
    {
        static string Word(long word) => word.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Indented = true, IndentSize = 1, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
               }))
        {
            json.WriteStartObject();
            json.WriteNumber("version", 2);
            json.WriteString("made_by", MadeBy);
            json.WriteString("game", r.Game);
            json.WriteNumber("disc", r.Disc);
            json.WriteNumber("revision", r.Revision);
            json.WriteString("name", "F-Zero GX (USA)");
            json.WriteNumber("files", r.Files);
            json.WriteNumber("folders", r.Folders);
            json.WriteNumber("bytes", r.Bytes);
            json.WriteString("content_sha256", r.ContentSha256);
            json.WriteStartObject("named");
            foreach (var (path, bytes) in r.Named)
            {
                json.WriteStartObject(path);
                json.WriteNumber("size", bytes.Length);
                json.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(bytes)));
                json.WriteEndObject();
            }
            json.WriteEndObject();
            json.WriteStartArray("header_layout");
            r.Layout.ForEach(word => json.WriteStringValue(Word(word)));
            json.WriteEndArray();
            json.WriteStartArray("words");
            foreach (var w in r.Words)
            {
                json.WriteStartObject();
                json.WriteString("file", w.File);
                json.WriteNumber("offset", w.Offset);
                json.WriteString("original", Word(w.Original));
                json.WriteString("new", Word(w.New));
                json.WriteString("what", w.What);
                if (w.Address is { } address)
                {
                    json.WriteString("address", Word(address));
                }
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static DiscImage Open(string path)
    {
        try
        {
            return DiscImage.Open(path);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new ToolException($"{path}: {e.Message}");
        }
    }

    private static byte[] ReadFile(DiscImage disc, DiscFile file)
    {
        var bytes = new byte[file.Length];
        disc.Read(file.Offset, bytes, file.Path);
        return bytes;
    }

    /// <summary>A manifest's disc_path as a pattern: '*' any characters, '?' one (fnmatch's, as the kit's other
    /// readers take it); no other mark.</summary>
    private static Regex Glob(string pattern) =>
        pattern.IndexOfAny(['[', ']']) >= 0
            ? throw new ToolException($"disc_path {pattern}: only '*' and '?' are taken")
            : new Regex("^" + Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$",
                RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static string Hex(long n) => "0x" + n.ToString("x", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Every difference between two game folders, as lines; the count of files that are the same.</summary>
    private static (List<string> Lines, int Same) Compare(string prepared, string reference)
    {
        var ours = Tree(prepared);
        var theirs = Tree(reference);
        var (lines, same) = (new List<string>(), 0);
        foreach (var name in ours.Keys.Union(theirs.Keys).Order(StringComparer.Ordinal))
        {
            if (!theirs.TryGetValue(name, out var other))
            {
                lines.Add($"only in the prepared folder: {name}");
            }
            else if (!ours.TryGetValue(name, out var mine))
            {
                lines.Add($"only in the reference: {name}");
            }
            else if (mine is not null)
            {
                var (a, b) = (File.ReadAllBytes(mine), File.ReadAllBytes(other!));
                if (a.AsSpan().SequenceEqual(b))
                {
                    same++;
                }
                else if (a.Length != b.Length)
                {
                    lines.Add($"differs: {name}: {a.Length} bytes, the reference {b.Length}");
                }
                else
                {
                    var words = Enumerable.Range(0, (a.Length + 3) / 4).Select(i => i * 4)
                        .Where(i => !a.AsSpan(i, Math.Min(4, a.Length - i)).SequenceEqual(b.AsSpan(i, Math.Min(4, b.Length - i))))
                        .Select(i => $"0x{i:X}: {Convert.ToHexString(a, i, Math.Min(4, a.Length - i))}, the reference " +
                                     Convert.ToHexString(b, i, Math.Min(4, b.Length - i))).ToList();
                    lines.Add($"differs: {name}: {words.Count} word(s): " + string.Join("; ", words.Take(8)) + (words.Count > 8 ? " ..." : "") +
                              $" (sha256 {Convert.ToHexStringLower(SHA256.HashData(a))[..12]}, the reference {Convert.ToHexStringLower(SHA256.HashData(b))[..12]})");
                }
            }
        }
        return (lines, same);
    }

    /// <summary>A folder's files by their path under it ('/'), and its empty folders (with a '/' after, no file).</summary>
    private static Dictionary<string, string?> Tree(string root)
    {
        var found = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var folder in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Prepend(root))
        {
            var relative = Path.GetRelativePath(root, folder).Replace('\\', '/');
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
            {
                found[relative + "/"] = null;
            }
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                found[(relative == "." ? "" : relative + "/") + Path.GetFileName(file)] = file;
            }
        }
        return found;
    }
}

/// <summary>A pinned word: its file, offset, stock and new word, the pin's name, and its address when known.</summary>
internal sealed record PinWord(string File, long Offset, uint Original, uint New, string What, long? Address);

/// <summary>What the resource holds (its members in Program.Text's order).</summary>
internal sealed record Resource(string Game, int Disc, int Revision, int Files, int Folders, long Bytes, string ContentSha256,
    List<(string Path, byte[] Bytes)> Named, List<uint> Layout, List<PinWord> Words);

/// <summary>Why the tool stops: said in a line, exit status 1.</summary>
internal sealed class ToolException(string message) : Exception(message);
