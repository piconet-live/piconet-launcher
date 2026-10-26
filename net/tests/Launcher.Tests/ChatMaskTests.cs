using Launcher.Core;
using Online.Protocol.Chat;

namespace Launcher.Tests;

/// <summary>
/// The chat filter in the launcher: "Hide swear words" is on unless the player
/// switches it off, and is kept; the mask covers the swear words and the server's always-blocked words as written,
/// and nothing else; the labels under the message box: "not sent" for five seconds, a mute counting down to its end,
/// a lifted mute said once.
/// </summary>
public sealed class ChatMaskTests
{
    [Fact]
    public void HideSwearWordsIsOnUnlessThePlayerSwitchesItOff()
    {
        Assert.True(new LauncherSettings().HideSwearWords);
        var dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;
        try
        {
            var data = Path.Combine(dir, "data");
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "settings.json"), "{\"noServer\": true}");   // (a file from before the setting)
            var state = Launcher.App.AppState.Load(data);
            try
            {
                Assert.True(state.Settings.HideSwearWords);
                state.Update(s => s with { HideSwearWords = false });
            }
            finally
            {
                state.Shutdown();
            }
            var again = Launcher.App.AppState.Load(data);
            Assert.False(again.Settings.HideSwearWords);   // kept
            again.Shutdown();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("what the fuck", "what the ****")]
    [InlineData("F U C K this", "* * * * this")]
    [InlineData("you sp1c", "you ****")]
    [InlineData("sh!tty boost", "****** boost")]
    [InlineData("nice cockpit, great class", "nice cockpit, great class")]
    [InlineData("Scunthorpe hell damn", "Scunthorpe hell damn")]
    [InlineData("こんにちは", "こんにちは")]
    public void TheMaskCoversTheWordsAsWritten(string text, string shown)
    {
        Assert.Equal(shown, ChatMask.Of(text));
        Assert.Equal(shown, ChatMask.Of(text));   // (the kept one)
    }

    [Fact]
    public void TheLabelsUnderTheMessageBox()
    {
        var at = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var blocked = new ChatNote(ChatRefusal.Blocked, null, at);
        Assert.Equal("Not sent", blocked.Label(at.AddSeconds(1)));
        Assert.Null(blocked.Label(at.AddSeconds(6)));
        Assert.Equal("Not sent · no links", new ChatNote(ChatRefusal.Link, null, at).Label(at));
        Assert.Equal("Not sent · said already", new ChatNote(ChatRefusal.Repeat, null, at).Label(at));
        Assert.Equal("Not sent · too fast", new ChatNote(ChatRefusal.TooFast, null, at).Label(at));
        var muted = new ChatNote(ChatRefusal.Muted, 300, at);
        Assert.Equal("Muted for 5:00", muted.Label(at));
        Assert.Equal("Muted for 4:32", muted.Label(at.AddSeconds(28)));
        Assert.Equal("Muted for 0:01", muted.Label(at.AddSeconds(299.5)));
        Assert.Null(muted.Label(at.AddSeconds(300)));
        Assert.Equal("You can chat again", new ChatNote(ChatRefusal.Muted, 0, at).Label(at.AddSeconds(2)));
        Assert.Null(new ChatNote(ChatRefusal.Muted, 0, at).Label(at.AddSeconds(6)));
    }
}
