using System.Diagnostics.CodeAnalysis;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.ViewModels;

// The chat's filter as the player sees it: swear words masked in the lines received (the Settings page's "Hide
// swear words"), and under the message box why a line was not sent, or how long the player is muted still.
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
