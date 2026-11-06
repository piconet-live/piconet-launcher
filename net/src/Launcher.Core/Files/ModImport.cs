using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Launcher.Core;

/// <summary>A folder of a mod laid out like the disc's <c>files/</c>: one part of the mod, to tick or not.
/// <paramref name="Path"/> is its place in the mod ("" its top).</summary>
public sealed record ModRoot(string Path, int Files, bool OnByDefault);

/// <summary>What a first look at a mod found: its name, its folders laid out like the disc, and its own picture.
/// <paramref name="Texts"/>: the mod's own text files (a readme, the author's notes; no game file), by path in the mod.</summary>
public sealed record ModSurvey(string Source, string Name, IReadOnlyList<ModRoot> Roots, string? Picture, IReadOnlyList<ModEntry> Entries,
    IReadOnlyList<string> Texts)
{
    /// <summary>The player has a choice to make: more than one folder.</summary>
    public bool AsksChoice => Roots.Count > 1;

    /// <summary>The mod's parts as a tree to tick.</summary>
    public ModTree Tree => ModTree.Of(Name, Roots.Select(r => r.Path), Texts);

    /// <summary>The parts ticked until the player says otherwise (<see cref="ModImport.Defaults"/>).</summary>
    public HashSet<string> OnByDefault => Roots.Where(r => r.OnByDefault).Select(r => r.Path).ToHashSet(StringComparer.Ordinal);
}

/// <summary>How far an import is: files done of all to read.</summary>
public readonly record struct ModProgress(int Done, int Total);

/// <summary>
/// Makes a mod the launcher can lay over the game: every part of the mod (its folders laid out like the disc), each
/// part's files kept under the data folder's <c>Mods/ID/store</c> once by their SHA-256 however many parts have them,
/// with what is no game file left out (save files, cheat codes, emulator settings, the disc's system files, previews,
/// loose pictures, the Japanese disc's <c>bmp</c>) and every file that is the player's own disc's. Each file kept is
/// classed and checked by <see cref="ModGuard"/>, and the mod's own manifest (<see cref="ModManifest"/>) says so and
/// which parts are ticked: the player can tick others later without importing again. Nothing is rebuilt and nothing
/// in the mod is run.
/// </summary>
public static class ModImport
{
    private static readonly string[] ExtraKinds =
    [
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".psd", ".txt", ".md", ".rtf", ".pdf", ".htm", ".html", ".url", ".ini", ".gci",
        ".raw", ".sav", ".gct", ".exe", ".bat", ".cmd", ".ps1", ".dll", ".py", ".hdr", ".ips", ".xdelta", ".ppf", ".zip", ".7z", ".rar",
    ];

    /// <summary>The top folders and files of the disc's <c>files/</c>; the Japanese disc's <c>bmp</c> too, for finding a
    /// mod's folders only (its files are left out).</summary>
    public static HashSet<string> DiscTop(IReadOnlyDictionary<string, string> disc)
    {
        ArgumentNullException.ThrowIfNull(disc);
        var top = new HashSet<string>(disc.Keys.Select(k => k.Split('/')[0]), StringComparer.OrdinalIgnoreCase) { "bmp" };
        return top;
    }

    /// <summary>The disc's files by path under <c>files/</c> (any case) to their spelling there.</summary>
    public static IReadOnlyDictionary<string, string> DiscFiles(string gameFiles)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(gameFiles))
        {
            foreach (var file in Directory.EnumerateFiles(gameFiles, "*", SearchOption.AllDirectories))
            {
                var path = Path.GetRelativePath(gameFiles, file).Replace('\\', '/');
                files[path] = path;
            }
        }
        return files;
    }

    /// <summary>The first look: which folders of the mod are laid out like the disc (a folder holding one of the
    /// disc's top folders or files), and its preview picture. Null and why when it is no mod.</summary>
    public static (ModSurvey? Survey, string? Refusal) Survey(string source, IReadOnlyDictionary<string, string> disc)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (entries, refusal) = ModArchive.List(source);
        return entries is null ? (null, refusal) : Survey(source, entries, disc);
    }

    /// <summary>The first look at a mod from its list of files (<see cref="ModArchive.List"/>).</summary>
    public static (ModSurvey? Survey, string? Refusal) Survey(string source, IReadOnlyList<ModEntry> entries, IReadOnlyDictionary<string, string> disc)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entries);
        var top = DiscTop(disc);
        var roots = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (PartOf(entry.Path, top, disc) is { } at)
            {
                roots[at.Root] = roots.GetValueOrDefault(at.Root) + 1;
            }
        }
        if (roots.Count == 0)
        {
            return (null, "No game files");
        }
        var list = roots.Select(r => new ModRoot(r.Key, r.Value, false)).ToList();
        var defaults = Defaults(list);
        list = [.. list.Select(r => r with { OnByDefault = defaults.Contains(r.Path) })];
        // the mod's name: its one top folder's where it has one (an archive's own name is often a download's), else
        // the file's or folder's
        var tops = entries.Select(e => e.Path.Split('/')).Where(p => p.Length > 1).Select(p => p[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var name = tops.Count == 1 && entries.All(e => e.Path.Contains('/', StringComparison.Ordinal)) && !top.Contains(tops[0])
            ? tops[0]
            : Directory.Exists(source) ? Path.GetFileName(Path.TrimEndingDirectorySeparator(source)) : Path.GetFileNameWithoutExtension(source);
        // the author's text files: a .txt or .md that is no file of the disc's in a part, small, at most so many
        var texts = entries.Where(e => IsText(e.Path) && e.Size <= MostText && !(PartOf(e.Path, top, disc) is { } at && disc.ContainsKey(at.Inside)))
            .Select(e => e.Path).Take(MostTexts).ToList();
        return (new ModSurvey(source, NameOf(name), list, Picture(entries, roots.Keys), entries, texts), null);
    }

    /// <summary>The part an entry of the mod is in and its path from there: the folder above its first disc folder
    /// (a folder of the disc's <c>files/</c> with something below it) or disc file (the entry itself); null: none.</summary>
    internal static (string Root, string Inside)? PartOf(string entry, HashSet<string> top, IReadOnlyDictionary<string, string> disc)
    {
        var parts = entry.Split('/');
        for (var i = 0; i < parts.Length; i++)
        {
            if (top.Contains(parts[i]) && (i < parts.Length - 1 || disc.ContainsKey(parts[i])))
            {
                return (string.Join('/', parts.Take(i)), string.Join('/', parts.Skip(i)));
            }
        }
        return null;
    }

    /// <summary>The largest text file of a mod's that is kept, and how many at most.</summary>
    public const int MostText = 1 << 20, MostTexts = 500;

    private static bool IsText(string path) =>
        path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    /// <summary>A text file of the mod read from the mod itself (in the chooser, before the import); null: it cannot be.</summary>
    public static string? ReadText(ModSurvey survey, string path)
    {
        ArgumentNullException.ThrowIfNull(survey);
        if (!survey.Texts.Contains(path, StringComparer.Ordinal))
        {
            return null;
        }
        byte[]? found = null;
        try
        {
            ModArchive.Each(survey.Source, p => p == path && found is null, (_, bytes) => found = bytes, CancellationToken.None);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
                                      or NotSupportedException or InvalidDataException or SharpCompress.Common.ArchiveException)
        {
            return null;
        }
        return found is null ? null : ModText.Decode(found);
    }

    /// <summary>A name to show: underscores as spaces, a trailing "complete" or "all versions" dropped, and words
    /// written all in small letters given a capital.</summary>
    private static string NameOf(string file)
    {
        var name = Regex.Replace(file.Replace('_', ' '), @"\s+(complete|all versions)\s*$", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        if (!name.Any(char.IsUpper))
        {
            name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        }
        return name.Length is > 0 and <= 60 ? name : name.Length > 60 ? name[..60] : "Mod";
    }

    private static readonly Regex Variant = new(@"^(?:step\s*)?(\d+)([a-z])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The folders on until the player says otherwise: every one but those marked optional, those of
    /// another region (Japanese, PAL), and, of lettered alternatives side by side (Unleashed's "2A", "2B"; "Step 2A"
    /// to "Step 2E"), all but the first.</summary>
    public static HashSet<string> Defaults(IReadOnlyList<ModRoot> roots)
    {
        var on = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var parts = root.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var skip = parts.Any(p => p.Contains("optional", StringComparison.OrdinalIgnoreCase) || p.Contains("japan", StringComparison.OrdinalIgnoreCase) ||
                                      Regex.IsMatch(p, @"\b(JP|JPN|PAL|EU)\b", RegexOptions.CultureInvariant));
            // a lettered alternative other than the first of its siblings
            for (var i = 0; i < parts.Length && !skip; i++)
            {
                if (Variant.Match(parts[i]) is { Success: true } m && !m.Groups[2].Value.Equals("a", StringComparison.OrdinalIgnoreCase))
                {
                    var parent = string.Join('/', parts.Take(i));
                    var number = m.Groups[1].Value;
                    var siblings = roots.Select(r => r.Path.Split('/', StringSplitOptions.RemoveEmptyEntries))
                        .Where(o => o.Length > i && string.Join('/', o.Take(i)).Equals(parent, StringComparison.OrdinalIgnoreCase))
                        .Select(o => Variant.Match(o[i])).Where(o => o.Success && o.Groups[1].Value == number).Select(o => o.Groups[2].Value.ToUpperInvariant());
                    skip = siblings.Any(l => string.CompareOrdinal(l, m.Groups[2].Value.ToUpperInvariant()) < 0);
                }
            }
            if (!skip)
            {
                on.Add(root.Path);
            }
        }
        return on;
    }

    /// <summary>The mod's own picture: a PNG outside its game folders, one named "preview" or after a folder that
    /// says so first, else one at its top. Null: none.</summary>
    private static string? Picture(IReadOnlyList<ModEntry> entries, IEnumerable<string> roots)
    {
        var pngs = entries.Where(e => e.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && e.Size is > 0 and <= 8 << 20).ToList();
        return pngs.FirstOrDefault(e => e.Path.Contains("preview", StringComparison.OrdinalIgnoreCase))?.Path
            ?? pngs.Where(e => !e.Path.Contains('/', StringComparison.Ordinal)).OrderByDescending(e => e.Size).FirstOrDefault()?.Path
            ?? pngs.Where(e => e.Path.Count(c => c == '/') == 1 && roots.Any(r => r.Length == 0 || !e.Path.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(e => e.Size).FirstOrDefault()?.Path;
    }

    /// <summary>Why a file of a part is left out (null: it is kept): another region's, or no game file.</summary>
    internal static string? LeftOut(string discPath)
    {
        var first = discPath.Split('/')[0];
        if (first.Equals("bmp", StringComparison.OrdinalIgnoreCase))
        {
            return "region";
        }
        return first.StartsWith("&&", StringComparison.Ordinal) || ExtraKinds.Any(k => discPath.EndsWith(k, StringComparison.OrdinalIgnoreCase))
            ? "extra"
            : null;
    }

    /// <summary>The import: every part of the mod into <paramref name="modsFolder"/>, beside the mods already there,
    /// those in <paramref name="on"/> ticked. The mod is written under another name and renamed when whole, so a
    /// stopped import leaves nothing. Returns the manifest, or why there is none.</summary>
    /// <param name="disc">The player's disc's files (<see cref="DiscFiles"/>) under <paramref name="gameFiles"/>.</param>
    public static (ModManifest? Mod, string? Refusal) Import(ModSurvey survey, IReadOnlySet<string> on, string gameFiles,
        IReadOnlyDictionary<string, string> disc, string modsFolder, IProgress<ModProgress>? progress = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(on);
        var plan = Plan(survey, disc);
        var id = Id(survey.Source);
        var part = Path.Combine(modsFolder, id + ".part");
        var store = Path.Combine(part, ModManifest.StoreFolder);
        // each part's files by disc path (a later entry of the same path in one part over an earlier one), and the
        // disc paths of its files that are the disc's own
        var parts = survey.Roots.ToDictionary(r => r.Path, _ => (Files: new Dictionary<string, ModFile>(StringComparer.OrdinalIgnoreCase),
            Same: new SortedSet<string>(StringComparer.Ordinal)), StringComparer.Ordinal);
        var known = new Dictionary<(string Disc, string Sha), ModFile?>();   // a file's word, once a disc path and content
        var dropped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var done = 0;
        string? picture = null;
        try
        {
            if (Directory.Exists(part))
            {
                Directory.Delete(part, true);
            }
            Directory.CreateDirectory(store);
            var texts = new HashSet<string>(survey.Texts, StringComparer.Ordinal);
            var kept = new List<ModText>();
            ModArchive.Each(survey.Source, p => plan.ContainsKey(p) || p == survey.Picture || texts.Contains(p), (path, bytes) =>
            {
                if (texts.Contains(path) && bytes.Length <= MostText)
                {
                    kept.Add(KeepText(path, bytes, store));
                }
                if (path == survey.Picture && picture is null)
                {
                    File.WriteAllBytes(Path.Combine(part, ModManifest.PictureFile), bytes);
                    picture = ModManifest.PictureFile;
                }
                if (plan.TryGetValue(path, out var at) && parts.TryGetValue(at.Part, out var into))
                {
                    progress?.Report(new ModProgress(++done, plan.Count));
                    // a file of a kind that is no game file is one where the disc has it (the movies' subtitles: .txt)
                    if (LeftOut(at.Disc) is { } why && !(why == "extra" && disc.ContainsKey(at.Disc)))
                    {
                        dropped[why] = dropped.GetValueOrDefault(why) + 1;
                        return;
                    }
                    var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
                    if (!known.TryGetValue((at.Disc, sha), out var file))
                    {
                        file = Take(at.Disc, bytes, sha, gameFiles, disc, store);
                        known[(at.Disc, sha)] = file;
                    }
                    into.Files.Remove(at.Disc);
                    into.Same.Remove(at.Disc);
                    if (file is null)
                    {
                        dropped["identical"] = dropped.GetValueOrDefault("identical") + 1;
                        into.Same.Add(at.Disc);
                    }
                    else
                    {
                        into.Files[at.Disc] = file;
                    }
                }
            }, cancel);
            if (parts.Values.All(p => p.Files.Count == 0))
            {
                Directory.Delete(part, true);
                return (null, "Every file is the disc's own");
            }
            var list = parts.OrderBy(p => p.Key, ExplorerOrder.Paths).Select(p => new ModPart(p.Key, on.Contains(p.Key),
                [.. p.Value.Files.Values.OrderBy(f => f.Disc, StringComparer.Ordinal)], [.. p.Value.Same])).ToList();
            var manifest = Written(part, id, survey, list, dropped, picture, kept);
            var final = Path.Combine(modsFolder, id);
            if (Directory.Exists(final))
            {
                Directory.Delete(final, true);
            }
            Directory.Move(part, final);
            return (manifest, null);
        }
        catch (OperationCanceledException)
        {
            TryDelete(part);
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
                                      or NotSupportedException or InvalidDataException or SharpCompress.Common.ArchiveException)
        {
            TryDelete(part);
            return (null, "Not imported: " + e.Message);
        }
    }

    /// <summary>The new mod's manifest, written into its folder.</summary>
    private static ModManifest Written(string folder, string id, ModSurvey survey, List<ModPart> parts, SortedDictionary<string, int> dropped,
        string? picture, List<ModText> texts)
    {
        var manifest = new ModManifest(id, survey.Name, Path.GetFileName(Path.TrimEndingDirectorySeparator(survey.Source)), SourceHash(survey.Source),
            DateTimeOffset.UtcNow, parts, dropped, picture) { Texts = [.. texts.OrderBy(t => t.Path, ExplorerOrder.Paths)] };
        ModLibrary.WriteManifest(folder, manifest);
        return manifest;
    }

    /// <summary>One of the mod's text files kept in the store as it came.</summary>
    private static ModText KeepText(string path, byte[] bytes, string store)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var to = Path.Combine(store, sha);
        if (!File.Exists(to))
        {
            File.WriteAllBytes(to, bytes);
        }
        return new ModText(path, bytes.LongLength, sha);
    }

    /// <summary>The part and disc path of each entry that is in a part.</summary>
    private static Dictionary<string, (string Part, string Disc)> Plan(ModSurvey survey, IReadOnlyDictionary<string, string> disc)
    {
        var top = DiscTop(disc);
        var plan = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var entry in survey.Entries)
        {
            if (PartOf(entry.Path, top, disc) is { } at)
            {
                plan[entry.Path] = (at.Root, disc.TryGetValue(at.Inside, out var spelt) ? spelt : at.Inside);
            }
        }
        return plan;
    }

    /// <summary>One file of a part, classed and checked, its bytes kept in the store (once: a file of the same
    /// content is there already) unless it is code; null: it is the disc's own.</summary>
    private static ModFile? Take(string discPath, byte[] bytes, string sha, string gameFiles, IReadOnlyDictionary<string, string> disc, string store)
    {
        var kind = ModGuard.Classify(discPath);
        var vanilla = disc.ContainsKey(discPath) ? File.ReadAllBytes(Path.Combine(gameFiles, discPath)) : null;
        if (vanilla is not null && ModGuard.Same(bytes, vanilla))
        {
            return null;
        }
        var verdict = ModGuard.Check(kind, bytes, vanilla);
        var to = Path.Combine(store, sha);
        if (kind != ModFileClass.Code && !File.Exists(to))
        {
            File.WriteAllBytes(to, bytes);
        }
        return new ModFile(discPath, kind, verdict.Online, verdict.Why, vanilla is null, bytes.LongLength, sha);
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // (left for the next import of the same mod, which clears it first)
        }
    }

    /// <summary>A mod's id: the same source is the same mod (imported again, it replaces itself). Mods of the first
    /// form had the folders chosen in theirs.</summary>
    internal static string Id(string source)
    {
        var key = Path.GetFileName(Path.TrimEndingDirectorySeparator(source)).ToLowerInvariant() + "\nparts";
        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..12];
    }

    /// <summary>The SHA-256 of the mod's file; of a folder, of its files' paths and sizes.</summary>
    private static string SourceHash(string source)
    {
        if (Directory.Exists(source))
        {
            var listing = string.Join("\n", ModArchive.List(source).Entries!.Select(e => $"{e.Path}\t{e.Size}"));
            return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(listing)));
        }
        using var stream = File.OpenRead(source);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
