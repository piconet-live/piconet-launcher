using System.Text.Json;

namespace Launcher.Core;

/// <summary>
/// The server notices this PC received, the last <see cref="Most"/>, newest last, kept in <c>notices.json</c> in the
/// data folder so that they outlast a restart; whether one came that the player has not seen in the list (the
/// bell's dot); and the ids of those whose banner the player dismissed (it stays dismissed). The file is the player's, on their disk: whatever is in it, <see cref="Load"/> never throws; a file
/// that is not this one's JSON is read as no notices (a notice lost is no loss worth a word to the player), and an
/// entry that is not sound is left out. A notice is kept once, by the server's id for it. One thread: the window's.
/// </summary>
public sealed class NoticeLog
{
    /// <summary>The notices kept; the oldest go when there would be more.</summary>
    public const int Most = 100;

    /// <summary>The longest file read: 100 notices are about 40 KB.</summary>
    public const long MostBytes = 1 << 20;

    /// <summary>The longest text and id kept (the server sends at most 200 characters and a short id).</summary>
    public const int MostText = 500;

    public const int MostId = 40;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;
    private readonly List<ServerNotice> _notices;

    private readonly HashSet<string> _dismissed;

    private NoticeLog(string path, List<ServerNotice> notices, bool unseen, IEnumerable<string>? dismissed = null)
    {
        _path = path;
        _notices = notices;
        Unseen = unseen;
        // only ids of notices kept: the list bounds them
        _dismissed = [.. (dismissed ?? []).Where(id => notices.Any(n => n.Id == id))];
    }

    /// <summary>Whether the player dismissed this notice's banner (a notice sent again never shows as one).</summary>
    public bool Dismissed(string? id) => id is not null && _dismissed.Contains(id);

    /// <summary>The player dismissed a notice's banner: kept, so that it stays dismissed after a restart.</summary>
    public void Dismiss(string? id)
    {
        if (id is not null && _notices.Any(n => n.Id == id) && _dismissed.Add(id))
        {
            Save();
        }
    }

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<ServerNotice> Notices => _notices;

    /// <summary>A notice came that the player has not seen in the list since.</summary>
    public bool Unseen { get; private set; }

    public static NoticeLog Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MostBytes)
            {
                return new NoticeLog(path, [], false);
            }
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), Json);
            var notices = (stored?.Notices ?? []).Select(Sound).OfType<ServerNotice>().TakeLast(Most).ToList();
            return new NoticeLog(path, notices, stored?.Unseen == true && notices.Count > 0,
                (stored?.Dismissed ?? []).OfType<string>());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new NoticeLog(path, [], false);
        }
    }

    /// <summary>Keeps a notice and marks it unseen; false (nothing changes) when one with its id is kept already.</summary>
    public bool Add(ServerNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (notice.Id is not null && _notices.Any(n => n.Id == notice.Id))
        {
            return false;
        }
        _notices.Add(notice with { Replayed = false });
        if (_notices.Count > Most)
        {
            _notices.RemoveRange(0, _notices.Count - Most);
            _dismissed.RemoveWhere(id => _notices.All(n => n.Id != id));
        }
        Unseen = true;
        Save();
        return true;
    }

    /// <summary>The player opened the list: nothing is unseen.</summary>
    public void Seen()
    {
        if (Unseen)
        {
            Unseen = false;
            Save();
        }
    }

    /// <summary>Written beside the file, then moved over it; a disk that does not take it costs this run's notices
    /// their next start, and nothing more.</summary>
    private void Save()
    {
        try
        {
            var stored = new Stored(Unseen, [.. _notices.Select(n => new StoredNotice(n.Id, n.Time, ServerNotice.WordOf(n.Level), n.Text))],
                [.. _dismissed]);
            Online.Protocol.AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(stored, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>An entry as a notice; null when it is not one (no time, no text, a text or id too long). A tier it
    /// does not know is info, as from the server.</summary>
    private static ServerNotice? Sound(StoredNotice? n) =>
        n is { Time: { } time, Text: { Length: >= 1 and <= MostText } text } && n.Id is null or { Length: >= 1 and <= MostId }
            ? new ServerNotice(n.Id, ServerNotice.LevelOf(n.Level), text, time)
            : null;

    private sealed record Stored(bool Unseen, List<StoredNotice?>? Notices, List<string?>? Dismissed = null);

    private sealed record StoredNotice(string? Id, DateTimeOffset? Time, string? Level, string? Text);
}
