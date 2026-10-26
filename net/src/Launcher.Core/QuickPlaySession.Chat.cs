using System.Diagnostics.CodeAnalysis;
using Online.Protocol;
using Online.Protocol.Chat;

namespace Launcher.Core;

// The chat's refusals: the server's filter told the player why a line was not sent, or that they are muted.
[SuppressMessage("Maintainability", "CA1506:AvoidExcessiveClassCoupling",
    Justification = "Pre-existing when the code metrics were turned on: to be broken up when the class is next worked on")]
public sealed partial class QuickPlaySession
{
    /// <summary>The server's last word on a line this player sent and it did not deliver (<see cref="ChatRefusal"/>),
    /// or null. A line of the player's own that arrives takes a "not sent" away; a mute lasts its time.</summary>
    public ChatNote? ChatNote { get; private set; }

    private void OnChatRefused(ErrorMessage e)
    {
        ChatNote = new ChatNote(e.Code, e.Seconds, DateTimeOffset.UtcNow);
        Changed?.Invoke();
    }

    /// <summary>The player's own line arrived: a "not sent" before it is over (a mute is not).</summary>
    private void OwnLineArrived()
    {
        if (ChatNote is { Code: not ChatRefusal.Muted })
        {
            ChatNote = null;
        }
    }
}

/// <summary>Why a line was not sent (a <see cref="ChatRefusal"/> code), how long a mute lasted when it came
/// (seconds; 0: a mute lifted), and when it came.</summary>
public sealed record ChatNote(string Code, int? Seconds, DateTimeOffset Since)
{
    /// <summary>How long a "not sent" stays.</summary>
    public static readonly TimeSpan Shown = TimeSpan.FromSeconds(5);

    /// <summary>The label as it is at <paramref name="now"/>: "Not sent", "Muted for 4:32" counting down; null when
    /// it is over.</summary>
    public string? Label(DateTimeOffset now)
    {
        if (Code == ChatRefusal.Muted && Seconds is > 0 and var seconds)
        {
            var left = (int)Math.Ceiling(seconds - (now - Since).TotalSeconds);
            return left > 0 ? ChatRefusal.Label(Code, left) : null;
        }
        return now - Since < Shown ? ChatRefusal.Label(Code, Seconds) : null;
    }
}
