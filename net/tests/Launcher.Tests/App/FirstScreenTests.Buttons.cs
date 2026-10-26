using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;

namespace Launcher.Tests;

// The left column's buttons that start a game, while a start lacks the game's files or the game program.
public sealed partial class FirstScreenTests
{
    /// <summary>The left column's two buttons as the player sees them: whether each can be pressed, and the line
    /// that says why not.</summary>
    private static (bool Race, string? RaceLine, bool Game, string? GameLine) Buttons(Screen screen)
    {
        var view = new PlayControlView { DataContext = screen.Main.Control };
        var window = new Window { Content = view, Width = 320, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var buttons = view.GetVisualDescendants().OfType<Button>().ToList();
            var race = buttons.Single(b => b.Classes.Contains("go"));
            var game = buttons.Single(b => Equals(b.Content, "Play F-Zero GX"));
            string? Line(string? text) => view.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.IsEffectivelyVisible && t.Text == text && text is { Length: > 0 })?.Text;
            return (race.IsEffectivelyEnabled, Line(screen.Main.Control.Line1), game.IsEffectivelyEnabled, Line(screen.Main.Control.GameNote));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task ThePlayButtonsAreOffWithTheReasonWhileTheGameHasNoFiles()
    {
        var image = _disc.Write(_dir);
        var state = State(stored: image, server: "ws://127.0.0.1:9/ws");
        await OnTheLauncher(state, screen =>
        {
            // the first screen stands before them: "Play F-Zero GX" is off with the reason, and the big button
            // would lead to the preparation
            Assert.NotNull(screen.View);
            Assert.Equal("PREPARE AGAIN", screen.Main.Control.GoText);
            Assert.Equal((true, NotPrepared, false, null), Buttons(screen));     // the reason once, under the big button
            Assert.Equal(NotPrepared, state.PlayOffline());                    // and a start itself says the same

            screen.Press(screen.Continue);
            Until(() => screen.Opened, "the launcher opens");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((true, null, true, null), Buttons(screen));           // the game has its files: both are on
            Assert.Equal("RACE", screen.Main.Control.GoText);
        });
    }

    [Fact]
    public async Task WithoutTheGameProgramTheLauncherOpensAndThePlayButtonsSayWhy()
    {
        const string NotInstalled = "Game program not installed";
        var image = _disc.Write(_dir);
        var state = State(stored: image, program: false, server: "ws://127.0.0.1:9/ws");
        await OnTheLauncher(state, screen =>
        {
            Assert.True(screen.Opened);                                        // nothing could start from prepared files
            Assert.Equal((false, NotInstalled, false, null), Buttons(screen));   // said once, under RACE
            Assert.Equal("RACE", screen.Main.Control.GoText);
            Assert.Equal("", screen.Main.Control.Notice);
            Assert.Equal(PreparedState.NotPrepared, Prepared(state));
        });
        Assert.False(_asked.IsSet);
    }
}
