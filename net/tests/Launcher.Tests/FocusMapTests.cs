using Launcher.Core.Pads;

namespace Launcher.Tests;

/// <summary>
/// Where the pad's focus goes (<see cref="FocusMap"/>), on layouts shaped as the launcher's pages are: the Garage's
/// grid of machine cells, the left column beside a page, a row of buttons over a wider one, a table's rows with their
/// buttons at the end. A control straight that way wins over a nearer one off to the side; nothing that way is null.
/// </summary>
public sealed class FocusMapTests
{
    /// <summary>The Garage's cells: 11 columns of 90 x 70, 6 apart, from (300, 500).</summary>
    private static List<Box> Cells(int count = 41) =>
        [.. Enumerable.Range(0, count).Select(i => new Box(300 + (i % 11 * 96), 500 + (i / 11 * 76), 90, 70))];

    [Fact]
    public void TheGridMovesCellByCell()
    {
        var cells = Cells();
        Assert.Equal(1, FocusMap.Pick(cells[0], PadMove.Right, cells));
        Assert.Equal(11, FocusMap.Pick(cells[0], PadMove.Down, cells));
        Assert.Equal(16, FocusMap.Pick(cells[5], PadMove.Down, cells));
        Assert.Equal(4, FocusMap.Pick(cells[5], PadMove.Left, cells));
        Assert.Equal(5, FocusMap.Pick(cells[16], PadMove.Up, cells));
        Assert.Null(FocusMap.Pick(cells[10], PadMove.Right, cells));   // the row's end
        Assert.Null(FocusMap.Pick(cells[3], PadMove.Up, cells));       // the first row
        // the last row has 8 cells: down from the 10th column's goes to the nearest below, the last one
        Assert.Equal(40, FocusMap.Pick(cells[32], PadMove.Down, cells));
    }

    [Fact]
    public void StraightThatWayWinsOverNearerToTheSide()
    {
        // a wide button far below, and a small one nearer but off to the right
        var from = new Box(100, 100, 120, 30);
        Box below = new(80, 260, 300, 40), aside = new(400, 150, 60, 30);
        Assert.Equal(0, FocusMap.Pick(from, PadMove.Down, [below, aside]));
        // with nothing straight below, the one aside is taken
        Assert.Equal(0, FocusMap.Pick(from, PadMove.Down, [aside]));
        // a settings page: from "Choose…" (right), down to the next row's check box set further left, before the
        // button straight below two rows on
        Box choose = new(820, 147, 77, 30), mute = new(706, 212, 52, 20), apply = new(720, 248, 60, 28), open = new(838, 383, 60, 32);
        Assert.Equal(0, FocusMap.Pick(choose, PadMove.Down, [mute, apply, open]));
        Assert.Equal(1, FocusMap.Pick(mute, PadMove.Down, [mute, apply, open]));
        // the tabs: right from Garage is Replays, not the page heading just under and across the tabs
        Box garage = new(344, 32, 50, 51), replays = new(422, 32, 57, 51), lan = new(386, 100, 104, 32);
        Assert.Equal(0, FocusMap.Pick(garage, PadMove.Right, [replays, lan]));
    }

    [Fact]
    public void TheLeftColumnAndThePageMeetSideBySide()
    {
        // the column: RACE, Play F-Zero GX, Settings; the page: a table's rows with Play at their end
        Box race = new(20, 300, 240, 56), play = new(20, 370, 240, 34), settings = new(20, 660, 120, 30);
        Box tab = new(330, 40, 40, 30), row1 = new(1100, 200, 60, 30), row2 = new(1100, 240, 60, 30), first = new(330, 200, 80, 28);
        List<Box> all = [race, play, settings, tab, row1, row2, first];
        Assert.Equal(6, FocusMap.Pick(race, PadMove.Right, all));      // the page's control nearest that way
        Assert.Equal(0, FocusMap.Pick(first, PadMove.Left, all));      // back to the column
        Assert.Equal(5, FocusMap.Pick(row1, PadMove.Down, all));
        Assert.Equal(1, FocusMap.Pick(race, PadMove.Down, all));
        Assert.Equal(2, FocusMap.Pick(play, PadMove.Down, all));
        Assert.Equal(3, FocusMap.Pick(first, PadMove.Up, all));        // the tab over the page
    }

    [Fact]
    public void FirstIsTheTopRowsLeftmost()
    {
        List<Box> boxes = [new(400, 105, 50, 20), new(100, 300, 50, 20), new(200, 100, 50, 20), new(50, 104, 10, 10)];
        Assert.Equal(3, FocusMap.First(boxes));                        // tops within 8 are one row: its leftmost
        Assert.Null(FocusMap.First([]));
    }

    [Fact]
    public void ABoxThatReachesBackPastTheMiddleIsNotThatWay()
    {
        var from = new Box(100, 100, 100, 100);
        Box overlapping = new(120, 140, 50, 100), beside = new(100, 160, 100, 40);
        Assert.Null(FocusMap.Pick(from, PadMove.Down, [overlapping]));
        Assert.Null(FocusMap.Pick(from, PadMove.Down, [from]));        // never itself
        Assert.Equal(0, FocusMap.Pick(new Box(100, 100, 100, 40), PadMove.Down, [beside]));
    }
}
