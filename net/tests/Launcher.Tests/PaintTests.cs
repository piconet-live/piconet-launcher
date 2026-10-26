using Launcher.Core;
using Online.Protocol;
using static Launcher.Tests.GarageFiles;

namespace Launcher.Tests;

/// <summary>The garage's colours on made-up texture files: the paint, its repainting, the stock swatches and the
/// colour wheel.</summary>
public sealed class PaintTests
{
    [Fact]
    public void ThePaintIsWhereTheFourStockColoursDiffer()
    {
        var paint = MachinePaint.Of(StockSet())!;
        // an 8x8 CMPR texture is four 4x4 blocks; the left two are paint, the right two (grey) are alike in all four
        Assert.Equal(2, paint.PaintUnits);
        Assert.Equal([0], paint.PaintedTextures);
        Assert.Equal(StockSet()[2], paint.Stock(2));
        Assert.Null(MachinePaint.Of([.. StockSet().Take(3)]));
    }

    [Fact]
    public void PaintingRecoloursOnlyThePaintAndKeepsItsShading()
    {
        var paint = MachinePaint.Of(StockSet())!;
        var green = new Rgb(40, 200, 60);
        var painted = Decode(paint.Painted(green));
        var stock = Decode(paint.Stock(0));
        // the grey right half is untouched
        Assert.Equal(Pixel(stock, 6, 1), Pixel(painted, 6, 1));
        Assert.Equal(Pixel(stock, 6, 6), Pixel(painted, 6, 6));
        // the paint is green now, and the darker band (top) stays darker than the lighter one (bottom)
        var (top, bottom) = (Pixel(painted, 1, 1), Pixel(painted, 1, 6));
        Assert.InRange(ColorWheel.ToHsv(top).Hue, 110, 140);
        Assert.InRange(ColorWheel.ToHsv(bottom).Hue, 110, 140);
        Assert.True(Lum(top) < Lum(bottom));
        // the decoded paint is what the preview puts on the model
        var (slot, picture) = Assert.Single(paint.PaintedPictures(paint.Painted(green)));
        Assert.Equal(0, slot);
        Assert.Equal(painted.Rgba, picture.Rgba);
    }

    [Fact]
    public void AStockSwatchIsTheColourMostOfThePaintHas()
    {
        // each stock colour: 70 texels of its body colour, 30 of a stripe colour, and texels alike in all four (grey)
        Rgb[] body = [new(200, 20, 20), new(20, 40, 200), new(30, 180, 40), new(220, 200, 30)];
        Rgb[] stripe = [new(250, 250, 250), new(250, 120, 0), new(0, 0, 0), new(160, 0, 200)];
        var sets = Enumerable.Range(0, 4).Select(k =>
        {
            var rgba = new byte[120 * 4];
            for (var i = 0; i < 120; i++)
            {
                var c = i < 70 ? body[k] : i < 100 ? stripe[k] : Grey;
                (rgba[i * 4], rgba[(i * 4) + 1], rgba[(i * 4) + 2], rgba[(i * 4) + 3]) = (c.R, c.G, c.B, 255);
            }
            return (IReadOnlyList<Picture>)[new Picture(120, 1, rgba)];
        }).ToList();
        var swatches = MachinePaint.StockSwatches(sets)!;
        for (var k = 0; k < 4; k++)
        {
            // the body's hue, lit as the paint shows (its strongest channel near 220)
            var hue = ColorWheel.ToHsv(body[k]).Hue;
            Assert.InRange(ColorWheel.ToHsv(swatches[k]).Hue, hue - 3, hue + 3);
            Assert.InRange(Math.Max(swatches[k].R, Math.Max(swatches[k].G, swatches[k].B)), 210, 225);
        }
        // four alike: no paint
        Assert.Null(MachinePaint.StockSwatches([sets[0], sets[0], sets[0], sets[0]]));
    }

    [Theory]
    [InlineData(255, 0, 0, 0, 1, 1)]
    [InlineData(0, 255, 0, 120, 1, 1)]
    [InlineData(0, 0, 128, 240, 1, 0.502)]
    [InlineData(255, 255, 255, 0, 0, 1)]
    [InlineData(232, 67, 46, 6.8, 0.802, 0.91)]
    public void HsvRoundTrips(byte r, byte g, byte b, double hue, double saturation, double value)
    {
        var (h, s, v) = ColorWheel.ToHsv(new Rgb(r, g, b));
        Assert.Equal(hue, h, 1);
        Assert.Equal(saturation, s, 2);
        Assert.Equal(value, v, 2);
        Assert.Equal(new Rgb(r, g, b), ColorWheel.FromHsv(h, s, v));
    }

    [Fact]
    public void TheWheelPutsHuesRoundAndSaturationOutwards()
    {
        // red to the right, yellow-green up (screen y runs down), the centre white
        var (x, y) = ColorWheel.Where(0, 1);
        Assert.Equal((1.0, 0.0), (Math.Round(x, 6), Math.Round(y, 6)));
        (x, y) = ColorWheel.Where(90, 0.5);
        Assert.Equal((0.0, -0.5), (Math.Round(x, 6), Math.Round(y, 6)));
        foreach (var (hue, sat) in new[] { (10.0, 0.3), (200.0, 0.9), (359.0, 1.0) })
        {
            var (h, s) = ColorWheel.At(ColorWheel.Where(hue, sat).X, ColorWheel.Where(hue, sat).Y);
            Assert.Equal(hue, h, 6);
            Assert.Equal(sat, s, 6);
        }
        Assert.Equal(1, ColorWheel.At(3, 4).Saturation);   // beyond the rim: the rim
        Assert.Equal(0, ColorWheel.At(0, 0).Saturation);
    }

    [Theory]
    [InlineData("#E8432E", 232, 67, 46)]
    [InlineData("e8432e", 232, 67, 46)]
    [InlineData("  #fff ", 255, 255, 255)]
    public void TypedColoursAreRead(string text, byte r, byte g, byte b) => Assert.Equal(new Rgb(r, g, b), Rgb.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("# 12345")]   // a space after the mark is no digit's place
    [InlineData("#12 345")]
    [InlineData(null)]
    public void OtherTextIsNoColour(string? text) => Assert.Null(Rgb.Parse(text));

    /// <summary>Four stock files of one 8x8 CMPR texture: the left half in each file's colour (darker in the top
    /// rows), the right half grey in all four.</summary>
    private static List<byte[]> StockSet()
    {
        Rgb[] paint = [new(200, 30, 30), new(30, 60, 200), new(40, 180, 50), new(220, 190, 30)];
        return [.. paint.Select(c => Tpl(Cmpr(8, 8, (x, y) =>
            x >= 4 ? Grey : y < 4 ? new Rgb((byte)(c.R / 2), (byte)(c.G / 2), (byte)(c.B / 2)) : c)))];
    }

    private static double Lum(Rgb c) => (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
}
