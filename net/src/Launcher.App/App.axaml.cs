using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Launcher.App.ViewModels;
using Launcher.App.Views;

namespace Launcher.App;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Watch(Avalonia.Threading.Dispatcher.UIThread, Program.Faults);
            var state = AppState.Load(Program.Line.DataFolder, Program.Line.Debug, standIn: Program.Line.DevService);
            state.TaskbarGroup = Taskbar.Group;   // its games' windows stand under this launcher's taskbar button
            // the handler of last resort tells the player as the launcher's other notices are shown; a connection
            // ended by an error (the session says so itself) is only written
            if (Program.Faults is { } faults)
            {
                faults.Tell = notice => state.Report(notice, faults.LogFolder);
            }
            state.Fault += (where, error) => Program.Faults?.Caught(where, error, tell: false);
            state.Noted += line => Program.Faults?.Note(line);
            if (ReferenceEquals(state.Debug, Launcher.Core.DebugSettings.Off) && !state.StandIn)
            {
                // Discord's status, while the player's setting is on; never a test's launcher (one with a launch
                // file), which would show on the Discord of whoever's PC it runs on
                state.StartPresence();
            }
            // the draw's blip and chime are made on a worker now, not on the first hop of the first draw
            _ = Task.Run(Launcher.Core.DrawSounds.Warm);
            // the launcher's own updates (an installed launcher's; Updates.cs): never in a test's launcher, which has a
            // launch file and must stay the build it is
            // (nor in dev mode, on the stand-in service)
            var updates = ReferenceEquals(state.Debug, Launcher.Core.DebugSettings.Off) && !state.StandIn
                ? new Updates(() => state.Racing, line => Program.Faults?.Note(line)) : null;
            var main = Pages(state, updates);
            var window = new MainWindow { DataContext = main };
            ForTests(window, main, state.Debug);
            desktop.MainWindow = window;
            // a restart (to put an update in, after an older copy's data came over) closes as the player's close does
            Relaunch.Close = window.Close;
            desktop.ShutdownRequested += (_, _) =>
            {
                updates?.Dispose();
                main.Dispose();
                state.Shutdown();
            };
            main.Update.Start();
            if (state.Debug.Fault is { } route)
            {
                Avalonia.Threading.DispatcherTimer.RunOnce(() => Program.TestFault(route), TimeSpan.FromSeconds(2));
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The window's pages on the look of the player's disc, as a start makes them: the look is loaded, then
    /// followed (when the settings name another disc it is loaded on the window's thread), and only then the pages
    /// are made. **The order is relied on**: a page that shows what the look read from the disc (the Garage: the
    /// machines' stats) looks again on the same change of the settings, and must come after the look's own handler,
    /// or it would read the disc that was there before.</summary>
    /// <param name="updates">The launcher's own updates; null: none (a test's pages).</param>
    public static MainViewModel Pages(AppState state, Updates? updates = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        _ = state.LookForTheGame();   // what a game start has: read on another thread while the look is loaded here
        Theme.GameSkin.DataFolder = state.DataFolder;   // where the Practice page's course stills are kept
        Theme.GameSkin.Own = (state.Settings.Racer.Raced, (state.Settings.Practice ?? Launcher.Core.PracticeSetup.Default).Course);
        Theme.GameSkin.Load(state.Settings.DiscPath);
        state.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => Theme.GameSkin.Load(state.Settings.DiscPath));
        return new MainViewModel(state, updates: updates);
    }

    /// <summary>An error nothing caught in a job of the window's thread (a click's command, a screen refreshed
    /// after a message, a timer) closed the launcher: it goes to the handler of last resort
    /// (<see cref="Launcher.Core.LastResort"/>: written to the log, the player told once) and the launcher goes
    /// on. The other routes are <see cref="Program"/>'s.</summary>
    public static void Watch(Avalonia.Threading.Dispatcher window, Launcher.Core.LastResort? faults)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (faults is not null)
        {
            window.UnhandledException += (_, e) => e.Handled = faults.Caught("on the window's thread (the launcher goes on)", e.Exception);
        }
    }

    /// <summary>A test's window (a launch file's "title", "windowAt", "windowPage"): named and placed as asked,
    /// opened without taking the keyboard from whoever is at the computer.</summary>
    private static void ForTests(MainWindow window, MainViewModel main, Launcher.Core.DebugSettings debug)
    {
        if (debug.Title is { } title)
        {
            window.Title = title;
        }
        if (debug.WindowAt is var (left, top))
        {
            window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual;
            window.Position = new PixelPoint(left, top);
            window.ShowActivated = false;
        }
        if (debug.WindowPage is { } page && main.Navigation)
        {
            main.ShowTabCommand.Execute(page);
        }
    }
}
