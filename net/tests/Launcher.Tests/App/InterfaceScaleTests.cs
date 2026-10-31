using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Controls;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.Core;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>
/// The interface scale: Settings, Launcher, "Interface scale", 50% to 250% in steps of 10 on
/// top of the system's scaling. The slider changes nothing until Apply, which applies and keeps it; Ctrl+0, Ctrl+plus
/// and Ctrl+minus act at once wherever the keyboard is, are kept, and stop at the ends. The whole window takes it,
/// menus too; the 3D views draw at the screen's own pixels under it; the window's least size grows with it, and
/// a window still too small scrolls. The file's value is read defensively: anything outside the range is 100.
/// </summary>
[Collection(ControllerPageTests.Name)]
public sealed class InterfaceScaleTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("launcher-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Data => Path.Combine(_dir, "data");

    private AppState State(string settings)
    {
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "settings.json"), settings);
        return AppState.Load(Data);
    }

    private static async Task OnTheApplication(Action steps)
    {
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        try
        {
            await ui.Dispatch(steps, TestContext.Current.CancellationToken);
        }
        finally
        {
            await ui.DisposeAsync();
        }
    }

    private static async Task OnAWindow(AppState state, Action<MainWindow, MainViewModel> steps)
    {
        try
        {
            await OnTheApplication(() =>
            {
                GameSkin.Load(null);
                var main = new MainViewModel(state);
                var window = new MainWindow { DataContext = main, Width = 1116, Height = 789 };
                window.Show();
                main.Current = main.Home;
                Dispatcher.UIThread.RunJobs();
                try
                {
                    steps(window, main);
                }
                finally
                {
                    window.Close();
                    main.Dispose();
                }
            });
        }
        finally
        {
            state.Shutdown();
        }
    }

    /// <summary>How large a control is drawn in the window, against its own layout size.</summary>
    private static double Drawn(Visual control, Visual window) => control.TransformToVisual(window)!.Value.M11;

    private static ScaledRoot Root(MainWindow window) => Named<ScaledRoot>(window, "ScaleRoot");

    [Theory]
    [InlineData("49", 100)]
    [InlineData("251", 100)]
    [InlineData("300", 100)]
    [InlineData("0", 100)]
    [InlineData("-120", 100)]
    [InlineData("1.5", 100)]
    [InlineData("\"150\"", 100)]
    [InlineData("null", 100)]
    [InlineData("[150]", 100)]
    [InlineData("{\"scale\": 150}", 100)]
    [InlineData("50", 50)]
    [InlineData("250", 250)]
    [InlineData("130", 130)]
    public void TheScaleInTheFileIsOneThereIsOr100(string written, int read)
    {
        var state = State("{\"server\": \"ws://coordinator.test:47810/ws\", \"interfaceScale\": " + written + "}");
        try
        {
            Assert.Equal((read, read, "ws://coordinator.test:47810/ws"),
                (state.Settings.InterfaceScale, state.Settings.ShownInterfaceScale, state.Settings.Server));
        }
        finally
        {
            state.Shutdown();
        }
    }

    [Theory]
    [InlineData(100, 1, 110)]
    [InlineData(100, -1, 90)]
    [InlineData(240, 1, 250)]
    [InlineData(250, 1, 250)]
    [InlineData(60, -1, 50)]
    [InlineData(50, -1, 50)]
    [InlineData(105, 1, 110)]
    [InlineData(105, -1, 100)]
    [InlineData(100, 3, 130)]
    public void AStepIsTenOnTheGridAndStopsAtTheEnds(int from, int steps, int to) =>
        Assert.Equal(to, new LauncherSettings { InterfaceScale = from }.InterfaceScaleStepped(steps));

    [Fact]
    public Task TheSliderChangesNothingUntilApplyWhichKeepsIt() => OnAWindow(State("""{"noServer": true}"""), (window, main) =>
    {
        main.ShowSettings(SettingsTab.Launcher);
        Dispatcher.UIThread.RunJobs();
        var slider = Named<Slider>(window, "InterfaceScale");
        var apply = Named<Button>(window, "ApplyScale");
        var number = Named<Launcher.App.Controls.NumberBox>(window, "InterfaceScaleNumber");
        Assert.Equal((100, 100, false), ((int)slider.Value, number.Value, apply.IsEffectivelyEnabled));
        Assert.Equal(1, Drawn(slider, window), 3);

        // dragged towards the high end with the mouse: the number follows, nothing else changes
        var from = slider.TranslatePoint(new Point(slider.Bounds.Width * 0.2, slider.Bounds.Height / 2), window)!.Value;
        var to = slider.TranslatePoint(new Point(slider.Bounds.Width * 0.7, slider.Bounds.Height / 2), window)!.Value;
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point((from.X + to.X) / 2, from.Y), RawInputModifiers.LeftMouseButton);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var pending = (int)slider.Value;
        Assert.InRange(pending, 160, 210);
        Assert.Equal(0, pending % 10);
        Assert.Equal(pending, number.Value);
        Assert.Equal((100, 1.0), (Now(main).InterfaceScale, main.Scale));
        Assert.Equal(1, Drawn(slider, window), 3);
        Assert.True(apply.IsEffectivelyEnabled);

        // Apply: the whole window at that scale, kept
        Click(window, apply);
        Assert.Equal(pending, Now(main).InterfaceScale);
        Assert.Equal(pending / 100.0, Drawn(slider, window), 3);
        Assert.Equal(pending / 100.0, Drawn(Named<Button>(window, "NoticeBell"), window), 3);
        Assert.False(apply.IsEffectivelyEnabled);
        Assert.Equal(pending, AppState.Load(Data).Settings.InterfaceScale);

        // back to where it was: Apply again
        slider.Value = 100;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(pending, Now(main).InterfaceScale);
        Click(window, apply);
        Assert.Equal((100, 1.0), (Now(main).InterfaceScale, main.Scale));
        Assert.Equal(1, Drawn(slider, window), 3);
    });

    /// <summary>The settings as they are now.</summary>
    private static LauncherSettings Now(MainViewModel main) => main.SoundSettings;

    [Fact]
    public Task TheKeysActAtOnceAnywhereAreKeptAndStopAtTheEnds() => OnAWindow(State("""{"noServer": true}"""), (window, main) =>
    {
        main.ShowSettings(SettingsTab.Launcher);
        Dispatcher.UIThread.RunJobs();
        // the keyboard in a text box: the keys still act there
        var box = Named<TextBox>(window, "DiscImage");
        box.Focus();
        Press(window, Key.OemPlus, PhysicalKey.Equal, RawInputModifiers.Control);
        Assert.Equal((110, 1.1), (Now(main).InterfaceScale, main.Scale));
        Assert.Equal(110, AppState.Load(Data).Settings.InterfaceScale);
        Assert.Equal(110, Named<Launcher.App.Controls.NumberBox>(window, "InterfaceScaleNumber").Value);    // the slider shows it
        Assert.Equal("", box.Text ?? "");                                                // and nothing was typed
        Press(window, Key.Add, PhysicalKey.NumPadAdd, RawInputModifiers.Control);
        Assert.Equal(120, Now(main).InterfaceScale);
        Press(window, Key.D0, PhysicalKey.Digit0, RawInputModifiers.Control);
        Assert.Equal(100, Now(main).InterfaceScale);
        Press(window, Key.OemMinus, PhysicalKey.Minus, RawInputModifiers.Control);
        Press(window, Key.Subtract, PhysicalKey.NumPadSubtract, RawInputModifiers.Control);
        Assert.Equal(80, Now(main).InterfaceScale);
        // without Ctrl a key is a key
        Press(window, Key.OemMinus, PhysicalKey.Minus);
        Assert.Equal(80, Now(main).InterfaceScale);

        for (var i = 0; i < 30; i++)
        {
            Press(window, Key.OemPlus, PhysicalKey.Equal, RawInputModifiers.Control);
        }
        Assert.Equal((250, 2.5), (Now(main).InterfaceScale, main.Scale));
        Assert.Equal(2.5, Drawn(box, window), 3);
        for (var i = 0; i < 30; i++)
        {
            Press(window, Key.OemMinus, PhysicalKey.Minus, RawInputModifiers.Control);
        }
        Assert.Equal((50, 0.5), (Now(main).InterfaceScale, main.Scale));
        Assert.Equal(0.5, Drawn(box, window), 3);
        Press(window, Key.NumPad0, PhysicalKey.NumPad0, RawInputModifiers.Control);
        Assert.Equal(100, AppState.Load(Data).Settings.InterfaceScale);
    });

    [Fact]
    public Task TheWindowsLeastSizeGrowsAndAWindowTooSmallScrolls() => OnAWindow(State("""{"noServer": true, "interfaceScale": 200}"""), (window, main) =>
    {
        Assert.Equal(2.0, main.Scale);
        var room = window.Screens?.ScreenFromWindow(window)?.WorkingArea.Size.ToSize(1);
        Assert.Equal(Math.Min(2560, room?.Width ?? double.MaxValue), window.MinWidth);
        Assert.Equal(Math.Min(1440, room?.Height ?? double.MaxValue), window.MinHeight);
        // a window smaller than that: the content keeps its room at the scale and scrolls, nothing is cut off
        window.MinWidth = window.MinHeight = 0;
        window.Width = 1116;
        window.Height = 789;
        Dispatcher.UIThread.RunJobs();
        var scroll = Named<ScrollViewer>(window, "ScaleScroll");
        Assert.True(Root(window).Bounds.Width >= 2560 && Root(window).Bounds.Height >= 1440, $"{Root(window).Bounds}");
        Assert.True(scroll.Extent.Width > scroll.Viewport.Width && scroll.Extent.Height > scroll.Viewport.Height);
        // at 100% the window is as it was: no scrolling
        main.StepScale(0);
        window.Width = 1366;
        window.Height = 728;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((1280, 720), (window.MinWidth, window.MinHeight));
        Assert.Equal(scroll.Viewport, scroll.Extent);
        Assert.Equal(new Size(1366, 728), Root(window).Bounds.Size);
    });

    /// <summary>The 3D views are laid out in the screen's own units under the scale: their pictures are drawn at
    /// the size they show, neither stretched (blurred) nor larger than shown. Each of the window's is in an
    /// <see cref="Unscaled"/>, which lays what it holds out so.</summary>
    [Fact]
    public Task TheThreeDViewsDrawAtTheScreensOwnPixels() => OnTheApplication(() =>
    {
        GameSkin.Load(null);
        var state = State("{\"noServer\": true, \"interfaceScale\": 250}");
        var main = new MainViewModel(state);
        var window = new MainWindow { DataContext = main, Width = 1116, Height = 789 };
        window.Show();
        main.Current = main.Garage;
        Dispatcher.UIThread.RunJobs();
        var views = window.GetVisualDescendants().OfType<MachinePreview>().ToList();
        Assert.Equal(2, views.Count);                                   // the left column's and the Garage's
        Assert.All(views, v => Assert.IsType<Unscaled>(v.GetVisualParent()));
        Assert.Contains(new CourseMap().GetVisualChildren(), c => c is Unscaled { Child: Launcher.App.Gpu.GpuView });   // the race map's
        window.Close();
        main.Dispose();
        state.Shutdown();

        // what an Unscaled holds is laid out at the scaled size, in the screen's units
        var inner = new Border();
        var place = new Unscaled { Child = inner, Width = 200, Height = 100 };
        var shown = WithMenus(new ScaledRoot { Scale = 2.5, Child = new StackPanel { Children = { place } } }, 900, 600);
        shown.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2.5, Drawn(place, shown), 3);
        Assert.Equal(1, Drawn(inner, shown), 3);
        Assert.Equal(new Size(500, 250), inner.Bounds.Size);
        shown.Close();
    });

    /// <summary>A menu is a window of its own: it takes the scale of the control it opens at.</summary>
    [Fact]
    public Task AMenuTakesTheScaleOfWhereItOpens() => OnTheApplication(() =>
    {
        var inside = new TextBlock { Text = "Save" };
        var button = new Button { Content = "Name", Flyout = new Flyout { Content = inside } };
        var root = new ScaledRoot { Scale = 2, Child = new StackPanel { Children = { button } } };
        var window = WithMenus(root, 600, 400);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Click(window, button);
        Pump(() => inside.IsEffectivelyVisible, "the menu did not open");
        Assert.Equal(2, Drawn(inside, TopLevel.GetTopLevel(inside)!), 3);
        window.Close();
    });
}
