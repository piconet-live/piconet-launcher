using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Launcher.Core;
using SkiaSharp;

namespace Launcher.App.Controls;

/// <summary>
/// The Controller page's test: m-overlay's default skin (bkacjios/m-overlay, MIT: Assets/PadOverlay/LICENSE.txt) drawn
/// live, layer for layer and in its order, from what it is given (<see cref="Pad"/>: the game's own reading of the
/// port). It keeps the skin's shape, 2 to 1, and takes the width it is given.
/// </summary>
public sealed class PadOverlay : Control
{
    public static readonly StyledProperty<HelperPad?> PadProperty = AvaloniaProperty.Register<PadOverlay, HelperPad?>(nameof(Pad));

    static PadOverlay() => AffectsRender<PadOverlay>(PadProperty);

    public HelperPad? Pad
    {
        get => GetValue(PadProperty);
        set => SetValue(PadProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? PadSkin.Width : availableSize.Width;
        return new Size(width, width * PadSkin.Height / PadSkin.Width);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.Custom(new Drawing(new Rect(Bounds.Size), Pad is { Connected: true } pad ? pad : null));
    }

    /// <summary>The pictures, read once: Skia's images belong to no thread, and the drawing runs on the renderer's.</summary>
    private static readonly Lazy<Dictionary<string, SKImage>> Art = new(() =>
    {
        string[] names =
        [
            "joystick-gate", "joystick", "joystick-mask", "c-stick-gate", "c-stick", "c-stick-mask", "analog-outline", "d-pad-gate",
            "d-pad-pressed-up", "d-pad-pressed-down", "d-pad-pressed-left", "d-pad-pressed-right",
            .. PadSkin.Buttons.SelectMany(b => new[] { b.Art + "-outline", b.Art + "-pressed" }),
        ];
        return names.ToDictionary(name => name, name =>
        {
            using var stream = AssetLoader.Open(new Uri($"avares://{typeof(PadOverlay).Assembly.GetName().Name}/Assets/PadOverlay/{name}.png"));
            using var data = SKData.Create(stream);
            return SKImage.FromEncodedData(data);
        });
    });

    private static readonly SKSamplingOptions Smooth = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>Every pixel of the mask with anything in it counts whole, as the skin's stencil does.</summary>
    private static readonly byte[] AnyAlpha = [0, .. Enumerable.Repeat((byte)255, 255)];

    private static readonly byte[] SameColour = [.. Enumerable.Range(0, 256).Select(i => (byte)i)];

    private sealed class Drawing(Rect bounds, HelperPad? pad) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
            {
                return;
            }
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var save = canvas.Save();
            var scale = (float)(bounds.Width / PadSkin.Width);
            canvas.Scale(scale);
            Skin(canvas);
            canvas.RestoreToCount(save);
        }

        private bool On(string button) => pad is not null && pad.Buttons.Contains(button);

        private void Skin(SKCanvas canvas)
        {
            var (stickX, stickY) = PadSkin.ClampedStick(pad?.StickX ?? 128, pad?.StickY ?? 128);
            var (x, y) = PadSkin.Translate(stickX, stickY, 72);
            Stick(canvas, PadSkin.Stick, x, y, "joystick", SKColors.White);
            var (cStickX, cStickY) = PadSkin.ClampedCStick(pad?.CStickX ?? 128, pad?.CStickY ?? 128);
            (x, y) = PadSkin.Translate(cStickX, cStickY, 59);
            Stick(canvas, PadSkin.CStick, x, y, "c-stick", PadSkin.CStickColour);

            Triggers(canvas);

            Picture(canvas, "d-pad-gate", 108, 144, SKColors.White);
            foreach (var (button, art) in new[] { ("Left", "left"), ("Right", "right"), ("Up", "up"), ("Down", "down") })
            {
                if (On(button))
                {
                    Picture(canvas, "d-pad-pressed-" + art, 108, 144, SKColors.White);
                }
            }
            foreach (var b in PadSkin.Buttons)
            {
                Picture(canvas, b.Art + (On(b.Button) ? "-pressed" : "-outline"), b.X, b.Y, b.Colour);
            }
        }

        /// <summary>The gate with the stick's shape cut out of it, then the stick, leaning.</summary>
        private static void Stick(SKCanvas canvas, PadSkin.StickPlace place, float x, float y, string art, SKColor colour)
        {
            var onto = PadSkin.PictureOnto(PadSkin.Quad(place, x, y), PadSkin.Cell, PadSkin.Cell);
            canvas.SaveLayer(new SKRect(place.GateX - 32, place.GateY - 32, place.GateX + PadSkin.Cell + 32, place.GateY + PadSkin.Cell + 32), null);
            Picture(canvas, art + "-gate", place.GateX, place.GateY, colour);
            using (var cut = new SKPaint { BlendMode = SKBlendMode.DstOut, ColorFilter = SKColorFilter.CreateTable(AnyAlpha, SameColour, SameColour, SameColour) })
            {
                Leaning(canvas, art + "-mask", onto, cut);
            }
            canvas.Restore();
            using var paint = Tinted(colour);
            Leaning(canvas, art, onto, paint);
        }

        private static void Leaning(SKCanvas canvas, string art, SKMatrix onto, SKPaint paint)
        {
            canvas.Save();
            canvas.Concat(in onto);
            canvas.DrawImage(Art.Value[art], new SKRect(0, 0, PadSkin.Cell, PadSkin.Cell), Smooth, paint);
            canvas.Restore();
        }

        private void Triggers(SKCanvas canvas)
        {
            using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var line = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4 };
            var l = On("L") ? 1 : PadSkin.Trigger(PadSkin.ClampedTrigger(pad?.L ?? 0));
            var r = On("R") ? 1 : PadSkin.Trigger(PadSkin.ClampedTrigger(pad?.R ?? 0));

            // L: the outline, the mark of the press, the bar filling from the left, the press's end at the right
            Picture(canvas, "analog-outline", 24 + 6, 14, SKColors.White, 116, 24);
            canvas.DrawLine(24 + 14 + 88, 20, 24 + 14 + 88, 20 + 12, line);
            Inside(canvas, 24 + 14, () =>
            {
                if (On("L"))
                {
                    canvas.DrawRect(24 + 14 + 88, 20, 12, 12, white);
                }
                canvas.DrawRect(24 + 14, 20, 88 * l, 12, white);
            });

            // R: the same, mirrored
            Picture(canvas, "analog-outline", 48 + 128 + 6, 14, SKColors.White, 116, 24);
            canvas.DrawLine(48 + 128 + 14 + 12, 20, 48 + 128 + 14 + 12, 20 + 12, line);
            Inside(canvas, 48 + 128 + 14, () =>
            {
                if (On("R"))
                {
                    canvas.DrawRect(48 + 128 + 14, 20, 12, 12, white);
                }
                canvas.DrawRect(48 + 128 + 14 + 12 + 88 * (1 - r), 20, 88 * r, 12, white);
            });
        }

        /// <summary>Drawing cut to a trigger's rounded bar.</summary>
        private static void Inside(SKCanvas canvas, float x, Action draw)
        {
            canvas.Save();
            using var bar = new SKRoundRect(new SKRect(x, 20, x + 100, 32), 6);
            canvas.ClipRoundRect(bar, antialias: true);
            draw();
            canvas.Restore();
        }

        private static void Picture(SKCanvas canvas, string art, float x, float y, SKColor colour, float width = PadSkin.Cell, float height = PadSkin.Cell)
        {
            using var paint = Tinted(colour);
            canvas.DrawImage(Art.Value[art], new SKRect(x, y, x + width, y + height), Smooth, paint);
        }

        private static SKPaint Tinted(SKColor colour) => new()
        {
            IsAntialias = true,
            ColorFilter = SKColorFilter.CreateBlendMode(colour, SKBlendMode.Modulate),
        };
    }
}
