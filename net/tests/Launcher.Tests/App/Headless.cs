using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Launcher.App;
using Launcher.App.ViewModels;
using Launcher.App.Views;

namespace Launcher.Tests;

/// <summary>What the tests of a page in a window without a screen do with it, as a player would: press with the
/// headless window's mouse (never a command, which skips what a press does), type, and wait for the page.</summary>
internal static class Headless
{
    /// <summary>A click in the middle of a control.</summary>
    public static void Click(Window window, Control control, MouseButton button = MouseButton.Left)
    {
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, button);
        window.MouseUp(at, button);
        Dispatcher.UIThread.RunJobs();
    }

    public static void Press(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Lets the window's thread work until <paramref name="done"/>, ten seconds at most.</summary>
    public static void Pump(Func<bool> done, string what = "the page did not get there in ten seconds")
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        Dispatcher.UIThread.RunJobs();
        while (!done() && DateTime.UtcNow < limit)
        {
            Thread.Sleep(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(done(), what);
    }

    /// <summary>As <see cref="Pump"/>, and meanwhile says on this thread, as a change of the settings does, that
    /// something changed: what a session says on its own threads does not arrive at a test's window by itself (each
    /// test's window has a thread of its own, and "the application's thread" named from another is not it).</summary>
    public static void PumpTelling(AppState state, Func<bool> done, string what = "the page did not get there in ten seconds", int seconds = 10)
    {
        var limit = DateTime.UtcNow.AddSeconds(seconds);
        Dispatcher.UIThread.RunJobs();
        while (!done() && DateTime.UtcNow < limit)
        {
            Thread.Sleep(20);
            state.Update(s => s);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(done(), what);
    }

    /// <summary>Lets the window's thread work for a while.</summary>
    public static void PumpFor(int ms, AppState? telling = null)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            Thread.Sleep(10);
            telling?.Update(s => s);
            Dispatcher.UIThread.RunJobs();
        }
    }

    public static async Task Until(Func<bool> met, CancellationToken cancel, string what = "not within ten seconds")
    {
        for (var i = 0; i < 500 && !met(); i++)
        {
            await Task.Delay(20, cancel);
        }
        Assert.True(met(), what);
    }

    /// <summary>A control of the window by its name.</summary>
    public static T Named<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    /// <summary>A window with the toolkit's layer for menus, which a flyout needs where there are no system windows
    /// and the launcher's own window theme lacks.</summary>
    public static Window WithMenus(Control content, double width, double height) => new()
    {
        Content = content, Width = width, Height = height,
        Template = new Avalonia.Controls.Templates.FuncControlTemplate<Window>((window, scope) =>
        {
            var presenter = new Avalonia.Controls.Presenters.ContentPresenter
            {
                Name = "PART_ContentPresenter",
                [!Avalonia.Controls.Presenters.ContentPresenter.ContentProperty] = window[!ContentControl.ContentProperty],
            };
            var layers = new Avalonia.Controls.Primitives.VisualLayerManager { Name = "PART_VisualLayerManager", Child = presenter };
            scope.Register(presenter.Name, presenter);
            scope.Register(layers.Name, layers);
            return layers;
        }),
    };

    /// <summary>The Garage's roster shows the whole shelf at once (SELECT): its stock machines' tiles,
    /// in its order (row by row, left to right).</summary>
    public static List<Button> PageCards(Control view) =>
        [.. GarageIn(view).RosterCards().Where(b => b.DataContext is GarageCard)
            .OrderBy(b => Math.Round(b.TranslatePoint(default, view)!.Value.Y)).ThenBy(b => b.TranslatePoint(default, view)!.Value.X)];

    /// <summary>Every stock machine's tile of the shelf, laid out, in the roster's order. From PREVIEW, as a player sees
    /// them: back to SELECT, and the same PREVIEW again after (its machine, its tab).</summary>
    public static void ForEachCard(Control view, Action<Button> check)
    {
        var garage = (GarageViewModel)GarageIn(view).DataContext!;
        if (!garage.Previewing)
        {
            foreach (var card in ShownRoster(view, garage))
            {
                check(card);
            }
            return;
        }
        var (machine, custom, section) = (garage.MachineIndex, garage.CustomShown, garage.Section);
        garage.BackCommand.Execute(null);
        try
        {
            foreach (var card in ShownRoster(view, garage))
            {
                check(card);
            }
        }
        finally
        {
            if (custom)
            {
                garage.ShowShelfCommand.Execute(GarageViewModel.CustomShelf);
                garage.ShowStepCommand.Execute(garage.PhaseSteps[GarageViewModel.PreviewPhase]);
            }
            else
            {
                garage.PreviewMachineCommand.Execute(machine);
            }
            garage.Section = section;
            PumpFor(50);
        }
    }

    /// <summary>The machines of every stock machine's tile on the shelf, in the roster's order.</summary>
    public static List<int> ShelfMachines(Control view)
    {
        var machines = new List<int>();
        ForEachCard(view, card => machines.Add(((GarageCard)card.DataContext!).Id));
        return machines;
    }

    /// <summary>A machine's tile (from PREVIEW, back in SELECT first, as a player goes to press one); null when the
    /// shelf does not hold it.</summary>
    public static Button? CardOf(Control view, int machine)
    {
        var garage = (GarageViewModel)GarageIn(view).DataContext!;
        if (garage.Previewing)
        {
            garage.BackCommand.Execute(null);
        }
        if (!garage.Shelved().Any(c => c is GarageCard g && g.Id == machine))
        {
            return null;
        }
        return ShownRoster(view, garage).Single(b => ((GarageCard)b.DataContext!).Id == machine);
    }

    /// <summary>The roster's stock tiles once they are laid out as the shelf has them.</summary>
    private static List<Button> ShownRoster(Control view, GarageViewModel garage)
    {
        Pump(() => PageCards(view).Select(b => b.DataContext).SequenceEqual(garage.Roster.OfType<GarageCard>()), "the roster was not laid out");
        return PageCards(view);
    }

    private static GarageView GarageIn(Control view) => view as GarageView ?? view.GetVisualDescendants().OfType<GarageView>().First();
}
