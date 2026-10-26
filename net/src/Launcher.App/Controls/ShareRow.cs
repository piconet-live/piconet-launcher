using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;

namespace Launcher.App.Controls;

/// <summary>
/// Parts side by side that share the row's width by their <see cref="ShareProperty"/> (the draw's strip: each course
/// as wide as its chance), <see cref="Gap"/> apart, the first beginning at the left edge and the last ending at the
/// right one, every edge on a whole pixel. A part with no share has no width. In an items control the item (the
/// container's child) can carry the share.
/// </summary>
public sealed class ShareRow : Panel
{
    /// <summary>A part's share of the row: any number, counted against the sum of all the parts'.</summary>
    public static readonly AttachedProperty<double> ShareProperty =
        AvaloniaProperty.RegisterAttached<ShareRow, Control, double>("Share");

    public static readonly StyledProperty<double> GapProperty = AvaloniaProperty.Register<ShareRow, double>(nameof(Gap), 2);

    static ShareRow()
    {
        AffectsMeasure<ShareRow>(GapProperty);
        // the row is the part's visual parent, or its container's (an items control's item sets its own share)
        ShareProperty.Changed.AddClassHandler<Control>((part, _) =>
        {
            var up = Avalonia.VisualTree.VisualExtensions.GetVisualParent(part);
            ((up as ShareRow) ?? (up is ContentPresenter ? Avalonia.VisualTree.VisualExtensions.GetVisualParent(up) as ShareRow : null))
                ?.InvalidateMeasure();
        });
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public static double GetShare(Control part) => part.GetValue(ShareProperty);

    /// <summary>A part's share: its own, or, for an items control's container, its item's.</summary>
    private static double ShareOf(Control part) =>
        part.IsSet(ShareProperty) || part is not ContentPresenter { Child: { } item } ? GetShare(part) : GetShare(item);

    public static void SetShare(Control part, double share) => part.SetValue(ShareProperty, share);

    /// <summary>Where each part begins and ends in a row of the given width.</summary>
    private List<(double Left, double Right)> Edges(double width)
    {
        var shares = Children.Select(c => ShareOf(c) is var s && double.IsFinite(s) && s > 0 ? s : 0).ToList();
        var all = shares.Sum();
        // the gaps between parts that have width come out of the row first; the rest is shared
        var gaps = Gap * Math.Max(0, shares.Count(x => x > 0) - 1);
        var room = Math.Max(0, width - gaps);
        var edges = new List<(double, double)>(shares.Count);
        var before = 0.0;
        var shown = 0;
        foreach (var share in shares)
        {
            var gap = Gap * shown;
            var left = all > 0 ? Math.Round((room * before / all) + gap) : 0;
            before += share;
            var right = all > 0 ? Math.Round((room * before / all) + gap) : 0;
            edges.Add((left, share > 0 ? Math.Max(left, right) : left));
            shown += share > 0 ? 1 : 0;
        }
        return edges;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var edges = Edges(width);
        var height = 0.0;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(edges[i].Right - edges[i].Left, availableSize.Height));
            height = Math.Max(height, Children[i].DesiredSize.Height);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var edges = Edges(finalSize.Width);
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Arrange(new Rect(edges[i].Left, 0, edges[i].Right - edges[i].Left, finalSize.Height));
        }
        return finalSize;
    }
}
