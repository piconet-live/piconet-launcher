using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;

namespace Launcher.Tests;

/// <summary>
/// The launcher's colours are the site's (piconet-site, src/styles/global.css): white on black, the accent at full
/// strength with black on it. Each token is read from the theme as the running application loads it (Theme.axaml,
/// with its references resolved), so a value that drifts from the site's fails here; and every text colour keeps
/// 4.5:1 on every tone, as it does on the site.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class ThemeTokensTests
{
    public static AppBuilder BuildAvaloniaApp() => HeadlessApp.BuildAvaloniaApp();

    /// <summary>The theme's key, and the site's value in the same role.</summary>
    private static readonly (string Key, string Site)[] Tokens =
    [
        ("GroundColor", "#000000"),        // --ground: the window
        ("DeepColor", "#000000"),
        ("ColumnColor", "#0D0E0D"),        // --bar: the left column and the title bar
        ("WellColor", "#171917"),          // --surface: panels, cards, tiles
        ("SurfaceBrush", "#171917"),
        ("RaisedColor", "#232623"),        // --raised: inputs and plates at rest
        ("HoverColor", "#2F332F"),         // --hover: one step up
        ("RowLineBrush", "#2A2E2A"),       // --row-line
        ("TextColor", "#FFFFFF"),          // --text
        ("DimColor", "#BCC2BC"),           // --dim
        ("DimBrush", "#BCC2BC"),
        ("FaintColor", "#9BA29B"),         // --faint
        ("HeadBrush", "#9BA29B"),
        ("AccentColor", "#3DFF6A"),        // --accent
        ("AccentOverBrush", "#7AFF96"),    // --accent-over
        ("AccentDownBrush", "#2BE356"),    // --accent-down
        ("OnAccentColor", "#000000"),      // --on-accent
        ("GoldColor", "#FFD500"),          // --gold
        ("OnGoldBrush", "#000000"),
        ("WarnColor", "#FF9F1A"),          // --warn
        ("NoticeBrush", "#FF9F1A"),
        ("TierWarningBrush", "#FF9F1A"),
        ("DangerColor", "#FF4D4D"),        // --danger
        ("OfflineBrush", "#FF4D4D"),
        ("TierUrgentBrush", "#FF4D4D"),
        ("FocusColor", "#FFFFFF"),         // the pad's ring: the text colour
        ("FocusGapColor", "#000000"),      // over a gap of the ground
    ];

    private static readonly string[] Tones = ["GroundColor", "ColumnColor", "WellColor", "RaisedColor", "HoverColor"];
    private static readonly string[] Texts = ["TextColor", "DimColor", "FaintColor", "HeadBrush"];
    private static readonly string[] Accents = ["AccentColor", "AccentOverBrush", "AccentDownBrush"];

    /// <summary>Reads the theme's colours on an application's thread (one application, as one start has).</summary>
    private static async Task<Dictionary<string, Color?>> Read(IEnumerable<string> keys)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTokensTests));
        try
        {
            return await session.Dispatch(() => keys.Distinct().ToDictionary(k => k, k =>
                Application.Current!.TryFindResource(k, out var value) ? value switch
                {
                    Color c => c,
                    ISolidColorBrush b => b.Color,
                    _ => (Color?)null,
                } : null), TestContext.Current.CancellationToken);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task EveryTokenIsTheSitesValue()
    {
        var found = await Read(Tokens.Select(t => t.Key));
        var wrong = Tokens.Where(t => found[t.Key] != Color.Parse(t.Site))
            .Select(t => $"{t.Key} is {found[t.Key]?.ToString() ?? "missing"}, the site's is {t.Site}").ToList();
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public async Task EveryTextColourReadsOnEveryToneAndBlackOnTheAccent()
    {
        var found = await Read([.. Tones, .. Texts, .. Accents, "OnAccentColor", "GoldColor", "OnGoldBrush"]);
        var pairs = Texts.SelectMany(t => Tones.Select(g => (Text: t, On: g)))
            .Concat(Accents.Select(a => (Text: "OnAccentColor", On: a)))
            .Append((Text: "OnGoldBrush", On: "GoldColor"));
        var low = pairs.Select(p => (p.Text, p.On, Ratio: Contrast(found[p.Text]!.Value, found[p.On]!.Value)))
            .Where(p => p.Ratio < 4.5).Select(p => $"{p.Text} on {p.On}: {p.Ratio:0.00}:1").ToList();
        Assert.True(low.Count == 0, string.Join("\n", low));
    }

    /// <summary>A leaning display line's box holds its lean's reach (PREVIEW's W was cut off): at
    /// each size the right padding the theme gives it is at least, and within a pixel of, the reach of the line's top
    /// corner under its transform (NameTagText.Overhang), so a parent that clips never cuts the last letter.</summary>
    [Theory]
    [InlineData("d64")]
    [InlineData("d40")]
    [InlineData("d20")]
    public async Task ADisplayLinesBoxHoldsItsLean(string size)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(ThemeTokensTests));
        try
        {
            await session.Dispatch(() =>
            {
                var text = new TextBlock { Text = "PREVIEW W", Classes = { "display", size } };
                var window = new Window { Content = new StackPanel { Children = { text } }, Width = 900, Height = 300 };
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var line = new Size(text.Bounds.Width - text.Padding.Right, text.Bounds.Height);
                var reach = Launcher.App.Controls.NameTagText.Overhang(text.RenderTransform, text.RenderTransformOrigin, line);
                Assert.True(reach > 0, $"{size}: no lean");
                Assert.True(text.Padding.Right >= reach - 0.01 && text.Padding.Right <= reach + 1,
                    $"{size}: the box holds {text.Padding.Right} px of a {reach:0.0} px reach");
                window.Close();
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    /// <summary>WCAG 2's contrast ratio of two opaque colours.</summary>
    private static double Contrast(Color a, Color b)
    {
        static double Lum(Color c)
        {
            static double Ch(byte v) => v / 255.0 is var s && s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        var (x, y) = (Lum(a), Lum(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
}
