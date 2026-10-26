using Online.Protocol;

namespace Launcher.Core;

/// <summary>The four tiers of a server's notice (<see cref="Notices"/>), least first: each does what the one before
/// does and more. Info: a line in the chat. Notice: a banner for a while. Warning: a banner until dismissed, and the
/// taskbar flashes. Urgent: and a short sound.</summary>
public enum NoticeLevel
{
    Info,
    Notice,
    Warning,
    Urgent,
}

/// <summary>
/// A notice from the server's operator, as the launcher keeps and shows it: the server's id for it (null from a
/// server that gives none: it is then never taken for one already kept), its tier, the operator's text (without the
/// tier's word the server puts in front for older launchers), when it was sent, and whether this is a notice of the
/// last hour sent again to a launcher that came later (<see cref="Replayed"/>: no chat line, no sound, no flash).
/// </summary>
public sealed record ServerNotice(string? Id, NoticeLevel Level, string Text, DateTimeOffset Time, bool Replayed = false)
{
    /// <summary>The notice a chat line is, checked by <c>ServerMessages</c> first; null when it is a player's line.
    /// The server is known by its id (<see cref="Notices.ServerId"/>), never by a name: names are not unique.</summary>
    public static ServerNotice? Of(ChatMessage line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.FromId != Notices.ServerId || line.Channel != ControlProtocol.GlobalChannel)
        {
            return null;
        }
        var word = Notices.Known(line.Level) ? line.Level : Notices.Info;
        return new ServerNotice(line.NoticeId, LevelOf(word), Notices.Unprefixed(word, line.Text), line.Time);
    }

    /// <summary>A notice of the last hour the server sent again (checked by <c>ServerMessages</c> first): replayed.</summary>
    public static ServerNotice Of(RecentNotice recent)
    {
        ArgumentNullException.ThrowIfNull(recent);
        return new ServerNotice(recent.Id, LevelOf(recent.Level), recent.Text, recent.Time, Replayed: true);
    }

    /// <summary>A tier by its word; one that is not known is the least, info.</summary>
    public static NoticeLevel LevelOf(string? word) => word switch
    {
        Notices.Notice => NoticeLevel.Notice,
        Notices.Warning => NoticeLevel.Warning,
        Notices.Urgent => NoticeLevel.Urgent,
        _ => NoticeLevel.Info,
    };

    /// <summary>The tier's word, as the server writes it.</summary>
    public static string WordOf(NoticeLevel level) => Notices.Levels[Math.Clamp((int)level, 0, Notices.Levels.Count - 1)];
}
