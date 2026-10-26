using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;

namespace Launcher.Tests;

/// <summary>
/// The connection box at the foot of the left column (<see cref="ConnectionView"/>), laid out without a window. A
/// build that names no server stack has no official server: the dot's word, no server's name and
/// the version, and nothing more. (The test build's mark bound to a value that is null on such a build would
/// leave an empty line there.) A build made for a stack names it, and a test build has one more line.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class ConnectionBoxTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>The box's lines as a player reads them (the text blocks that show, top to bottom, left to right),
    /// its height, and the height of the mark's line.</summary>
    private async Task<(string[] Shown, double Height, double Mark)> Box(StackStamp? stack)
    {
        var state = AppState.Load(Path.Combine(_dir, Guid.NewGuid().ToString("N")));
        var session = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            return await session.Dispatch(() =>
            {
                // the first screen is not passed (the third argument): the dot asks no server
                using var account = new AccountViewModel(state, () => { }, () => false, stack);
                var view = new ConnectionView { DataContext = account };
                var window = new Window { Content = view, Width = 260, Height = 300, SizeToContent = SizeToContent.Height };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var shown = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible)
                    .OrderBy(t => t.TranslatePoint(default, view)!.Value.Y).ThenBy(t => t.TranslatePoint(default, view)!.Value.X)
                    .Select(t => t.Text ?? "").ToArray();
                var mark = view.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "BuildMark");
                // the box's height as the column lays it out: its width, as tall as it needs (the headless window does
                // not always follow a size-to-content box that grows after it is shown)
                view.Measure(new Size(260, double.PositiveInfinity));
                var seen = (shown, view.DesiredSize.Height, mark.IsVisible ? mark.DesiredSize.Height : 0);
                window.Close();
                return seen;
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await session.DisposeAsync();
            state.Shutdown();
        }
    }

    [Fact]
    public async Task ABuildThatNamesNoStackNamesNoServer()
    {
        var none = await Box(StackStamp.From(null, null, null));
        var own = await Box(null);                                   // this build's own stamp: the tests' stack A (TestStack)
        var test = await Box(StackStamp.From("B", "wss://piconet-b-coordinator.fly.dev/ws", "test"));
        var players = await Box(StackStamp.From("B", "wss://piconet-b-coordinator.fly.dev/ws", "public"));

        // the dot's word (the one of a launcher still on its first screen, which asks no server), no server's name
        // (its text block is empty), the version, and no line more
        const string Dot = "No server set";
        Assert.Equal([Dot, "", BuildInfo.Text], none.Shown);
        Assert.Equal([Dot, "official server A", BuildInfo.Text], own.Shown);
        Assert.Equal(0, none.Mark);
        Assert.StartsWith("Version ", BuildInfo.Text, StringComparison.Ordinal);

        // a stack's own build names the stack, in a box of the same height; a test build has its mark under the version
        Assert.Equal([Dot, "official server B", BuildInfo.Text], players.Shown);
        Assert.Equal(none.Height, players.Height);
        Assert.Equal([Dot, "test server B", BuildInfo.Text, "TEST BUILD"], test.Shown);
        Assert.True(test.Mark > 0);
        Assert.Equal(none.Height + test.Mark, test.Height, 0.5);
    }
}
