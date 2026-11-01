using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Launcher.App.Controls;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>The window's pages (Controls/PageDeck.cs): the window opens on one, the others come in behind it, and a
/// page's rows come back into view as a new list's do (Controls/Motion.cs).</summary>
[Collection(HeadlessApp.Name)]
public sealed class PageDeckTests
{
    private static readonly string[] Rows = ["a", "b", "c"];

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
    public Task TheWindowOpensOnOnePageAndTheOthersComeInBehindIt() => OnTheApplication(() =>
    {
        var deck = new PageDeck { Pages = new object[] { "home", "garage", "lobby" }, Current = "home" };
        var window = new Window { Content = deck, Width = 800, Height = 600 };
        window.Show();
        Assert.Single(deck.Children);
        Assert.False(deck.Ready);
        Pump(() => deck.Ready, "the pages did not come in");
        Assert.Equal(3, deck.Children.Count);
        Assert.Single(deck.Children, c => c.IsVisible);
        deck.Current = "lobby";
        Assert.Single(deck.Children, c => c.IsVisible);
        Assert.True(deck.Children[^1].IsVisible);
        window.Close();
    });

    [Fact]
    public Task APageGoneToBeforeItsTurnShowsAtOnceAndIsNotAddedTwice() => OnTheApplication(() =>
    {
        var deck = new PageDeck { Pages = new object[] { "home", "garage", "lobby" }, Current = "home" };
        var window = new Window { Content = deck, Width = 800, Height = 600 };
        window.Show();
        deck.Current = "lobby";
        Assert.Equal(2, deck.Children.Count);
        Assert.Single(deck.Children, c => c.IsVisible);
        Pump(() => deck.Ready, "the pages did not come in");
        Assert.Equal(3, deck.Children.Count);
        Assert.Single(deck.Children, c => c.IsVisible);
        window.Close();
    });

    [Fact]
    public Task RowsComingBackWithAPageEnterAndEndAsTheyWere() => OnTheApplication(() =>
    {
        var list = new ItemsControl();
        Motion.SetStagger(list, true);
        Paced.SetItems(list, Rows);
        var page = new StackPanel { Children = { list } };
        var window = new Window { Content = page, Width = 800, Height = 600 };
        window.Show();
        Pump(() => list.GetRealizedContainers().Count() == 3, "the rows did not come");
        Motion.Settle();
        Dispatcher.UIThread.RunJobs();
        var rows = list.GetRealizedContainers().ToList();
        Assert.All(rows, r => Assert.Equal(1, r.Opacity));

        // as the deck does when the page shows again: no row is made again, each starts its entrance
        Motion.PageShown(page);
        Assert.Equal(rows, list.GetRealizedContainers());
        Assert.All(rows, r => Assert.True(r.Opacity < 1));

        Motion.Settle();
        Dispatcher.UIThread.RunJobs();
        Assert.All(rows, r => Assert.Equal(1, r.Opacity));
        window.Close();
    });
}
