using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Launcher.App.ViewModels;
using Launcher.Core;

namespace Launcher.App;

/// <summary>
/// A replay opened with a double-click in Explorer (<see cref="ReplayFileType"/>): the file type registered for the
/// launcher a player has, and the file played in the launcher's window, the one open already when there is one
/// (<see cref="ReplayHandOver"/>), as the Replays page's "Open a file…" plays it (<see cref="MainViewModel.OpenReplay"/>).
/// <para>Who registers the type: an installed launcher in Velopack's hooks (Setup and each update register it, the
/// uninstall takes it away: <see cref="Hooks"/>) and at each start of its window; a portable launcher or player copy
/// at its window's start (<see cref="ClaimAtStart"/>). Never a test's launcher (one with a launch file), dev mode,
/// a development copy, or a start from .NET's own host. On another system than Windows nothing is registered.</para>
/// </summary>
public static partial class ReplayOpening
{
    /// <summary>The longest wait for the open window to take a file; after it this start opens the window itself.</summary>
    private static readonly TimeSpan HandOverWait = TimeSpan.FromSeconds(5);

    /// <summary>Velopack's hooks of an installed launcher (Program.Main): the type registered after Setup and after
    /// each update, taken away before the uninstall. Each runs in a process Velopack ends at once after it.</summary>
    public static Velopack.VelopackApp Hooks(Velopack.VelopackApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return OperatingSystem.IsWindows()
            ? app.OnAfterInstallFastCallback(_ => Register())
                .OnAfterUpdateFastCallback(_ => Register())
                .OnBeforeUninstallFastCallback(_ => Unregister())
            : app;   // (Velopack runs them on Windows only)
    }

    /// <summary>The start of a window whose launcher is a player's (no launch file, no dev mode): the type taken when
    /// this launcher owns it (<see cref="ReplayFileType.Takes"/>), on a worker (the registry is not the window's
    /// business).</summary>
    public static void ClaimAtStart(CommandLine line, Action<string>? note)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!ReferenceEquals(line.Debug, DebugSettings.Off) || line.DevService)
        {
            return;
        }
        _ = Task.Run(() => Register(note));
    }

    /// <summary>A start with a replay file (<see cref="CommandLine.ReplayToOpen"/>): the file handed to the window
    /// open on this data folder. True: that window took it, and this start ends; false: this start opens the window
    /// itself and plays it there.</summary>
    public static bool HandOver(CommandLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.ReplayToOpen is not { } file)
        {
            return false;
        }
        if (OperatingSystem.IsWindows())
        {
            // the start that Explorer made may bring a window to the front; it lets the open window do so
            _ = AllowSetForegroundWindow(AnyProcess);
        }
        return ReplayHandOver.Send(ReplayHandOver.NameOf(AppState.DataFolderOf(line.DataFolder)), file, HandOverWait);
    }

    /// <summary>The window listens for files handed over (until disposed), and plays the one this start was given
    /// once the window is open and the mods' list read (a game start waits for it: Program.Main).</summary>
    public static IDisposable Listen(AppState state, MainViewModel main, Window window, string? first)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(window);
        var listening = ReplayHandOver.Listen(ReplayHandOver.NameOf(state.DataFolder),
            file => Dispatcher.UIThread.Post(() => Open(state, main, window, file)), line => Program.Faults?.Note(line));
        if (first is not null)
        {
            window.Opened += async (_, _) =>
            {
                await state.ModsRead.ConfigureAwait(true);
                Open(state, main, window, first);
            };
        }
        return listening;
    }

    /// <summary>A file opened in the window (on its thread): the window in front, the Replays page playing it.</summary>
    public static void Open(AppState state, MainViewModel main, Window window, string file)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(window);
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Activate();
        if (!main.OpenReplay(file))
        {
            Program.Faults?.Note($"replay not opened (the first screen shows): {file}");
        }
    }

    /// <summary>The type registered for this launcher when it owns it; the shell told of a change.</summary>
    private static void Register(Action<string>? note = null)
    {
        if (!OperatingSystem.IsWindows() || OwnProgram() is not { } program)
        {
            return;
        }
        try
        {
            if (ReplayFileType.Claim(new WindowsClasses(), Install.This, program, Branding.ReplayTypeName))
            {
                Changed();
                note?.Invoke($"replays open with {program}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            note?.Invoke($"the replay file type was not registered ({e.GetType().Name}: {e.Message})");
        }
    }

    /// <summary>This installed launcher's registration taken away (another launcher's is left).</summary>
    private static void Unregister()
    {
        if (!OperatingSystem.IsWindows() || OwnProgram() is not { } program)
        {
            return;
        }
        try
        {
            if (ReplayFileType.Release(new WindowsClasses(), program))
            {
                Changed();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // the uninstall goes on: the type then names a program that has gone, which a portable copy takes over
        }
    }

    /// <summary>The launcher's own program file; null when it is .NET's host (a development start).</summary>
    private static string? OwnProgram() =>
        Environment.ProcessPath is { } program &&
        !string.Equals(Path.GetFileNameWithoutExtension(program), "dotnet", StringComparison.OrdinalIgnoreCase)
            ? program : null;

    /// <summary>Explorer told that file types changed, so that the replays' icon and type show at once.</summary>
    private static void Changed()
    {
        if (OperatingSystem.IsWindows())
        {
            SHChangeNotify(AssociationsChanged, 0, 0, 0);
        }
    }

    private const int AssociationsChanged = 0x08000000;   // SHCNE_ASSOCCHANGED (with SHCNF_IDLIST, no items)
    private const int AnyProcess = -1;                    // ASFW_ANY

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int process);
}
