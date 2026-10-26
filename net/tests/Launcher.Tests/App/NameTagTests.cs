using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Launcher.App.Controls;
using Launcher.App.ViewModels;
using Launcher.App.Views;

namespace Launcher.Tests;

/// <summary>A player's name with the tag after it, "NAME #4F2A": the tag in the dim colour
/// and a little smaller than the name, wherever it shows; no tag, the name alone.</summary>
[Collection(HeadlessApp.Name)]
public sealed class NameTagTests
{
    private static async Task<T> OnTheWindow<T>(Func<T> look)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            return await session.Dispatch(look, TestContext.Current.CancellationToken);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    /// <summary>The headless window's pointer over a control (its left end, half way down).</summary>
    private static void Hover(Window window, Control control)
    {
        window.MouseMove(control.TranslatePoint(new Point(2, control.Bounds.Height / 2), window)!.Value);
        Dispatcher.UIThread.RunJobs();
    }

    private static string?[] Runs(NameTagText text) => [.. text.Inlines!.OfType<Run>().Select(r => r.Text)];

    /// <summary>The tag shows only while the pointer is over the name.</summary>
    [Fact]
    public async Task TheTagFollowsTheNameDimAndSmallerWhilePointedAt()
    {
        var (hidden, runs, away, nameSize, tagSize, tagBrush, dim, alone) = await OnTheWindow(() =>
        {
            var text = new NameTagText { FontSize = 26, PlayerName = "KESTREL", PlayerTag = "4F2A" };
            var window = new Window { Width = 400, Height = 200, Content = new StackPanel { Children = { text } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var hidden = Runs(text);
            Hover(window, text);
            var runs = Runs(text);
            var tag = text.Inlines!.OfType<Run>().Last();
            var dim = window.FindResource("DimBrush");
            window.MouseMove(new Point(200, 180));
            Dispatcher.UIThread.RunJobs();
            var away = Runs(text);
            Hover(window, text);
            text.PlayerTag = null;
            var alone = Runs(text);
            window.Close();
            return (hidden, runs, away, text.FontSize, tag.FontSize, tag.Foreground, dim, alone);
        });
        Assert.Equal(["KESTREL"], hidden);
        Assert.Equal(["KESTREL", " #4F2A"], runs);
        Assert.Equal(["KESTREL"], away);
        Assert.InRange(tagSize, nameSize * 0.7, nameSize * 0.9);
        Assert.Same(dim, tagBrush);
        Assert.Equal(["KESTREL"], alone);
    }

    /// <summary>A chat line copied whole carries the tag as players read it.</summary>
    [Fact]
    public void AChatLineIsCopiedWithItsTag()
    {
        Assert.Equal("BEN #7KQ2: gg", LobbyChat.Whole(new ChatLine("p2", "BEN", "#FF0000", "gg", you: false, tag: "7KQ2")));
        Assert.Equal("BEN: gg", LobbyChat.Whole(new ChatLine("p2", "BEN", "#FF0000", "gg", you: false)));
        Assert.Equal("BEN #7KQ2", NameTagText.Shown("BEN", "7KQ2"));
    }

    /// <summary>A name too long for its room is cut before the tag, never the tag (a lobby's seat).</summary>
    [Fact]
    public async Task ANameTooLongIsCutBeforeTheTag()
    {
        var (wide, narrow) = await OnTheWindow(() =>
        {
            string[] Shown(double width)
            {
                var text = new NameTagText { FontSize = 14, PlayerName = "MAXIMILIANOVICH", PlayerTag = "4F2A", Width = width };
                var window = new Window { Content = new StackPanel { Children = { text } } };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Hover(window, text);
                var runs = text.Inlines!.OfType<Run>().Select(r => r.Text ?? "").ToArray();
                window.Close();
                return runs;
            }
            return (Shown(400), Shown(90));
        });
        Assert.Equal(["MAXIMILIANOVICH", " #4F2A"], wide);
        Assert.Equal(" #4F2A", narrow[1]);
        Assert.EndsWith("…", narrow[0], StringComparison.Ordinal);
        Assert.StartsWith(narrow[0][..^1], "MAXIMILIANOVICH", StringComparison.Ordinal);
        Assert.InRange(narrow[0].Length, 2, 14);
    }

    /// <summary>A name of emoji is cut between them, never inside one's pair of characters.</summary>
    [Fact]
    public async Task ANameIsNeverCutInsideAnEmoji()
    {
        const string Name = "A\U0001F680\U0001F680\U0001F680\U0001F680\U0001F680\U0001F680\U0001F680\U0001F680\U0001F680\U0001F680";
        var cuts = await OnTheWindow(() =>
        {
            var shown = new List<string>();
            foreach (var width in new[] { 40.0, 50, 60, 70, 80, 90, 100, 110 })
            {
                var text = new NameTagText { FontSize = 14, PlayerName = Name, PlayerTag = "4F2A", Width = width };
                var window = new Window { Content = new StackPanel { Children = { text } } };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Hover(window, text);
                shown.Add(text.Inlines!.OfType<Run>().First().Text ?? "");
                window.Close();
            }
            return shown;
        });
        Assert.Contains(cuts, c => c.EndsWith('…'));
        Assert.All(cuts.Where(c => c.EndsWith('…')), c =>
        {
            var kept = c[..^1];
            Assert.StartsWith(kept, Name, StringComparison.Ordinal);
            Assert.False(char.IsHighSurrogate(kept[^1]), $"cut inside an emoji: {kept.Length} characters");
        });
    }

    /// <summary>The left column's name leans (the display role's skew): its box holds the lean's reach, and a name too
    /// long is cut with its ellipsis so that its letters and the reach fit the room, at a render scaling of 1.25 and
    /// 1.5 as on a scaled Windows display (where a last K was cut flat).</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public async Task ALeaningNameKeepsItsReachInItsBoxAtAnyScaling(double scaling)
    {
        const double Room = 208;   // the left column's width inside its margins
        var (shortRuns, shortBox, shortLetters, longRuns, longBox, longLetters, reach) = await OnTheWindow(() =>
        {
            NameTagText Racer(string name)
            {
                var text = new NameTagText { PlayerName = name, PlayerTag = "4F2A" };
                text.Classes.Add("racer");
                return text;
            }
            var shortName = Racer("DEEP40K");
            var longName = Racer("MAXIMILIANOVICH");
            var column = new StackPanel { Width = Room, HorizontalAlignment = HorizontalAlignment.Left, Spacing = 8 };
            column.Children.Add(new Button { Classes = { "plain" }, HorizontalAlignment = HorizontalAlignment.Left, Content = shortName });
            column.Children.Add(new Button { Classes = { "plain" }, HorizontalAlignment = HorizontalAlignment.Left, Content = longName });
            var window = new Window { Width = 400, Height = 200, Content = column };
            window.Show();
            window.SetRenderScaling(scaling);
            Dispatcher.UIThread.RunJobs();
            var reach = NameTagText.Overhang(shortName.RenderTransform, shortName.RenderTransformOrigin, shortName.Bounds.Size);
            var result = (Runs(shortName), shortName.Bounds.Width, shortName.TextLayout.WidthIncludingTrailingWhitespace,
                Runs(longName), longName.Bounds.Width, longName.TextLayout.WidthIncludingTrailingWhitespace, reach);
            window.Close();
            return result;
        });
        // the capitals (0.688 of the size) under a 12 degree lean reach at least this far past the letters
        Assert.InRange(reach, 40 * 0.688 * Math.Tan(12 * Math.PI / 180), 40);
        Assert.Equal(["DEEP40K"], shortRuns);
        Assert.True(shortBox >= shortLetters + reach - 0.5, $"box {shortBox}, letters {shortLetters} + reach {reach}");
        Assert.EndsWith("…", longRuns[0], StringComparison.Ordinal);
        Assert.StartsWith(longRuns[0]![..^1], "MAXIMILIANOVICH", StringComparison.Ordinal);
        Assert.True(longLetters + reach <= Room, $"letters {longLetters} + reach {reach} past {Room}");
        Assert.True(longBox <= Room + 0.5, $"box {longBox} past {Room}");
    }

    /// <summary>In a seat's row (a grid's star column beside a machine's name) a name that fits stays whole.</summary>
    [Fact]
    public async Task ANameThatFitsItsColumnStaysWhole()
    {
        var shown = await OnTheWindow(() =>
        {
            var text = new NameTagText { FontSize = 14, PlayerName = "KESTREL", PlayerTag = "9KX5" };
            Grid.SetColumn(text, 2);
            var machine = new TextBlock { Text = "Red Gazelle" };
            Grid.SetColumn(machine, 3);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,16,*,Auto,48,18"), Children = { text, machine } };
            var window = new Window { Width = 340, Content = new StackPanel { Children = { row } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Hover(window, text);
            var runs = string.Join("|", text.Inlines!.OfType<Run>().Select(r => r.Text ?? "")) + $" {text.Bounds.Width}";
            window.Close();
            return runs;
        });
        Assert.StartsWith("KESTREL| #9KX5", shown, StringComparison.Ordinal);
    }
}
