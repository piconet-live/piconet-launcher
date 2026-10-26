using Avalonia;
using Avalonia.Controls;

namespace Launcher.App.Controls;

/// <summary>
/// The Garage's roster: tiles of one size (<see cref="TileWidth"/> x <see cref="TileHeight"/>) in rows of
/// <see cref="Columns"/>, <see cref="Gap"/> apart, from the left edge: a shelf of a few keeps the tiles' size and stands
/// at the left. The view picks the size and the columns for the room (<see cref="Fit"/>), so that the whole roster
/// shows without scrolling.
/// </summary>
public sealed class RosterTiles : Panel
{
    public static readonly StyledProperty<int> ColumnsProperty = AvaloniaProperty.Register<RosterTiles, int>(nameof(Columns), 8);

    public static readonly StyledProperty<double> TileWidthProperty = AvaloniaProperty.Register<RosterTiles, double>(nameof(TileWidth), 112);

    public static readonly StyledProperty<double> TileHeightProperty = AvaloniaProperty.Register<RosterTiles, double>(nameof(TileHeight), 88);

    public static readonly StyledProperty<double> GapProperty = AvaloniaProperty.Register<RosterTiles, double>(nameof(Gap), 8);

    static RosterTiles() => AffectsMeasure<RosterTiles>(ColumnsProperty, TileWidthProperty, TileHeightProperty, GapProperty);

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public double TileWidth
    {
        get => GetValue(TileWidthProperty);
        set => SetValue(TileWidthProperty, value);
    }

    public double TileHeight
    {
        get => GetValue(TileHeightProperty);
        set => SetValue(TileHeightProperty, value);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>The still's shape in a tile (176 x 104, MachineStill's).</summary>
    public const double StillAspect = 176.0 / 104.0;

    /// <summary>A tile's name line, under its still (two lines when a name needs them), and the still's margin.</summary>
    public const double NameLine = 24, TwoLines = 44, StillMargin = 4;

    /// <summary>The still at its least before a name of two lines gives way to one line a size smaller.</summary>
    public const double LeastStill = 32;

    /// <summary>The still at its largest in a tile (its height), and the narrowest tile.</summary>
    public const double MostStill = 88, LeastTileWidth = 72;

    /// <summary>The columns and the tiles' size for <paramref name="count"/> tiles in a room of
    /// <paramref name="width"/> x <paramref name="height"/>, <paramref name="gap"/> apart: of the column counts whose
    /// tiles are at least <see cref="LeastTileWidth"/> wide, the one that draws the still largest (its height, up to
    /// <see cref="MostStill"/>), the most columns of those that tie; the tiles as wide as the still needs (filling the
    /// width at most) and as tall as the still and the name.</summary>
    public static (int Columns, double Width, double Height) Fit(int count, double width, double height, double gap, double nameLine = NameLine)
    {
        count = Math.Max(1, count);
        var best = (Columns: 1, Width: Math.Max(LeastTileWidth, width), Height: Math.Max(NameLine + 8, height), Still: -1.0);
        var most = Math.Max(1, (int)Math.Floor((width + gap) / (LeastTileWidth + gap)));
        for (var columns = 1; columns <= most; columns++)
        {
            var rows = (count + columns - 1) / columns;
            var w = Math.Floor((width - ((columns - 1) * gap)) / columns);
            var h = Math.Floor((height - ((rows - 1) * gap)) / rows);
            var still = Math.Min(MostStill, Math.Min((w - (2 * StillMargin)) / StillAspect, h - nameLine - StillMargin));
            if (still >= best.Still - 0.5)
            {
                // (whole pixels: the tile as wide as its still asks, the width filled at most)
                var tileW = Math.Min(w, Math.Ceiling((still * StillAspect) + (2 * StillMargin) + 16));
                var tileH = Math.Min(h, Math.Ceiling(still + nameLine + StillMargin));
                best = (columns, tileW, tileH, Math.Max(still, best.Still));
            }
        }
        return (best.Columns, best.Width, best.Height);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = Math.Max(1, Columns);
        var size = new Size(TileWidth, TileHeight);
        foreach (var child in Children)
        {
            child.Measure(size);
        }
        var rows = (Children.Count + columns - 1) / columns;
        var used = Math.Min(columns, Children.Count);
        return new Size(used == 0 ? 0 : (used * TileWidth) + ((used - 1) * Gap), rows == 0 ? 0 : (rows * TileHeight) + ((rows - 1) * Gap));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Math.Max(1, Columns);
        for (var i = 0; i < Children.Count; i++)
        {
            var (row, column) = (i / columns, i % columns);
            Children[i].Arrange(new Rect(column * (TileWidth + Gap), row * (TileHeight + Gap), TileWidth, TileHeight));
        }
        return finalSize;
    }
}
