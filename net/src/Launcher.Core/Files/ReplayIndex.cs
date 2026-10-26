using System.Text.Json;

namespace Launcher.Core;

/// <summary>
/// The replay index: what the Stats page counts of each replay (<see cref="ReplayRow"/>), kept in
/// <c>cache/replay-index.json</c> in the data folder, so that a start reads only the replays that are new or changed
/// since the last (a file is known by its path, length and last write; one that is not a replay is kept as none, and
/// not read again until it changes). A replay gone from the folder leaves the index. The index is the launcher's own
/// working file: one that cannot be read, or of another <see cref="Format"/>, is made again from the replays.
/// </summary>
public static class ReplayIndex
{
    /// <summary>The index's format: raised whenever <see cref="ReplayRow"/> changes, and the index is made again.</summary>
    public const int Format = 1;

    /// <summary>The largest index read back (one row is a few hundred bytes: this is tens of thousands of replays).</summary>
    private const long Largest = 32L << 20;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The index file of a data folder.</summary>
    public static string FileIn(string dataFolder) => Path.Combine(dataFolder, "cache", "replay-index.json");

    /// <summary>One file as indexed: its row, or null when it is not a replay this launcher lists.</summary>
    private sealed record Entry(string Path, long Length, long Written, ReplayRow? Row);

    private sealed record Kept(int Format, IReadOnlyList<Entry> Entries);

    /// <summary>The rows of the replays in <paramref name="replayFolder"/>, newest first, reading only the files
    /// the index does not know as they are; the index written again when anything changed. A folder that is none
    /// or cannot be listed has no rows (and leaves the index as it is).</summary>
    public static IReadOnlyList<ReplayRow> Rows(string? replayFolder, string indexFile)
    {
        ArgumentNullException.ThrowIfNull(indexFile);
        if (replayFolder is null || !Directory.Exists(replayFolder))
        {
            return [];
        }
        var known = Load(indexFile);
        var entries = new List<Entry>();
        var changed = false;
        try
        {
            foreach (var path in Directory.EnumerateFiles(replayFolder, "*.fzrp"))
            {
                var info = new FileInfo(path);
                var (length, written) = (info.Length, info.LastWriteTimeUtc.Ticks);
                if (known.TryGetValue(path, out var entry) && entry.Length == length && entry.Written == written)
                {
                    entries.Add(entry);
                    continue;
                }
                entries.Add(new Entry(path, length, written, ReplayFile.Read(path) is { } replay ? ReplayRow.Of(replay) : null));
                changed = true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                                      or System.Security.SecurityException)
        {
            return [];
        }
        changed |= entries.Count != known.Count;
        if (changed)
        {
            Save(indexFile, entries);
        }
        return [.. entries.Select(e => e.Row).OfType<ReplayRow>().OrderByDescending(r => r.Recorded)];
    }

    private static Dictionary<string, Entry> Load(string indexFile)
    {
        try
        {
            var info = new FileInfo(indexFile);
            if (!info.Exists || info.Length > Largest)
            {
                return [];
            }
            var kept = JsonSerializer.Deserialize<Kept>(File.ReadAllText(indexFile), Json);
            if (kept is not { Format: Format, Entries: { } list })
            {
                return [];
            }
            var byPath = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var entry in list.Where(e => e?.Path is not null && (e.Row is null || e.Row.Machine is not null)))
            {
                byPath[entry.Path] = entry;
            }
            return byPath;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException
                                      or ArgumentException or InvalidOperationException)
        {
            return [];
        }
    }

    private static void Save(string indexFile, List<Entry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(indexFile)!);
            Online.Protocol.AtomicFile.WriteAllText(indexFile, JsonSerializer.Serialize(new Kept(Format, entries), Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // made again next time
        }
    }
}
