using System.Globalization;
using Launcher.Core;

namespace Launcher.App.ViewModels;

/// <summary>A server notice as <c>Views/NoticeCard</c> draws it, the same in the chat, the banner and the list: its
/// tier's word (the card's look), the label ("SYSTEM · WARNING"), when (the player's clock), and the text.</summary>
public sealed record NoticeItem(string Level, string Label, string When, string Text)
{
    /// <summary>Which showing of the banner this is (0: no banner): each one is a new item, so one with the text,
    /// tier and minute of the one before still starts the banner's bar again with its timer.</summary>
    public long Showing { get; init; }

    public static NoticeItem Of(ServerNotice notice, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var word = ServerNotice.WordOf(notice.Level);
        var local = notice.Time.ToLocalTime();
        var today = (now ?? DateTimeOffset.Now).ToLocalTime().Date == local.Date;
        return new NoticeItem(word, $"{Online.Protocol.Notices.SenderName} · {word.ToUpperInvariant()}",
            local.ToString(today ? "HH:mm" : "MMM d, HH:mm", CultureInfo.InvariantCulture), notice.Text);
    }
}
