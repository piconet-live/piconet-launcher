using System.Diagnostics.CodeAnalysis;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

// The chat's lines: the lobby's, with the server's notices (the chat sidebar shows them: ChatSidebarViewModel). The chat's filter as the player sees it: swear words masked in the
// lines received (the Settings page's "Hide swear words"), and under the message box why a line was not sent, or how
// long the player is muted still.
[SuppressMessage("Maintainability", "CA1506:AvoidExcessiveClassCoupling",
    Justification = "Pre-existing when the code metrics were turned on: to be broken up when the class is next worked on")]
public sealed partial class LobbyViewModel
{
    private readonly DispatcherTimer _noteClock = new() { Interval = TimeSpan.FromMilliseconds(500) };

    /// <summary>"Not sent", "Not sent · no links", "Muted for 4:32"; empty: nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChatNoteShown))]
    public partial string ChatNote { get; set; } = "";

    public bool ChatNoteShown => ChatNote.Length > 0;

    private string _chatKey = "";   // what the lines were last made from: the session's lines, the paints, the mask

    /// <summary>The lobby's lines and the server's notices (which come on the global channel, as in 0.6.0); a player's
    /// line on the global channel is not shown (the launcher sends none: the owner, 2026-11-08). Rebuilt only when a line came, a speaker's paint changed or the mask was
    /// switched (the race's live reports refresh the lobby many times a second), so the chat keeps its place while
    /// read. Every line kept shows (<see cref="QuickPlaySession.ChatKept"/>).</summary>
    private void ShowChatLines(QuickPlaySession session, LobbyState? lobby)
    {
        var paints = new Dictionary<string, string>();
        foreach (var p in lobby?.Players ?? [])
        {
            paints[p.Id] = Rgb.Hex(p.Color);
        }
        var key = $"{session.ChatVersion}|{_state.Settings.HideSwearWords}|{string.Join(",", paints.Select(p => p.Key + p.Value))}";
        if (key == _chatKey)
        {
            return;
        }
        _chatKey = key;
        var all = session.Chat;
        // a server's notice (known by its id, never its name) is the server's own line, its text without the tier's word
        ChatLine Line(ChatMessage c) => ServerNotice.Of(c) is { } notice
            ? new ChatLine(c.FromId, Notices.SenderName, "#000000", notice.Text, you: false, NoticeItem.Of(notice))
            : new ChatLine(c.FromId, c.FromName, paints.TryGetValue(c.FromId, out var paint) ? paint : "#8792A2", Shown(c),
                c.FromId == session.PlayerId, tag: c.FromTag);
        ShowChat(Chat, [.. all.Where(c => c.Channel != ControlProtocol.GlobalChannel || ServerNotice.Of(c) is not null).Select(Line)]);
    }

    /// <summary>A line's text as this player sees it: its swear words masked unless they show them.</summary>
    private string Shown(ChatMessage c) => _state.Settings.HideSwearWords ? ChatMask.Of(c.Text) : c.Text;

    /// <summary>The session's chat note as it is now; a mute counts down every half second until it is over.</summary>
    private void ShowChatNote(QuickPlaySession? session)
    {
        ChatNote = session?.ChatNote?.Label(DateTimeOffset.UtcNow) ?? "";
        if (ChatNoteShown && !_noteClock.IsEnabled)
        {
            _noteClock.Start();
        }
        else if (!ChatNoteShown && _noteClock.IsEnabled)
        {
            _noteClock.Stop();
        }
    }
}
