using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Launcher.Core;
using Online.Protocol;

namespace Launcher.App.Controls;

/// <summary>
/// A colour wheel for painting a machine: hue around the wheel and saturation out from its white centre
/// (<see cref="ColorWheel"/>), and beside it a bar for brightness, from the colour at the top to black. Click or drag
/// on either. <see cref="Color"/> is "#RRGGBB", both ways: picking writes it, and a colour typed elsewhere moves the
/// marks (a grey keeps the hue the wheel had, so turning the brightness back up finds it again).
/// </summary>
public sealed class ColorWheelPicker : Control
{
    public static readonly StyledProperty<string?> ColorProperty =
        AvaloniaProperty.Register<ColorWheelPicker, string?>(nameof(Color), defaultBindingMode: BindingMode.TwoWay);

    private const double Bar = 22;
    private const double Gap = 16;

    private double _hue;
    private double _saturation = 1;
    private double _value = 1;
    private WriteableBitmap? _wheel;
    private bool _onBar;
    private bool _dragging;

    static ColorWheelPicker()
    {
        AffectsRender<ColorWheelPicker>(ColorProperty);
    }

    public ColorWheelPicker() => Cursor = new Cursor(StandardCursorType.Hand);

    public string? Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var d = double.IsInfinity(availableSize.Height) ? 180 : Math.Min(availableSize.Height, 220);
        return new Size(d + Gap + Bar, d);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColorProperty && !_dragging && Rgb.Parse(Color) is { } c
            && c != ColorWheel.FromHsv(_hue, _saturation, _value))
        {
            var (h, s, v) = ColorWheel.ToHsv(c);
            // a grey or black has no hue of its own: keep the wheel's
            (_hue, _saturation, _value) = (s > 0 && v > 0 ? h : _hue, v > 0 ? s : _saturation, v);
        }
    }

    private double Diameter => Math.Max(10, Math.Min(Bounds.Height, Bounds.Width - Gap - Bar));

    private Rect BarRect => new(Diameter + Gap, 0, Bar, Diameter);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var at = e.GetPosition(this);
        _onBar = at.X > Diameter + (Gap / 2);
        _dragging = true;
        e.Pointer.Capture(this);
        Pick(at);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
        {
            Pick(e.GetPosition(this));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
    }

    private void Pick(Point at)
    {
        var d = Diameter;
        if (_onBar)
        {
            _value = Math.Clamp(1 - (at.Y / d), 0, 1);
        }
        else
        {
            (_hue, _saturation) = ColorWheel.At((at.X - (d / 2)) / (d / 2), (at.Y - (d / 2)) / (d / 2));
            if (_value < 0.05)
            {
                _value = 1;   // picking a hue on a black machine: show the hue
            }
        }
        SetCurrentValue(ColorProperty, Rgb.Hex(ColorWheel.FromHsv(_hue, _saturation, _value)));
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var d = Diameter;
        var scaling = (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) * ScaledRoot.GetFactor(this);
        var pixels = Math.Max(8, (int)Math.Ceiling(d * scaling));
        if (_wheel is null || _wheel.PixelSize.Width != pixels)
        {
            _wheel?.Dispose();
            _wheel = Wheel(pixels);
        }
        context.DrawImage(_wheel, new Rect(0, 0, d, d));

        // the mark on the wheel: a ring around the chosen hue and saturation
        var (x, y) = ColorWheel.Where(_hue, _saturation);
        var mark = new Point((d / 2) + (x * d / 2), (d / 2) + (y * d / 2));
        var chosen = ColorWheel.FromHsv(_hue, _saturation, _value);
        var fill = new SolidColorBrush(Avalonia.Media.Color.FromRgb(chosen.R, chosen.G, chosen.B));
        context.DrawEllipse(fill, new Pen(Brushes.White, 2.5), mark, 8, 8);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(120, 0, 0, 0)), 1), mark, 9.5, 9.5);

        // the brightness bar: the colour at full brightness down to black, a notch at the chosen brightness
        var bar = BarRect;
        var top = ColorWheel.FromHsv(_hue, _saturation, 1);
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Avalonia.Media.Color.FromRgb(top.R, top.G, top.B), 0),
                new GradientStop(Colors.Black, 1),
            },
        };
        context.DrawRectangle(gradient, new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(60, 255, 255, 255)), 1), bar, 8, 8);
        var notch = bar.Y + ((1 - _value) * bar.Height);
        context.DrawRectangle(fill, new Pen(Brushes.White, 2.5), new Rect(bar.X - 3, notch - 5, bar.Width + 6, 10), 4, 4);
    }

    /// <summary>The wheel at full brightness, its rim smoothed.</summary>
    private static WriteableBitmap Wheel(int size)
    {
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var pixels = new byte[size * size * 4];
        var r = size / 2.0;
        for (var py = 0; py < size; py++)
        {
            for (var px = 0; px < size; px++)
            {
                var (x, y) = ((px + 0.5 - r) / r, (py + 0.5 - r) / r);
                var distance = Math.Sqrt((x * x) + (y * y));
                var alpha = Math.Clamp((1 - distance) * r, 0, 1);
                if (alpha <= 0)
                {
                    continue;
                }
                var (hue, saturation) = ColorWheel.At(x, y);
                var c = ColorWheel.FromHsv(hue, saturation, 1);
                var o = ((py * size) + px) * 4;
                pixels[o] = (byte)(c.B * alpha);
                pixels[o + 1] = (byte)(c.G * alpha);
                pixels[o + 2] = (byte)(c.R * alpha);
                pixels[o + 3] = (byte)(255 * alpha);
            }
        }
        using var frame = bitmap.Lock();
        for (var y = 0; y < size; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(pixels, y * size * 4, frame.Address + (y * frame.RowBytes), size * 4);
        }
        return bitmap;
    }
}
