using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.Controls;
using Launcher.App.Controls.NextUi;
using Launcher.App.Theme;
using Launcher.App.ViewModels;
using Launcher.App.Views;
using Launcher.App.Views.NextUi;
using static Launcher.Tests.Headless;

namespace Launcher.Tests;

/// <summary>What the tests of the redesigned interface ("Experimental UI") share: the launcher's own window on a state
/// (<see cref="InWindow"/>), its shell, its pages and their rows and hints, and a picture of it for the eye
/// (<c>NEXT_SHOTS</c> naming a folder).</summary>
internal static class NextHeadless
{
    /// <summary>The launcher's window at 1280×720 on <paramref name="state"/>; afterwards the lobby is left, the state
    /// shut down and the window closed.</summary>
    public static Task InWindow(AppState state, Action<MainWindow, MainViewModel> work) => InWindow(state, null, work);

    /// <summary>As <see cref="InWindow(AppState, Action{MainWindow, MainViewModel})"/>, the servers' status asked of
    /// <paramref name="answers"/>.</summary>
    public static async Task InWindow(AppState state, HttpMessageHandler? answers, Action<MainWindow, MainViewModel> work)
    {
        ArgumentNullException.ThrowIfNull(state);
        var ui = HeadlessUnitTestSession.StartNew(typeof(GameSkinTests));
        MainWindow? window = null;
        MainViewModel? main = null;
        try
        {
            await ui.Dispatch(() =>
            {
                GameSkin.Load(null);
                main = new MainViewModel(state, answers);
                window = new MainWindow { DataContext = main, Width = 1280, Height = 720 };
                // no tips: a pointer resting on a control past the tip's delay would open one, and a window without
                // a screen has no system windows for it (nor does the launcher's window theme hold a layer for one)
                ToolTip.SetServiceEnabled(window, false);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                work(window, main);
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            await state.LeaveAsync();
            state.Shutdown();
            await Task.Delay(300, CancellationToken.None);
            await ui.Dispatch(() =>
            {
                window?.Close();
                main?.Dispose();
            }, CancellationToken.None);
            await ui.DisposeAsync();
        }
    }

    /// <summary>With <c>NEXT_SHOTS</c> naming a folder, the window there as <paramref name="name"/>.png.</summary>
    public static void Shot(Window window, AppState state, string name)
    {
        if (Environment.GetEnvironmentVariable("NEXT_SHOTS") is not { Length: > 0 } folder)
        {
            return;
        }
        PumpFor(500, state);
        Aim();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory(folder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    public static NextShell Shell(MainWindow window) => Assert.IsType<NextShell>(Named<ScaledRoot>(window, "ScaleRoot").Child);

    /// <summary>The button that shows and says <paramref name="name"/> (its automation name).</summary>
    public static Button Said(Visual root, string name) =>
        root.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == name);

    /// <summary>The page's view that shows.</summary>
    public static T Page<T>(MainWindow window) where T : Control
    {
        Dispatcher.UIThread.RunJobs();
        return Shell(window).GetVisualDescendants().OfType<T>().Single(c => c.IsEffectivelyVisible);
    }

    /// <summary>The page's own hints' words, as the bar shows them (not the chat's and the main menu's, which every page has).</summary>
    public static string[] Hints(MainViewModel main) => [.. main.Shell.Hints.Where(h => !h.Everywhere).Select(h => h.Label)];

    /// <summary>The control scrolled into its list's view (the settings' groups stand one under another: the last of
    /// them are below the fold), for a click on it.</summary>
    public static T Shown<T>(T control)
        where T : Control
    {
        ArgumentNullException.ThrowIfNull(control);
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    /// <summary>The option row that shows with that label.</summary>
    public static OptionButton Row(Control page, string label)
    {
        Dispatcher.UIThread.RunJobs();
        return page.GetVisualDescendants().OfType<OptionButton>().Single(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == label);
    }

    /// <summary>The focus on a control, as the pad puts it there.</summary>
    public static void Focus(Control control)
    {
        control.BringIntoView();
        control.Focus(NavigationMethod.Directional);
        Dispatcher.UIThread.RunJobs();
    }
}
