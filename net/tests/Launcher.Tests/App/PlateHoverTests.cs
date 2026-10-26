using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Controls;

namespace Launcher.Tests;

/// <summary>
/// A leaning plate under the pointer (Controls/Plate; RACE flickered between taking a click
/// and not, then a press on the green did nothing, only one on the word). The plate eases one tone up
/// under the pointer and draws itself anew each frame of it; the toolkit hit-tests a drawn shape only once the render
/// thread has it, so the fill dropped out from under the pointer each frame and the hover came and went. These hold
/// the pointer on the fill (never the word): at the middle on either side of the word, by the leaning edges and in
/// every corner of the button's box, frame after frame, and press there.
/// </summary>
[Collection(HeadlessApp.Name)]
public sealed class PlateHoverTests
{
    public static AppBuilder BuildAvaloniaApp() => HeadlessApp.BuildAvaloniaApp();

    private static async Task OnTheApplication(Action steps)
    {
        var ui = HeadlessUnitTestSession.StartNew(typeof(PlateHoverTests));
        try
        {
            await ui.Dispatch(steps, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
        }
    }

    /// <summary>Frames of the window: time passes, the render timer ticks (transitions step, the plate redraws, the
    /// pointer is looked for again), the window's thread works.</summary>
    private static void Frames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            Thread.Sleep(16);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Points of a plate's fill, in the button's own coordinates: beside the word at the middle, just inside
    /// each leaning edge top and bottom, and the box's four corners (outside the cut shape, still the button's).</summary>
    private static (string Where, Point At)[] FillPoints(Button button, Control word)
    {
        var (w, h) = (button.Bounds.Width, button.Bounds.Height);
        var cut = h * 0.2679;
        var text = word.TranslatePoint(default, button)!.Value;
        return
        [
            ("left of the word", new(text.X / 2, h / 2)),
            ("right of the word", new((text.X + word.Bounds.Width + w) / 2, h / 2)),
            ("by the left edge, top", new(cut + 3, 3)),
            ("by the left edge, bottom", new(3, h - 3)),
            ("by the right edge, top", new(w - 3, 3)),
            ("by the right edge, bottom", new(w - cut - 3, h - 3)),
            ("the box's top left corner", new(1, 1)),
            ("the box's bottom right corner", new(w - 1, h - 1)),
        ];
    }

    /// <summary>The pointer comes onto each point from outside the button and stays twenty frames (the hover's
    /// easing and well past it); the button is under it in every frame, never let go of, never off; a press there
    /// takes it. What went wrong is said point by point.</summary>
    private static void HoldsAndPresses(string classes, string word)
    {
        var pressed = 0;
        var text = new TextBlock { Text = word };
        var button = new Button { Content = text, Width = 280, Command = new RelayCommand(() => pressed++) };
        button.Classes.AddRange(classes.Split(' '));
        var window = new Window
        {
            Content = new StackPanel { Margin = new Thickness(40), Children = { button } }, Width = 400, Height = 200,
        };
        window.Show();
        Frames(3);
        var plate = button.GetVisualDescendants().OfType<Plate>().Single();
        var events = new List<string>();
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == InputElement.IsPointerOverProperty || e.Property == InputElement.IsEnabledProperty)
            {
                events.Add($"{e.Property.Name} {e.NewValue}");
            }
        };
        var wrong = new List<string>();
        try
        {
            foreach (var (where, point) in FillPoints(button, text))
            {
                var at = button.TranslatePoint(point, window)!.Value;
                window.MouseMove(new Point(2, 2));
                Frames(12);
                events.Clear();
                window.MouseMove(at);
                var hit = window.InputHitTest(at);
                if (hit != plate)
                {
                    wrong.Add($"{where}: the pointer finds {hit?.GetType().Name ?? "nothing"}, not the plate");
                }
                var lost = new List<int>();
                for (var f = 0; f < 20; f++)
                {
                    Frames(1);
                    if (!button.IsPointerOver)
                    {
                        lost.Add(f);
                    }
                }
                if (lost.Count > 0 || events.Any(e => e != "IsPointerOver True"))
                {
                    wrong.Add($"{where}: off the button in frames {string.Join(",", lost)}; it went {string.Join(", ", events)}");
                }
                var before = pressed;
                window.MouseDown(at, MouseButton.Left);
                Frames(2);
                window.MouseUp(at, MouseButton.Left);
                Frames(2);
                if (pressed != before + 1)
                {
                    wrong.Add($"{where}: a press did not take");
                }
            }
        }
        finally
        {
            window.Close();
        }
        Assert.True(wrong.Count == 0, $"{classes}:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public async Task RaceKeepsThePointerOnItsFillAndTakesAPressThere() =>
        await OnTheApplication(() => HoldsAndPresses("go", "RACE"));

    [Fact]
    public async Task AModeTabKeepsThePointerOnItsPlateOnAndOff() =>
        await OnTheApplication(() =>
        {
            HoldsAndPresses("section on", "QUICK PLAY");
            HoldsAndPresses("section", "LOBBY");
        });
}
