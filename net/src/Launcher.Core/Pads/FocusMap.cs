namespace Launcher.Core.Pads;

/// <summary>Where a control is on the screen, in the window's units.</summary>
public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);
}

/// <summary>
/// Where the pad's focus goes, by position on the screen: from the control that has it, in a direction, to the
/// nearest control that lies that way. A control straight that way (its span across the move overlaps the focus's)
/// is preferred to one off to the side: the side gap counts one and a half times the gap ahead, and a little more on
/// top, enough that a grid goes cell by cell and a tab to the tab beside it, and little enough that a row of controls set further across still comes before the row
/// after it (a settings page's rows). Plain maths on boxes, so that a page's layout is tested without a window.
/// </summary>
public static class FocusMap
{
    /// <summary>The weight of the gap to the side against the gap ahead.</summary>
    private const double SideWeight = 1.5;

    /// <summary>What a control off to the side pays besides its gap: one straight that way and near wins (a tab's
    /// neighbour before the heading just under the tabs).</summary>
    private const double OffBeam = 30;

    /// <summary>The nearest box that way from <paramref name="from"/>; null: none lies that way.</summary>
    public static int? Pick(Box from, PadMove move, IReadOnlyList<Box> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        int? best = null;
        double bestScore = double.MaxValue, bestCentre = double.MaxValue;
        for (var i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            if (box == from || Score(from, move, box) is not var (score, centre))
            {
                continue;
            }
            if (score < bestScore - 0.5 || (Math.Abs(score - bestScore) <= 0.5 && centre < bestCentre))
            {
                (best, bestScore, bestCentre) = (i, score, centre);
            }
        }
        return best;
    }

    /// <summary>The first box in reading order: the highest row, and in it the leftmost (a row: tops within 8).</summary>
    public static int? First(IReadOnlyList<Box> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        int? first = null;
        for (var i = 0; i < boxes.Count; i++)
        {
            if (first is not { } f || boxes[i].Y < boxes[f].Y - 8 || (Math.Abs(boxes[i].Y - boxes[f].Y) <= 8 && boxes[i].X < boxes[f].X))
            {
                first = i;
            }
        }
        return first;
    }

    /// <summary>A box's score that way (lower is nearer) and its centre's distance to the side; null: it does not
    /// lie that way. A box lies that way when its centre is beyond the focus's and it does not reach back past the
    /// focus's middle.</summary>
    private static (double Score, double Centre)? Score(Box from, PadMove move, Box box)
    {
        double ahead, side, centre;
        switch (move)
        {
            case PadMove.Down when box.CenterY > from.CenterY + 1 && box.Y >= from.CenterY:
                ahead = box.Y - from.Bottom;
                side = Gap(from.X, from.Right, box.X, box.Right);
                centre = Math.Abs(box.CenterX - from.CenterX);
                break;
            case PadMove.Up when box.CenterY < from.CenterY - 1 && box.Bottom <= from.CenterY:
                ahead = from.Y - box.Bottom;
                side = Gap(from.X, from.Right, box.X, box.Right);
                centre = Math.Abs(box.CenterX - from.CenterX);
                break;
            case PadMove.Right when box.CenterX > from.CenterX + 1 && box.X >= from.CenterX:
                ahead = box.X - from.Right;
                side = Gap(from.Y, from.Bottom, box.Y, box.Bottom);
                centre = Math.Abs(box.CenterY - from.CenterY);
                break;
            case PadMove.Left when box.CenterX < from.CenterX - 1 && box.Right <= from.CenterX:
                ahead = from.X - box.Right;
                side = Gap(from.Y, from.Bottom, box.Y, box.Bottom);
                centre = Math.Abs(box.CenterY - from.CenterY);
                break;
            default:
                return null;
        }
        return (Math.Max(ahead, 0) + (SideWeight * side) + (side > 0 ? OffBeam : 0), centre);
    }

    /// <summary>The gap between two spans; 0 when they overlap.</summary>
    private static double Gap(double start, double end, double otherStart, double otherEnd) =>
        Math.Max(0, Math.Max(otherStart - end, start - otherEnd));
}
