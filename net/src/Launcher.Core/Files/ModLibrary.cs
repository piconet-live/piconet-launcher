using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Launcher.Core;

/// <summary>One file a mod lays over the disc: its path under the disc's <c>files/</c>, its class, the guard's word
/// (<see cref="ModGuard"/>), whether the disc has no such file, its size and SHA-256. A code file is listed and not
/// kept: it is never used.</summary>
public sealed record ModFile(string Disc, ModFileClass Class, bool Online, string? Why, bool New, long Size, string Sha256);

/// <summary>One of the mod's own text files (a readme, the author's notes), kept as it came in the store: its path in
/// the mod, its size and SHA-256.</summary>
public sealed record ModText(string Path, long Size, string Sha256)
{
    /// <summary>The most of a text shown.</summary>
    public const int MostShown = 256 << 10;

    /// <summary>A text file's words: UTF-8 (its mark dropped) where it is that, else Latin-1 (older Windows notes);
    /// lines ended by '\n'; no control characters but tabs; at most <see cref="MostShown"/> bytes of it.</summary>
    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var span = bytes.AsSpan(0, Math.Min(bytes.Length, MostShown));
        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            span = span[3..];
        }
        string text;
        try
        {
            text = new System.Text.UTF8Encoding(false, true).GetString(span);
        }
        catch (System.Text.DecoderFallbackException)
        {
            text = System.Text.Encoding.Latin1.GetString(span);
        }
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return new string([.. text.Where(c => c is '\n' or '\t' || !char.IsControl(c))]).TrimEnd();
    }
}

/// <summary>One part of a mod: a folder of it laid out like the disc (<see cref="ModImport.Survey"/>'s roots), its
/// path in the mod, whether it is ticked, its files kept (every file of it, ticked or not: the player may tick it
/// later), and the paths of its files that are the disc's own (none kept: in a later part they put the disc's file
/// back over an earlier part's).</summary>
public sealed record ModPart(string Path, bool On, IReadOnlyList<ModFile> Files, IReadOnlyList<string> Same);

/// <summary>
/// A mod's own manifest (<c>Mods/ID/manifest.json</c>), written by the import: its name, the file it came from and
/// that file's hash, its parts with their files (each with its class and the guard's word) and which are ticked, how
/// many files were left out and why, and its picture. Mods ship none of this; this is the launcher's own. A file is
/// kept once in <c>store/</c> by its SHA-256, however many parts have it. A mod of the first form (imported before
/// the tree) holds only the files of the folders chosen then, under <c>files/</c> by disc path, all of them in its
/// last part: its parts cannot change (<see cref="Fixed"/>).
/// </summary>
public sealed record ModManifest(string Id, string Name, string Source, string SourceSha256, DateTimeOffset Imported,
    IReadOnlyList<ModPart> Parts, IReadOnlyDictionary<string, int> Dropped, string? Picture, int Form = ModManifest.Format)
{
    public const int Format = 2;

    public const string FileName = "manifest.json", PictureFile = "picture.png", StoreFolder = "store";

    /// <summary>The mod's own text files, in the store (none in a mod of the first form).</summary>
    public IReadOnlyList<ModText> Texts { get; init; } = [];

    /// <summary>A mod of the first form: its parts are as they were chosen at its import.</summary>
    public bool Fixed => Form < 2;

    /// <summary>The paths of the parts ticked.</summary>
    public IReadOnlySet<string> PartsOn => Parts.Where(p => p.On).Select(p => p.Path).ToHashSet(StringComparer.Ordinal);

    /// <summary>The manifest with these parts ticked (a fixed one as it is).</summary>
    public ModManifest WithOn(IReadOnlySet<string> on)
    {
        ArgumentNullException.ThrowIfNull(on);
        return Fixed ? this : this with { Parts = [.. Parts.Select(p => p with { On = on.Contains(p.Path) })] };
    }

    /// <summary>The files the mod lays over the disc: those of the ticked parts in Explorer's order, a later part's
    /// over an earlier one's (and a later part's file that is the disc's own takes an earlier one's away), by disc path.</summary>
    public IReadOnlyList<ModFile> Files
    {
        get
        {
            var files = new Dictionary<string, ModFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in Parts.Where(p => p.On).OrderBy(p => p.Path, ExplorerOrder.Paths))
            {
                foreach (var file in part.Files)
                {
                    files[file.Disc] = file;
                }
                foreach (var same in part.Same)
                {
                    files.Remove(same);
                }
            }
            return [.. files.Values.OrderBy(f => f.Disc, StringComparer.Ordinal)];
        }
    }

    /// <summary>The courses the mod's files name (stage/stNN, selstNN, COLI_COURSENN), in order: for a picture
    /// where the mod has none of its own.</summary>
    public IReadOnlyList<int> Courses => [.. Parts.SelectMany(p => p.Files).Select(f => System.Text.RegularExpressions.Regex.Match(f.Disc,
            @"^stage/(?:st|selst|COLI_COURSE)(\d{2})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        .Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().Order()];
}

/// <summary>One mod in the player's list: its manifest, its folder, and whether it is on.</summary>
public sealed record InstalledMod(ModManifest Manifest, string Folder, bool On)
{
    /// <summary>Where a mod of the first form keeps its files, laid out by disc path.</summary>
    public string Files => Path.Combine(Folder, "files");

    /// <summary>Where a mod keeps its files by their SHA-256.</summary>
    public string Store => Path.Combine(Folder, ModManifest.StoreFolder);

    /// <summary>The file that holds a kept file's bytes.</summary>
    public string PathOf(ModFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Manifest.Fixed ? Path.Combine(Files, file.Disc) : Path.Combine(Store, file.Sha256);
    }

    /// <summary>The mod's own picture's file, or null.</summary>
    public string? Picture => Manifest.Picture is { } p && File.Exists(Path.Combine(Folder, p)) ? Path.Combine(Folder, p) : null;

    /// <summary>One of the mod's text files, read; null: it has no such file or it cannot be read.</summary>
    public string? ReadText(string path)
    {
        if (Manifest.Texts.FirstOrDefault(t => t.Path == path) is not { } text)
        {
            return null;
        }
        try
        {
            var file = Path.Combine(Store, text.Sha256);
            return new FileInfo(file).Length > ModImport.MostText ? null : ModText.Decode(File.ReadAllBytes(file));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The kept car data file of the ticked parts, when there is one.</summary>
    public string? CarData => Manifest.Files.FirstOrDefault(f => f.Class == ModFileClass.CarData) is { } f ? PathOf(f) : null;
}

/// <summary>
/// The player's mods: the data folder's <c>Mods/</c>, one folder a mod (<see cref="ModImport"/>), and
/// <c>Mods/mods.json</c>, their order, which are on, and which of each mod's parts are ticked (none said: as at its
/// import). A mod's folder whose manifest cannot be read is no mod (left as it is); a mod the list does not name
/// comes at the end, off.
/// </summary>
public sealed class ModLibrary(string dataFolder)
{
    public const string FolderName = "Mods", ListFile = "mods.json";

    public string Folder => Path.Combine(dataFolder, FolderName);

    /// <summary>The mods in the player's order.</summary>
    public IReadOnlyList<InstalledMod> Read()
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }
        var found = new Dictionary<string, ModManifest>(StringComparer.Ordinal);
        foreach (var dir in Directory.EnumerateDirectories(Folder))
        {
            if (!dir.EndsWith(".part", StringComparison.Ordinal) && ReadManifest(dir) is { } manifest && manifest.Id == Path.GetFileName(dir))
            {
                found[manifest.Id] = manifest;
            }
        }
        var list = new List<InstalledMod>();
        foreach (var (id, on, parts) in ReadList())
        {
            if (found.Remove(id, out var manifest))
            {
                list.Add(new InstalledMod(parts is null ? manifest : manifest.WithOn(parts), Path.Combine(Folder, id), on));
            }
        }
        list.AddRange(found.Values.OrderBy(m => m.Imported).Select(m => new InstalledMod(m, Path.Combine(Folder, m.Id), false)));
        return list;
    }

    /// <summary>Writes the order, the switches and the parts ticked.</summary>
    public void Save(IEnumerable<InstalledMod> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);
        Directory.CreateDirectory(Folder);
        var array = new JsonArray();
        foreach (var mod in mods)
        {
            var entry = new JsonObject { ["id"] = mod.Manifest.Id, ["on"] = mod.On };
            if (!mod.Manifest.Fixed)
            {
                entry["parts"] = new JsonArray([.. mod.Manifest.Parts.Where(p => p.On).Select(p => (JsonNode)JsonValue.Create(p.Path))]);
            }
            array.Add(entry);
        }
        var text = new JsonObject { ["format"] = 1, ["mods"] = array }.ToJsonString(Indented);
        var temp = Path.Combine(Folder, ListFile + ".new");
        File.WriteAllText(temp, text);
        File.Move(temp, Path.Combine(Folder, ListFile), true);
    }

    /// <summary>A mod's folder removed, and the mod with it.</summary>
    public void Remove(InstalledMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (Directory.Exists(mod.Folder) && Path.GetDirectoryName(mod.Folder) == Folder)
        {
            Directory.Delete(mod.Folder, true);
        }
    }

    private List<(string Id, bool On, HashSet<string>? Parts)> ReadList()
    {
        var list = new List<(string, bool, HashSet<string>?)>();
        try
        {
            var path = Path.Combine(Folder, ListFile);
            if (!File.Exists(path) || new FileInfo(path).Length > 16 << 20)
            {
                return list;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("mods", out var mods) && mods.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in mods.EnumerateArray())
                {
                    if (m.ValueKind == JsonValueKind.Object && m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String &&
                        !list.Exists(l => l.Item1 == id.GetString()))
                    {
                        HashSet<string>? parts = null;
                        if (m.TryGetProperty("parts", out var p) && p.ValueKind == JsonValueKind.Array)
                        {
                            parts = [.. p.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)];
                        }
                        list.Add((id.GetString()!, m.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.True, parts));
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // a list that cannot be read: every mod off, in the order of import
        }
        return list;
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static JsonObject FileJson(ModFile f)
    {
        var o = new JsonObject
        {
            ["disc"] = f.Disc,
            ["class"] = f.Class.ToString().ToLowerInvariant(),
            ["online"] = f.Online,
            ["size"] = f.Size,
            ["sha256"] = f.Sha256,
        };
        if (f.Why is { } why)
        {
            o["why"] = why;
        }
        if (f.New)
        {
            o["new"] = true;
        }
        return o;
    }

    /// <summary>Writes a mod's manifest into its folder (the form of today: a mod of the first form is never
    /// written again).</summary>
    public static void WriteManifest(string folder, ModManifest m)
    {
        ArgumentNullException.ThrowIfNull(m);
        if (m.Fixed)
        {
            throw new ArgumentException("a manifest of the first form is not written", nameof(m));
        }
        var parts = new JsonArray();
        foreach (var part in m.Parts)
        {
            parts.Add(new JsonObject
            {
                ["path"] = part.Path,
                ["on"] = part.On,
                ["files"] = new JsonArray([.. part.Files.Select(f => (JsonNode)FileJson(f))]),
                ["same"] = new JsonArray([.. part.Same.Select(d => (JsonNode)JsonValue.Create(d))]),
            });
        }
        var dropped = new JsonObject();
        foreach (var (why, count) in m.Dropped)
        {
            dropped[why] = count;
        }
        var root = new JsonObject
        {
            ["format"] = ModManifest.Format,
            ["id"] = m.Id,
            ["name"] = m.Name,
            ["source"] = m.Source,
            ["sourceSha256"] = m.SourceSha256,
            ["imported"] = m.Imported.ToString("o", CultureInfo.InvariantCulture),
            ["dropped"] = dropped,
            ["parts"] = parts,
            ["texts"] = new JsonArray([.. m.Texts.Select(t => (JsonNode)new JsonObject { ["path"] = t.Path, ["size"] = t.Size, ["sha256"] = t.Sha256 })]),
        };
        if (m.Picture is { } picture)
        {
            root["picture"] = picture;
        }
        File.WriteAllText(Path.Combine(folder, ModManifest.FileName), root.ToJsonString());
    }

    /// <summary>A mod's manifest, of either form; null when it is not one this launcher wrote (or cannot be read). The
    /// files it names are held to paths inside the mod's folder: a disc path that stays inside it, a SHA-256 that is
    /// one.</summary>
    public static ModManifest? ReadManifest(string folder)
    {
        try
        {
            var path = Path.Combine(folder, ModManifest.FileName);
            if (!File.Exists(path) || new FileInfo(path).Length > 64 << 20)
            {
                return null;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            var form = r.GetProperty("format").GetInt32();
            if (form is not (1 or ModManifest.Format))
            {
                return null;
            }
            List<ModPart> parts = [];
            if (form == 1)
            {
                // the folders chosen at the import, all of them ticked, the files (one each by disc path) in the last
                var files = Files(r.GetProperty("files"), form);
                if (files is null)
                {
                    return null;
                }
                var chosen = r.GetProperty("chosen").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
                parts = [.. chosen.Select((c, i) => new ModPart(c, true, i == chosen.Count - 1 ? files : [], []))];
            }
            else
            {
                foreach (var part in r.GetProperty("parts").EnumerateArray())
                {
                    var files = Files(part.GetProperty("files"), form);
                    var same = part.GetProperty("same").EnumerateArray().Select(d => ModArchive.Safe(d.GetString())).ToList();
                    if (files is null || same.Contains(null))
                    {
                        return null;
                    }
                    parts.Add(new ModPart(part.GetProperty("path").GetString() ?? "", part.GetProperty("on").GetBoolean(), files, same!));
                }
            }
            var dropped = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var d in r.GetProperty("dropped").EnumerateObject())
            {
                dropped[d.Name] = d.Value.GetInt32();
            }
            var picture = r.TryGetProperty("picture", out var p) && p.GetString() == ModManifest.PictureFile ? ModManifest.PictureFile : null;
            if (Texts(r, form) is not { } texts)
            {
                return null;
            }
            return new ModManifest(r.GetProperty("id").GetString() ?? "", r.GetProperty("name").GetString() ?? "Mod",
                r.GetProperty("source").GetString() ?? "", r.GetProperty("sourceSha256").GetString() ?? "",
                DateTimeOffset.Parse(r.GetProperty("imported").GetString() ?? "", CultureInfo.InvariantCulture),
                form == 1 ? parts : [.. parts.OrderBy(x => x.Path, ExplorerOrder.Paths)], dropped, picture, form) { Texts = texts };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException
                                      or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The mod's text files (none in the first form); null when one names a place outside the mod's folder.</summary>
    private static List<ModText>? Texts(JsonElement root, int form)
    {
        var texts = new List<ModText>();
        if (form < 2 || !root.TryGetProperty("texts", out var list))
        {
            return texts;
        }
        foreach (var text in list.EnumerateArray())
        {
            var sha = text.GetProperty("sha256").GetString() ?? "";
            if (ModArchive.Safe(text.GetProperty("path").GetString()) is not { } at || !IsSha256(sha))
            {
                return null;
            }
            texts.Add(new ModText(at, text.GetProperty("size").GetInt64(), sha));
        }
        return texts;
    }

    /// <summary>A list of files; null when one names a place outside the mod's folder: a disc path that leaves it, or
    /// (in the store of today's form) a SHA-256 that is not 64 small hex digits.</summary>
    private static List<ModFile>? Files(JsonElement list, int form)
    {
        var files = new List<ModFile>();
        foreach (var f in list.EnumerateArray())
        {
            var sha = f.GetProperty("sha256").GetString() ?? "";
            if (ModArchive.Safe(f.GetProperty("disc").GetString()) is not { } disc ||
                !Enum.TryParse<ModFileClass>(f.GetProperty("class").GetString(), true, out var kind) ||
                (form >= 2 && !IsSha256(sha)))
            {
                return null;
            }
            files.Add(new ModFile(disc, kind, f.GetProperty("online").GetBoolean(),
                f.TryGetProperty("why", out var why) ? why.GetString() : null, f.TryGetProperty("new", out var n) && n.GetBoolean(),
                f.GetProperty("size").GetInt64(), sha));
        }
        return files;
    }

    /// <summary>A SHA-256 as the import writes it: 64 hex digits in small letters.</summary>
    public static bool IsSha256(string s) => s is { Length: 64 } && s.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
}
