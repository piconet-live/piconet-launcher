using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Controls;
using Launcher.App.Controls.NextUi;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.ViewModels.NextUi;
using Launcher.App.Views;
using Launcher.App.Views.NextUi;
using Launcher.Core;
using static Launcher.Tests.Headless;
using static Launcher.Tests.NextHeadless;

namespace Launcher.Tests;

/// <summary>
/// The redesigned interface's finish ("Experimental UI" on): Developer mode (its DEV chip and Diagnostics, its two
/// lines in the hint bar, the keys at the rows, Turn off); the theme's text holding 4.5:1 on every ground it stands
/// on; the main actions reachable at 150 %; and Reduced motion stilling what runs by itself. With <c>NEXT_SHOTS</c>
/// naming a folder, the pages there as PNGs.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class NextPolishTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    private static readonly string[] Texts = ["NextTextColor", "NextDimColor", "NextAccentColor", "NextGoldColor", "NextDangerColor", "NextDevColor"];

    private static readonly string[] Areas = ["Garage", "Settings"];

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AppState State(string more = "")
    {
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"),
            """{"noServer": true, "experimentalUi": true, "recordReplays": false, "soundMuted": true, "racer": {"name": "ANA", "machine": 6, "color": {"r": 232, "g": 67, "b": 46}}""" + more + "}");
        return AppState.Load(data, standIn: true);
    }

    [Fact]
    public async Task DeveloperModeShowsItsChipItsDiagnosticsItsLinesAndTheKeys()
    {
        var state = State(""", "developerMode": true""");
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            Assert.True(main.DeveloperMode);
            Assert.True(Named<Button>(shell, "DevChip").IsEffectivelyVisible);
            Assert.True(Named<StackPanel>(shell, "DevLines").IsEffectivelyVisible);
            Assert.Contains("play", main.Shell.DevPlace, StringComparison.Ordinal);
            Assert.StartsWith("input keyboard and mouse", main.Shell.DevInput, StringComparison.Ordinal);

            // the keys at the rows: Settings' graphics
            Click(window, Named<Button>(shell, "Settings"));
            var page = shell.GetVisualDescendants().OfType<SettingsPageView>().Single();
            main.Shell.SettingsPage.ShowCategory("Graphics");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Classes.Contains("devmono") && t.Text == "launch · auroraSize");
            Assert.Equal("rule · course-select", DevKeys.For("Course"));

            // Diagnostics: its groups, its text, and Turn off
            Click(window, Named<Button>(shell, "DevChip"));
            var diagnostics = Assert.IsType<DiagnosticsSheetViewModel>(main.Shell.Sheet);
            Assert.Equal(["Build", "Files", "Connection", "Input", "Interface"], diagnostics.Groups.Select(g => g.Heading));
            Assert.Contains("Version", diagnostics.Text(), StringComparison.Ordinal);
            Assert.Contains(state.DataFolder, diagnostics.Text(), StringComparison.Ordinal);
            Shot(window, state, "next-diagnostics");
            Click(window, Said(shell.GetVisualDescendants().OfType<DiagnosticsSheetView>().Single(), "Turn off developer mode"));
            Pump(() => !main.DeveloperMode, "developer mode stayed on");
            Assert.False(state.Settings.DeveloperMode);
            Assert.Null(main.Shell.Sheet);
            Assert.False(Named<Button>(shell, "DevChip").IsEffectivelyVisible);
            Assert.False(Named<StackPanel>(shell, "DevLines").IsEffectivelyVisible);
        });
    }

    [Fact]
    public async Task EveryTextHoldsFourAndAHalfToOneOnItsGround()
    {
        var state = State();
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            Color Of(string key) => shell.TryFindResource(key, out var value) && value is Color color ? color
                : throw new InvalidOperationException($"no colour {key}");
            var ground = Of("NextGroundColor");
            Color Over(Color top) => Blend(top, ground);
            var grounds = new[] { ground, Over(Of("NextPlateColor")), Over(Of("NextPlateHiColor")), Of("NextSolidColor"), Of("NextRaisedColor"), Of("NextHoverColor") };
            foreach (var text in Texts)
            {
                foreach (var on in grounds)
                {
                    var ratio = Contrast(Of(text), on);
                    Assert.True(ratio >= 4.5, $"{text} on {on}: {ratio:0.00}");
                }
            }
            Assert.True(Contrast(Of("NextOnAccentColor"), Of("NextAccentColor")) >= 4.5);
            Assert.True(Contrast(Of("NextOnAccentColor"), Of("NextAccentHiColor")) >= 4.5);
        });
    }

    [Fact]
    public async Task AtOneHundredAndFiftyPercentTheMainActionsCanBeReached()
    {
        var state = State(""", "interfaceScale": 150""");
        await InWindow(state, (window, main) =>
        {
            var shell = Shell(window);
            foreach (var area in Areas)
            {
                var button = area == "Settings" ? Named<Button>(shell, "Settings")
                    : Named<StackPanel>(shell, "Areas").GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == area);
                Click(window, button);
                Dispatcher.UIThread.RunJobs();
                var target = area == "Garage"
                    ? shell.GetVisualDescendants().OfType<GaragePageView>().Single(g => g.IsEffectivelyVisible).GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Use")
                    : shell.GetVisualDescendants().OfType<SettingsPageView>().Single(s => s.IsEffectivelyVisible).GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Replays");
                target.BringIntoView();
                PumpFor(200, state);
                var at = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
                Assert.True(at.X >= 0 && at.Y >= 0 && at.X <= window.Bounds.Width && at.Y <= window.Bounds.Height, $"{area}: {target.Name} at {at}");
                Shot(window, state, $"next-150-{area.ToLowerInvariant()}");
            }
        });
    }

    [Fact]
    public async Task ReducedMotionStillsWhatRunsByItself()
    {
        var state = State();
        var before = Motion.Reduced;
        try
        {
            await InWindow(state, (window, main) =>
            {
                var shell = Shell(window);
                Motion.Reduced = false;
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("moving", shell.Classes);
                main.Settings.ReducedMotion = true;
                Dispatcher.UIThread.RunJobs();
                Assert.True(Motion.Reduced);
                Assert.DoesNotContain("moving", shell.Classes);
                Assert.True(state.Settings.ReducedMotion);
            });
        }
        finally
        {
            Motion.Reduced = before;
        }
    }

    /// <summary>A colour over another, by its alpha.</summary>
    private static Color Blend(Color top, Color under)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte u) => (byte)Math.Round((t * a) + (u * (1 - a)));
        return Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
    }

    /// <summary>The contrast of two colours, as WCAG 2 measures it.</summary>
    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color c) => (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
}
