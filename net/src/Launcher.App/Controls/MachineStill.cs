using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Launcher.App.Gpu;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.Controls;

/// <summary>A machine drawn still in its look (a stock machine in its colour, or a custom machine's parts in theirs),
/// for the garage's roster: a picture drawn on the graphics card
/// (<see cref="StillPainter"/>) again only when the look changes.</summary>
public sealed class MachineStill : Control
{
    public static readonly StyledProperty<int> MachineProperty =
        AvaloniaProperty.Register<MachineStill, int>(nameof(Machine), -1);

    public static readonly StyledProperty<int?> StockColorProperty =
        AvaloniaProperty.Register<MachineStill, int?>(nameof(StockColor));

    public static readonly StyledProperty<string?> PaintColorProperty =
        AvaloniaProperty.Register<MachineStill, string?>(nameof(PaintColor));

    /// <summary>A custom machine's build: drawn from its parts (<see cref="Machine"/> is not used then).</summary>
    public static readonly StyledProperty<CustomBuild?> CustomProperty =
        AvaloniaProperty.Register<MachineStill, CustomBuild?>(nameof(Custom));

    /// <summary>A custom machine's colours by part (body, cockpit, booster; null a part's own).</summary>
    public static readonly StyledProperty<IReadOnlyList<Online.Protocol.Rgb?>?> PartColoursProperty =
        AvaloniaProperty.Register<MachineStill, IReadOnlyList<Online.Protocol.Rgb?>?>(nameof(PartColours));

    // drawn at twice the card's size, for sharp edges on high-density screens; larger under an interface scale above 200%
    private static readonly PixelSize Base = new(176, 104);

    private PixelSize Size
    {
        get
        {
            var k = Math.Max(1, ScaledRoot.GetFactor(this) / 2);
            return new PixelSize((int)Math.Round(Base.Width * k), (int)Math.Round(Base.Height * k));
        }
    }

    private Bitmap? _picture;
    private PixelSize _painted = Base;
    private int _asked;

    public int Machine
    {
        get => GetValue(MachineProperty);
        set => SetValue(MachineProperty, value);
    }

    public int? StockColor
    {
        get => GetValue(StockColorProperty);
        set => SetValue(StockColorProperty, value);
    }

    public string? PaintColor
    {
        get => GetValue(PaintColorProperty);
        set => SetValue(PaintColorProperty, value);
    }

    public CustomBuild? Custom
    {
        get => GetValue(CustomProperty);
        set => SetValue(CustomProperty, value);
    }

    public IReadOnlyList<Online.Protocol.Rgb?>? PartColours
    {
        get => GetValue(PartColoursProperty);
        set => SetValue(PartColoursProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MachineProperty || change.Property == StockColorProperty || change.Property == PaintColorProperty
            || change.Property == CustomProperty || change.Property == PartColoursProperty
            || (change.Property == ScaledRoot.FactorProperty && Size != _painted))
        {
            // asked once its bindings have all been applied: a card made gets its machine, then its stock colour, then
            // its paint, and each look on the way would be a picture drawn for nothing
            if (!_asking)
            {
                _asking = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _asking = false;
                    _ = AskAsync();
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
        }
    }

    private bool _asking;

    private async Task AskAsync()
    {
        var asked = ++_asked;
        if (Machine < 0 && Custom is null)
        {
            return;
        }
        var size = Size;
        var picture = Custom is { } custom ? await StillPainter.PaintCustom(custom, PartColours, size)
            : await StillPainter.Paint(Machine, StockColor, StockColor is null ? Rgb.Parse(PaintColor) : null, size);
        if (asked == _asked)
        {
            (_picture, _painted) = (picture, size);
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_picture is null)
        {
            return;
        }
        var bounds = new Rect(Bounds.Size);
        var scale = Math.Min(bounds.Width / Base.Width, bounds.Height / Base.Height);
        var (w, h) = (Base.Width * scale, Base.Height * scale);
        context.DrawImage(_picture, new Rect((bounds.Width - w) / 2, (bounds.Height - h) / 2, w, h));
    }
}
