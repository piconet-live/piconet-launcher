using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Launcher.Core;

/// <summary>A folder of a mod laid out like the disc's <c>files/</c>, one of which the player chooses where there
/// are several (variants, optional steps). <paramref name="Path"/> is its place in the mod ("" its top).</summary>
public sealed record ModRoot(string Path, int Files, bool OnByDefault)
{
    /// <summary>What the chooser shows: its folders, a "root" folder of a mod's own left out.</summary>
    public string Label => Path.Length == 0 ? "All files" : Regex.Replace(Path, "(^|/)root$", "", RegexOptions.IgnoreCase).Replace("/", " / ", StringComparison.Ordinal) is { Length: > 0 } l ? l : Path;
}

/// <summary>What a first look at a mod found: its name, its folders laid out like the disc, and its own picture.</summary>
public sealed record ModSurvey(string Source, string Name, IReadOnlyList<ModRoot> Roots, string? Picture, IReadOnlyList<ModEntry> Entries)
{
    /// <summary>The player has a choice to make: more than one folder.</summary>
    public bool AsksChoice => Roots.Count > 1;
}

/// <summary>How far an import is: files done of all to read.</summary>
public readonly record struct ModProgress(int Done, int Total);

/// <summary>
/// Makes a mod the launcher can lay over the game: the mod's chosen folders unpacked under the data folder's
/// <c>Mods/ID/files</c> by their path on the disc, a later folder over an earlier one, with what is no game file
/// left out (save files, cheat codes, emulator settings, the disc's system files, previews, loose pictures, the
/// Japanese disc's <c>bmp</c>) and every file that is the player's own disc's. Each file kept is classed and checked
/// by <see cref="ModGuard"/>, and the mod's own manifest (<see cref="ModManifest"/>) says so. Nothing is rebuilt and
/// nothing in the mod is run.
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
        if (entries is null)
        {
            return (null, refusal);
        }
        var top = DiscTop(disc);
        var roots = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var parts = entry.Path.Split('/');
            for (var i = 0; i < parts.Length; i++)
            {
                // a disc folder (a folder below it) or a disc file (the last part) at depth i: the folder above is a root
                if (top.Contains(parts[i]) && (i < parts.Length - 1 || disc.ContainsKey(parts[i])))
                {
                    var root = string.Join('/', parts.Take(i));
                    roots[root] = roots.GetValueOrDefault(root) + 1;
                    break;
                }
            }
        }
        if (roots.Count == 0)
        {
            return (null, "No game files");
        }
        var list = roots.Select(r => new ModRoot(r.Key, r.Value, false)).ToList();
        var defaults = Defaults(list);
        list = [.. list.Select(r => r with { OnByDefault = defaults.Contains(r.Path) })];
        var name = Directory.Exists(source) ? Path.GetFileName(Path.TrimEndingDirectorySeparator(source)) : Path.GetFileNameWithoutExtension(source);
        return (new ModSurvey(source, NameOf(name), list, Picture(entries, roots.Keys), entries), null);
    }

    private static string NameOf(string file)
    {
        var name = file.Replace('_', ' ').Trim();
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

    /// <summary>Why a file of a chosen folder is left out (null: it is kept): another region's, or no game file.</summary>
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

    /// <summary>The import: the chosen folders (in the order given, a later one's file over an earlier one's) into
    /// <paramref name="modsFolder"/>, beside the mods already there. The mod is written under another name and
    /// renamed when whole, so a stopped import leaves nothing. Returns the manifest, or why there is none.</summary>
    /// <param name="disc">The player's disc's files (<see cref="DiscFiles"/>) under <paramref name="gameFiles"/>.</param>
    public static (ModManifest? Mod, string? Refusal) Import(ModSurvey survey, IReadOnlyList<string> chosen, string gameFiles,
        IReadOnlyDictionary<string, string> disc, string modsFolder, IProgress<ModProgress>? progress = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(chosen);
        if (chosen.Count == 0)
        {
            return (null, "Nothing chosen");
        }
        var plan = Plan(survey, chosen, disc);
        var id = Id(survey.Source, chosen);
        var part = Path.Combine(modsFolder, id + ".part");
        var files = new List<ModFile>();
        var dropped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var done = 0;
        string? picture = null;
        try
        {
            if (Directory.Exists(part))
            {
                Directory.Delete(part, true);
            }
            Directory.CreateDirectory(Path.Combine(part, "files"));
            ModArchive.Each(survey.Source, p => plan.ContainsKey(p) || p == survey.Picture, (path, bytes) =>
            {
                if (path == survey.Picture && picture is null)
                {
                    File.WriteAllBytes(Path.Combine(part, ModManifest.PictureFile), bytes);
                    picture = ModManifest.PictureFile;
                }
                if (plan.TryGetValue(path, out var discPath))
                {
                    progress?.Report(new ModProgress(++done, plan.Count));
                    if (Take(discPath, bytes, gameFiles, disc, part, dropped) is { } file)
                    {
                        files.Add(file);
                    }
                }
            }, cancel);
            if (files.Count == 0)
            {
                Directory.Delete(part, true);
                return (null, "Every file is the disc's own");
            }
            var manifest = new ModManifest(id, survey.Name, Path.GetFileName(Path.TrimEndingDirectorySeparator(survey.Source)), SourceHash(survey.Source),
                DateTimeOffset.UtcNow, chosen, [.. files.OrderBy(f => f.Disc, StringComparer.Ordinal)], dropped, picture);
            ModLibrary.WriteManifest(part, manifest);
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

    /// <summary>The disc path of each entry of a chosen folder, the last chosen folder's where two give one path.</summary>
    private static Dictionary<string, string> Plan(ModSurvey survey, IReadOnlyList<string> chosen, IReadOnlyDictionary<string, string> disc)
    {
        var top = DiscTop(disc);
        var plan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // entry -> disc path
        var byDisc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);  // disc path -> entry
        foreach (var root in chosen)
        {
            foreach (var entry in survey.Entries)
            {
                var rest = root.Length == 0 ? entry.Path : entry.Path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase) ? entry.Path[(root.Length + 1)..] : null;
                if (rest is null || !top.Contains(rest.Split('/')[0]) || (!rest.Contains('/', StringComparison.Ordinal) && !disc.ContainsKey(rest)))
                {
                    continue;
                }
                var discPath = disc.TryGetValue(rest, out var spelt) ? spelt : rest;
                if (byDisc.TryGetValue(discPath, out var earlier))
                {
                    plan.Remove(earlier);
                }
                byDisc[discPath] = entry.Path;
                plan[entry.Path] = discPath;
            }
        }
        return plan;
    }

    /// <summary>One file of the mod: left out (counted by why), or classed, checked and kept.</summary>
    private static ModFile? Take(string discPath, byte[] bytes, string gameFiles, IReadOnlyDictionary<string, string> disc, string part,
        SortedDictionary<string, int> dropped)
    {
        if (LeftOut(discPath) is { } why)
        {
            dropped[why] = dropped.GetValueOrDefault(why) + 1;
            return null;
        }
        var kind = ModGuard.Classify(discPath);
        var vanilla = disc.ContainsKey(discPath) ? File.ReadAllBytes(Path.Combine(gameFiles, discPath)) : null;
        if (vanilla is not null && ModGuard.Same(bytes, vanilla))
        {
            dropped["identical"] = dropped.GetValueOrDefault("identical") + 1;
            return null;
        }
        var verdict = ModGuard.Check(kind, bytes, vanilla);
        if (kind != ModFileClass.Code)
        {
            var to = Path.Combine(part, "files", discPath);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.WriteAllBytes(to, bytes);
        }
        return new ModFile(discPath, kind, verdict.Online, verdict.Why, vanilla is null, bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)));
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

    /// <summary>A mod's id: the same source with the same choices is the same mod (imported again, it replaces itself).</summary>
    internal static string Id(string source, IReadOnlyList<string> chosen)
    {
        var key = Path.GetFileName(Path.TrimEndingDirectorySeparator(source)).ToLowerInvariant() + "\n" + string.Join("\n", chosen);
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
