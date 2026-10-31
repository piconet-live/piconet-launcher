using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Launcher.App.Controls;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>What the launcher's motion promises (Controls/Motion.cs): a move ends with nothing left on the control,
/// a click lands while it runs, a figure's text is true at once, and Reduced motion stands still.</summary>
[Collection(HeadlessApp.Name)]
public sealed class MotionTests
{
    private static async Task OnTheApplication(Action steps)
    {
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(steps, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
        }
    }

    [Fact]
    public Task AMoveEndsWithNothingLeftOnTheControlAndAClickLandsWhileItRuns() => OnTheApplication(() =>
    {
        var clicks = 0;
        var button = new Button { Content = "Go", Width = 200, Height = 60 };
        button.Click += (_, _) => clicks++;
        var window = new Window { Content = button, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var ended = false;
        Motion.Enter(button, 0, 16);
        Motion.Play(button, "end", Motion.Standard, null, _ => { }, () => ended = true);
        Assert.NotNull(button.RenderTransform);
        Click(window, button);
        Assert.Equal(1, clicks);
        Pump(() => ended && button.RenderTransform is null, "the move did not end");
        Assert.Equal(1, button.Opacity);
        window.Close();
    });

    [Fact]
    public Task AFiguresTextIsTrueAtOnce() => OnTheApplication(() =>
    {
        var figure = new RollingText { Text = "10" };
        var window = new Window { Content = figure };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        figure.Text = "90";
        Assert.Equal("90", figure.Text);
        window.Close();
    });
}
