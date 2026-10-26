using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App.Controls;
using Launcher.Core;

namespace Launcher.App.Views;

/// <summary>The Garage page. The view's own part: how tall PREVIEW's stage is and how many columns and how large
/// SELECT's tiles are for the window, so that the page fits without scrolling down to the window's least height (the
/// whole roster at once) and nothing is drawn over anything else at any size; and the roster's keys (the arrows from
/// tile to tile, a letter to the next machine of that letter, Escape back to SELECT).</summary>
/// <remarks>The heights are set while the view is measured, before its parts are, so that every part is laid out
/// with them in the same pass. Set after the layout, when the view or the page has changed size, they would leave the
/// 3D view and the colour column (a window maximised on a wide, short screen at 130%) with heights the rows under
/// them have not been laid out for: the cells drawn over both.</remarks>
[SuppressMessage("Maintainability", "CA1506:AvoidExcessiveClassCoupling",
    Justification = "Pre-existing when the code metrics were turned on: to be broken up when the class is next worked on")]
public sealed partial class GarageView : UserControl
{
    /// <summary>The roster's columns, and its tiles' size (<see cref="RosterTiles.Fit"/>).</summary>
    public static readonly StyledProperty<int> RosterColumnsProperty =
        AvaloniaProperty.Register<GarageView, int>(nameof(RosterColumns), 8);

    public static readonly StyledProperty<double> TileWidthProperty =
        AvaloniaProperty.Register<GarageView, double>(nameof(TileWidth), 112);

    public static readonly StyledProperty<double> TileHeightProperty =
        AvaloniaProperty.Register<GarageView, double>(nameof(TileHeight), 88);

    /// <summary>The page's own title stands over it. Off by default since direction D: the window's head names the
    /// place (GARAGE), and the lobby has its line there.</summary>
    public static readonly StyledProperty<bool> TitledProperty = AvaloniaProperty.Register<GarageView, bool>(nameof(Titled), false);

    // What the view holds besides the roster or the stage: the step line (32 and 8 under it) and the heading row (32
    // and 8 under it).
    private const double Rest = 32 + 8 + 32 + 8;

    /// <summary>The roster's tiles apart.</summary>
    private const double TileGap = 8;

    // What stands on the page besides this view before it has been laid out once, and its place is known: the page's
    // padding (20 above and under) on the Garage tab.
    private const double PageRest = 40;

    /// <summary>The panel's room around the column: its padding (the panel is as tall as the stage).</summary>
    private const double AroundColumn = 16 + 16;

    /// <summary>What the column holds above what its tab shows: the tabs' row.</summary>
    private const double AboveColumn = 32 + 8;

    /// <summary>What the column holds under what its tab shows: the setting's row, the actions' row and the
    /// primary.</summary>
    private const double UnderColumn = 8 + 32 + 8 + 32 + 8 + 48;

    /// <summary>What the colour holds besides its wheel: the tabs, the swatches with their names, the gap, the
    /// actions.</summary>
    private const double BesideWheel = AboveColumn + 55 + 12 + UnderColumn;

    /// <summary>The wheel at its least: as tall as what stands beside it (a custom machine's three parts over the box).</summary>
    private const double LeastWheel = 118;

    /// <summary>What the column needs at its least, and the stage then as tall as the panel round it.</summary>
    private const double LeastColumn = BesideWheel + LeastWheel;
    private const double LeastStage = LeastColumn + AroundColumn;

    /// <summary>A part's row in its list (Button.stat at 26).</summary>
    private const double PartRow = 26;

    private ScrollViewer? _page;
    private ViewModels.GarageViewModel? _garage;
    private ViewModels.CustomBuilderViewModel? _builder;

    /// <summary>The room the heights were last fitted to; NaN: none yet.</summary>
    private double _fitted = double.NaN;

    /// <summary>The view has been laid out on its page since it was put there: its place on it is known.</summary>
    private bool _arranged;

    public GarageView()
    {
        var made = LoadTrace.Now;
        AvaloniaXamlLoader.Load(this);
        if (LoadTrace.On)
        {
            LoadTrace.Note(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"garage page made in {LoadTrace.Now - made:F0} ms"));
            AttachedToVisualTree += (_, _) => TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ => LoadTrace.Note("garage page's first frame"));
        }
        // the labels on a custom machine follow its parts as it turns (the view tells where they stand)
        var preview = this.FindControl<MachinePreview>("Preview")!;
        var callouts = this.FindControl<PartCallouts>("Callouts")!;
        preview.AnchorsMoved += points =>
            callouts.Anchors = [.. points.Select(p => preview.TranslatePoint(p, callouts) ?? p)];
        DataContextChanged += (_, _) => Watch();
        // (what stands above and under the view on the page can change without the view's own size changing: the
        // lobby's line and notice; its room is looked at again after every layout, and only a change measures anew)
        // (fitted here, after the layout and outside it, the heights set start a layout of their own: on Windows a
        // height set while the view was being measured could be left unapplied by a part of it, the colour wheel
        // drawn at its old size over the cells' row, until the next change of size)
        // the strip's keys: left and right on a card step to the next machine (Enter, in a lobby, is the lobby's)
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Bubble);
        // a press outside the Sort & filter panel closes it
        this.FindControl<Border>("SortFilterDismiss")!.PointerPressed += (_, e) =>
        {
            _garage?.CloseSortFilterCommand.Execute(null);
            e.Handled = true;
        };
        LayoutUpdated += (_, _) =>
        {
            // the labels on a custom machine stand under the name's band (its name, letters, pilot and heart)
            var band = this.FindControl<Control>("NameBand")!;
            if (band.IsEffectivelyVisible && band.TranslatePoint(new Point(0, band.Bounds.Height), callouts) is { } foot
                && Math.Abs(callouts.Clear - foot.Y) > 0.5)
            {
                callouts.Clear = foot.Y;
            }
            if (Room() is { } room && (Math.Abs(room - _fitted) > 0.5 || (RosterWidth() is { } wide && Math.Abs(wide - _fittedWidth) > 0.5)))
            {
                Fit(room);
            }
            // (a row that took a new height after its stack was arranged keeps its old place and height until the stack
            // is arranged again: measured after one resize, the 3D view's row 268 px high for 381 wanted,
            // the 3D view drawn over the cells' row. The stack is arranged again then)
            if (this.FindControl<StackPanel>("Body") is { } stack && stack.Children.Any(c => c.IsVisible
                && Math.Abs(c.Bounds.Height + c.Margin.Top + c.Margin.Bottom - c.DesiredSize.Height) > 0.5))
            {
                stack.InvalidateArrange();
            }
        };
    }

    /// <summary>The page's model, and its builder's: the part list scrolled to the part picked, the labels' names.</summary>
    private void Watch()
    {
        if (_garage is not null)
        {
            _garage.PropertyChanged -= OnGarageChanged;
        }
        if (_builder is not null)
        {
            _builder.PropertyChanged -= OnBuilderChanged;
        }
        _garage = DataContext as ViewModels.GarageViewModel;
        _builder = _garage?.Builder;
        if (_garage is not null)
        {
            _garage.PropertyChanged += OnGarageChanged;
        }
        if (_builder is not null)
        {
            _builder.PropertyChanged += OnBuilderChanged;
        }
        ShowNames();
        InvalidateMeasure();
    }

    private void OnGarageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.GarageViewModel.ShownParts))
        {
            Dispatcher.UIThread.Post(ShowPicked, DispatcherPriority.Background);
        }
        else if (e.PropertyName == nameof(ViewModels.GarageViewModel.Phase))
        {
            Dispatcher.UIThread.Post(FocusPhase, DispatcherPriority.Loaded);
        }
    }

    /// <summary>The keyboard where the phase begins, when it was in the view (or nowhere): SELECT on the tile of the
    /// machine raced; PREVIEW on the stage, where Enter is the primary (USE THIS MACHINE; in a lobby, its READY) and
    /// Escape goes back.</summary>
    private void FocusPhase()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is not null && focused.IsEffectivelyVisible && !this.IsVisualAncestorOf(focused))
        {
            return;   // (the keyboard is somewhere else: a box being typed in, the lobby's line)
        }
        if (_garage is { Previewing: true })
        {
            this.FindControl<Control>("Well")!.Focus(Avalonia.Input.NavigationMethod.Directional);
        }
        else
        {
            CurrentCard()?.Focus(Avalonia.Input.NavigationMethod.Directional);
        }
    }

    private void OnBuilderChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "" or null or nameof(ViewModels.CustomBuilderViewModel.BodyName) or nameof(ViewModels.CustomBuilderViewModel.Build))
        {
            ShowNames();
            Dispatcher.UIThread.Post(ShowPicked, DispatcherPriority.Background);
        }
    }

    private void ShowNames() => this.FindControl<PartCallouts>("Callouts")!.Names =
        _builder is { } b ? [b.BodyName, b.CockpitName, b.BoosterName] : null;

    /// <summary>The part list scrolled so that its picked part shows (a saved machine opened shows its parts).</summary>
    private void ShowPicked()
    {
        if (_garage?.ShownParts is not { } parts)
        {
            return;
        }
        var list = this.FindControl<ScrollViewer>("PartList")!;
        var at = parts.Choices.ToList().FindIndex(p => p.Selected) * PartRow;
        if (at >= 0 && (at < list.Offset.Y || at + PartRow > list.Offset.Y + list.Viewport.Height))
        {
            list.Offset = new Vector(0, Math.Max(0, at - ((list.Viewport.Height - PartRow) / 2)));
        }
    }

    public int RosterColumns
    {
        get => GetValue(RosterColumnsProperty);
        set => SetValue(RosterColumnsProperty, value);
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

    public bool Titled
    {
        get => GetValue(TitledProperty);
        set => SetValue(TitledProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _page = this.FindAncestorOfType<ScrollViewer>();
        if (_page is not null)
        {
            _page.SizeChanged += OnPageSized;
        }
        (_fitted, _fittedWidth, _arranged) = (double.NaN, double.NaN, false);
        InvalidateMeasure();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_page is not null)
        {
            _page.SizeChanged -= OnPageSized;
        }
        _page = null;
        base.OnDetachedFromVisualTree(e);
    }

    // (a page that is only taller or lower measures its content again with the same width and no height, which the
    // toolkit may skip: the view is measured again itself; and once more after the layout the new size brought has
    // settled. A window resized once from outside (a drag, a maximise, a move by another program) was left with the
    // heights of its old size until it was resized again: the 3D view, the colour wheel and its box cut off)
    private void OnPageSized(object? sender, SizeChangedEventArgs e)
    {
        InvalidateMeasure();
        Dispatcher.UIThread.Post(Refit, DispatcherPriority.Background);
    }

    /// <summary>The heights fitted again from the room as it stands, outside a layout, and every part they size and
    /// everything up to the page measured anew, so the page arranges it by them.</summary>
    private void Refit()
    {
        if (Room() is not { } room)
        {
            return;
        }
        Fit(room);
        foreach (var name in (string[])["Stage", "Side", "Wheel", "BuildWheel", "Roster"])
        {
            this.FindControl<Control>(name)?.InvalidateMeasure();
        }
        for (Visual? at = this; at is not null; at = at.GetVisualParent())
        {
            (at as Layoutable)?.InvalidateMeasure();
            if (ReferenceEquals(at, _page))
            {
                break;
            }
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _arranged = true;
        return base.ArrangeOverride(finalSize);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _given = double.IsFinite(availableSize.Width) ? availableSize.Width : double.NaN;
        Fit(double.IsFinite(availableSize.Height) ? availableSize.Height : Room());
        return base.MeasureOverride(availableSize);
    }

    /// <summary>A tile's name's room is its width less this (its margins, and a pixel of the text's own).</summary>
    private const double NameInset = 8 + 8 + 2;

    /// <summary>The widest name a tile may hold, at a size of the tiles' name face (every machine's, the custom
    /// machines' saved, New machine).</summary>
    private double WidestName(double size)
    {
        var face = new Avalonia.Media.Typeface(this.TryFindResource("BodyFont", out var f) && f is Avalonia.Media.FontFamily family ? family
            : new Avalonia.Media.FontFamily("Saira Semi Condensed"), Avalonia.Media.FontStyle.Normal, Avalonia.Media.FontWeight.SemiBold);
        IEnumerable<string> names = [.. Machines.Names, .. _garage?.Builder.Saved.Select(c => c.Name) ?? [], "New machine"];
        return names.Max(n => new Avalonia.Media.FormattedText(n, System.Globalization.CultureInfo.InvariantCulture,
            Avalonia.Media.FlowDirection.LeftToRight, face, size, null).WidthIncludingTrailingWhitespace);
    }

    // the width the roster was last fitted to (NaN: none)
    private double _fittedWidth = double.NaN;

    // the width the view was last given to measure in (NaN: none)
    private double _given = double.NaN;

    /// <summary>The width the roster has: the view's, as given to it to measure, else as laid out; null before either.</summary>
    private double? RosterWidth() => double.IsFinite(_given) && _given > 0 ? _given : Bounds.Width > 0 ? Bounds.Width : null;

    /// <summary>The height the page has for this view: what it shows less what stands above and under the view on it
    /// (the page's padding; in the lobby its line, the course drawn and a notice), as they were last laid out; before
    /// that, the page's padding only. Null without a page.</summary>
    private double? Room()
    {
        if (_page is not { Bounds.Height: > 0 } page)
        {
            return null;
        }
        var shown = page.Viewport.Height > 0 ? page.Viewport.Height : page.Bounds.Height;
        if (page.Content is Control content && _arranged && this.TranslatePoint(default, content) is { } at)
        {
            // (what is under the view: the content as it asks to be, less the view and what is above it)
            var under = Math.Max(0, content.DesiredSize.Height - at.Y - DesiredSize.Height);
            return shown - at.Y - under;
        }
        return shown - PageRest;
    }

    /// <summary>SELECT's roster and PREVIEW's stage take all the room under the two rows: the roster's tiles as large
    /// as the room lets the whole of the largest shelf be (<see cref="ViewModels.GarageViewModel.RosterRoom"/>, so a
    /// shelf of a few keeps the size), the stage down to the panel's least (its colour wheel 118 px). The sizes are the
    /// same on every shelf: no region moves from one to another.</summary>
    private void Fit(double? given)
    {
        if (given is not { } height)
        {
            return;
        }
        _fitted = height;
        var room = height - Rest;
        if (RosterWidth() is { } width)
        {
            _fittedWidth = width;
            // a name on one line where the longest fits the tile, else two lines (the tile a line taller), else, where
            // two would leave too small a still, one line a size smaller (else "Green Pant…")
            var rosterRoom = Math.Max(0, room);
            var count = ViewModels.GarageViewModel.RosterRoom;
            var fit = RosterTiles.Fit(count, width, rosterRoom, TileGap);
            var (two, small) = (false, false);
            if (WidestName(13) > fit.Width - NameInset)
            {
                var tall = RosterTiles.Fit(count, width, rosterRoom, TileGap, RosterTiles.TwoLines);
                if (tall.Height - RosterTiles.TwoLines - RosterTiles.StillMargin >= RosterTiles.LeastStill)
                {
                    (fit, two) = (tall, true);
                }
                else
                {
                    small = true;
                }
            }
            Classes.Set("twolines", two);
            Classes.Set("smallnames", small);
            (RosterColumns, TileWidth, TileHeight) = fit;
        }
        var stage = Math.Max(LeastStage, room);
        // (SELECT's place as tall as the room, whatever the shelf holds: nothing under it moves)
        this.FindControl<Panel>("RosterPlace")!.Height = Math.Max(0, room);
        this.FindControl<Panel>("Stage")!.Height = stage;
        // the column inside the floating panel: its tabs at the top, its actions at the foot, what the tab shows
        // between (a list scrolls in it)
        var column = stage - AroundColumn;
        this.FindControl<Grid>("Side")!.Height = column;
        var wheel = Math.Clamp(column - BesideWheel, LeastWheel, 180);
        this.FindControl<ColorWheelPicker>("Wheel")!.Height = wheel;
        this.FindControl<ColorWheelPicker>("BuildWheel")!.Height = wheel;
    }
}
