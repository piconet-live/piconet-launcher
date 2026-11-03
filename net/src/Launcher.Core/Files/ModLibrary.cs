using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Launcher.Core;

/// <summary>One file a mod lays over the disc: its path under the disc's <c>files/</c>, its class, the guard's word
/// (<see cref="ModGuard"/>), whether the disc has no such file, its size and SHA-256. A code file is listed and not
/// kept: it is never used.</summary>
public sealed record ModFile(string Disc, ModFileClass Class, bool Online, string? Why, bool New, long Size, string Sha256);

/// <summary>
/// A mod's own manifest (<c>Mods/ID/manifest.json</c>), written by the import: its name, the file it came from and
/// that file's hash, the folders chosen, each file kept with its class and the guard's word, how many were left
/// out and why, and its picture. Mods ship none of this; this is the launcher's own.
/// </summary>
public sealed record ModManifest(string Id, string Name, string Source, string SourceSha256, DateTimeOffset Imported,
    IReadOnlyList<string> Chosen, IReadOnlyList<ModFile> Files, IReadOnlyDictionary<string, int> Dropped, string? Picture)
{
    public const int Format = 1;

    public const string FileName = "manifest.json", PictureFile = "picture.png";

    /// <summary>The courses the mod's files name (stage/stNN, selstNN, COLI_COURSENN), in order: for a picture
    /// where the mod has none of its own.</summary>
    public IReadOnlyList<int> Courses => [.. Files.Select(f => System.Text.RegularExpressions.Regex.Match(f.Disc,
            @"^stage/(?:st|selst|COLI_COURSE)(\d{2})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        .Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().Order()];
}

/// <summary>One mod in the player's list: its manifest, its folder, and whether it is on.</summary>
public sealed record InstalledMod(ModManifest Manifest, string Folder, bool On)
{
    /// <summary>Where the mod's kept files are, laid out by disc path.</summary>
    public string Files => Path.Combine(Folder, "files");

    /// <summary>The mod's own picture's file, or null.</summary>
    public string? Picture => Manifest.Picture is { } p && File.Exists(Path.Combine(Folder, p)) ? Path.Combine(Folder, p) : null;

    /// <summary>The kept car data file, when the mod has one.</summary>
    public string? CarData => Manifest.Files.FirstOrDefault(f => f.Class == ModFileClass.CarData) is { } f ? Path.Combine(Files, f.Disc) : null;
}

/// <summary>
/// The player's mods: the data folder's <c>Mods/</c>, one folder a mod (<see cref="ModImport"/>), and
/// <c>Mods/mods.json</c>, their order and which are on. A mod's folder whose manifest cannot be read is no mod (left
/// as it is); a mod the list does not name comes at the end, off.
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
        foreach (var (id, on) in ReadList())
        {
            if (found.Remove(id, out var manifest))
            {
                list.Add(new InstalledMod(manifest, Path.Combine(Folder, id), on));
            }
        }
        list.AddRange(found.Values.OrderBy(m => m.Imported).Select(m => new InstalledMod(m, Path.Combine(Folder, m.Id), false)));
        return list;
    }

    /// <summary>Writes the order and the switches.</summary>
    public void Save(IEnumerable<InstalledMod> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);
        Directory.CreateDirectory(Folder);
        var array = new JsonArray();
        foreach (var mod in mods)
        {
            array.Add(new JsonObject { ["id"] = mod.Manifest.Id, ["on"] = mod.On });
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

    private List<(string Id, bool On)> ReadList()
    {
        var list = new List<(string, bool)>();
        try
        {
            var path = Path.Combine(Folder, ListFile);
            if (!File.Exists(path) || new FileInfo(path).Length > 1 << 20)
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
                        list.Add((id.GetString()!, m.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.True));
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

    /// <summary>Writes a mod's manifest into its folder.</summary>
    public static void WriteManifest(string folder, ModManifest m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var files = new JsonArray();
        foreach (var f in m.Files)
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
            files.Add(o);
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
            ["chosen"] = new JsonArray([.. m.Chosen.Select(c => (JsonNode)JsonValue.Create(c))]),
            ["dropped"] = dropped,
            ["files"] = files,
        };
        if (m.Picture is { } picture)
        {
            root["picture"] = picture;
        }
        File.WriteAllText(Path.Combine(folder, ModManifest.FileName), root.ToJsonString(Indented));
    }

    /// <summary>A mod's manifest; null when it is not one this launcher wrote (or cannot be read). The files it names
    /// are held to paths inside the mod's folder.</summary>
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
            if (r.GetProperty("format").GetInt32() != ModManifest.Format)
            {
                return null;
            }
            var files = new List<ModFile>();
            foreach (var f in r.GetProperty("files").EnumerateArray())
            {
                if (ModArchive.Safe(f.GetProperty("disc").GetString()) is not { } disc ||
                    !Enum.TryParse<ModFileClass>(f.GetProperty("class").GetString(), true, out var kind))
                {
                    return null;
                }
                files.Add(new ModFile(disc, kind, f.GetProperty("online").GetBoolean(),
                    f.TryGetProperty("why", out var why) ? why.GetString() : null, f.TryGetProperty("new", out var n) && n.GetBoolean(),
                    f.GetProperty("size").GetInt64(), f.GetProperty("sha256").GetString() ?? ""));
            }
            var dropped = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var d in r.GetProperty("dropped").EnumerateObject())
            {
                dropped[d.Name] = d.Value.GetInt32();
            }
            var picture = r.TryGetProperty("picture", out var p) && p.GetString() == ModManifest.PictureFile ? ModManifest.PictureFile : null;
            return new ModManifest(r.GetProperty("id").GetString() ?? "", r.GetProperty("name").GetString() ?? "Mod",
                r.GetProperty("source").GetString() ?? "", r.GetProperty("sourceSha256").GetString() ?? "",
                DateTimeOffset.Parse(r.GetProperty("imported").GetString() ?? "", CultureInfo.InvariantCulture),
                [.. r.GetProperty("chosen").EnumerateArray().Select(c => c.GetString() ?? "")], files, dropped, picture);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException
                                      or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
